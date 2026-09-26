using AniLingo.Web.Data;
using AniLingo.Web.Features.Artwork;
using AniLingo.Web.Features.MediaSegments;
using AniLingo.Web.Features.Metadata;
using AniLingo.Web.Features.Sonarr;
using AniLingo.Web.Features.Subtitles;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Library;

public sealed class LibraryScanner(
    AppDbContext db,
    SubtitleImportService subtitleImport,
    EmbeddedSubtitleExtractor embeddedSubtitleExtractor,
    SonarrArtworkSyncService sonarrArtworkSync,
    ILogger<LibraryScanner> logger,
    AnimeMetadataService? metadataService = null,
    MediaSegmentSidecarImporter? segmentSidecars = null)
{
    internal static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".webm"
    };

    public async Task<ScanResult> ScanAsync(Guid rootId, CancellationToken cancellationToken)
    {
        var root = await db.LibraryRoots.SingleAsync(x => x.Id == rootId, cancellationToken);
        if (!root.IsEnabled)
        {
            return new ScanResult(0, 0, 0, 0);
        }

        var rootPath = Path.GetFullPath(root.Path);
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Library root does not exist: {rootPath}");
        }

        var candidates = new List<FileInfo>();
        var nfoFiles = new NfoFileIndex();
        try
        {
            foreach (var path in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
            {
                if (MediaExtensions.Contains(Path.GetExtension(path)))
                {
                    candidates.Add(new FileInfo(path));
                }
                else if (NfoFileIndex.IsNfo(path))
                {
                    nfoFiles.Add(path);
                }
            }

            // Ordinal order makes the "first file wins" rules (series folder, NFO title) deterministic.
            candidates.Sort((left, right) => string.CompareOrdinal(left.FullName, right.FullName));
        }
        catch (IOException exception)
        {
            throw new IOException(
                $"Library root could not be enumerated safely: {rootPath}",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new IOException(
                $"Library root could not be enumerated safely: {rootPath}",
                exception);
        }

        var observedMediaPaths = candidates
            .Select(file => Path.GetFullPath(file.FullName))
            .ToHashSet(StringComparer.Ordinal);

        var existingFiles = await db.MediaFiles
            .Where(x => x.LibraryRootId == rootId)
            .ToDictionaryAsync(x => x.Path, StringComparer.Ordinal, cancellationToken);

        if (existingFiles.Count > 0 && candidates.Count == 0)
        {
            throw new IOException(
                "Library root returned no media files while AniLingo still has known media for it. " +
                "Reconciliation was stopped to avoid treating an unavailable NAS mount as a mass deletion.");
        }

        var animeByKey = await db.Anime.ToDictionaryAsync(x => x.Key, StringComparer.Ordinal, cancellationToken);
        var episodes = await db.Episodes.ToListAsync(cancellationToken);
        var episodeByKey = episodes.ToDictionary(
            x => (x.AnimeId, x.SeasonNumber, x.Number),
            x => x);

        var discovered = 0;
        var updated = 0;
        var skipped = 0;
        var subtitleCandidates = new List<SubtitleCandidate>();
        var artworkDirectories = new Dictionary<Guid, string>();
        var newlyDiscoveredAnimeIds = new HashSet<Guid>();
        var nfoAniListIds = new Dictionary<Guid, string>();
        var nfoTitledEpisodes = new HashSet<(Guid AnimeId, int SeasonNumber, int EpisodeNumber)>();
        var metadataWarnings = 0;

        foreach (var file in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var normalizedPath = Path.GetFullPath(file.FullName);
            if (!MediaPathParser.TryParse(rootPath, normalizedPath, out var descriptor))
            {
                skipped++;
                continue;
            }

            if (!animeByKey.TryGetValue(descriptor.AnimeKey, out var anime))
            {
                anime = new Anime { Key = descriptor.AnimeKey, Title = descriptor.AnimeTitle };
                animeByKey.Add(anime.Key, anime);
                db.Anime.Add(anime);
                newlyDiscoveredAnimeIds.Add(anime.Id);
            }

            var animeDirectory = TryGetAnimeDirectory(rootPath, normalizedPath);
            if (animeDirectory is not null &&
                artworkDirectories.TryAdd(anime.Id, animeDirectory) &&
                nfoFiles.FindShow(animeDirectory) is { } showNfoPath)
            {
                var show = NfoReader.ReadShow(showNfoPath);
                if (show.Value is null)
                {
                    metadataWarnings++;
                    LogRejectedNfo(showNfoPath, show.Warning);
                }
                else
                {
                    if (show.Value.Title is { } showTitle &&
                        !string.Equals(anime.Title, showTitle, StringComparison.Ordinal))
                    {
                        anime.Title = showTitle;
                        if (!newlyDiscoveredAnimeIds.Contains(anime.Id))
                        {
                            updated++;
                        }
                    }

                    if (show.Value.ProviderIds.AniList is { } aniListId)
                    {
                        nfoAniListIds[anime.Id] = aniListId;
                    }
                }
            }

            var episodeKey = (anime.Id, descriptor.SeasonNumber, descriptor.EpisodeNumber);
            var episodeTitle = ResolveEpisodeTitle(
                nfoFiles.FindEpisode(normalizedPath),
                descriptor,
                nfoTitledEpisodes.Contains(episodeKey),
                ref metadataWarnings,
                out var titleFromNfo);
            if (titleFromNfo)
            {
                nfoTitledEpisodes.Add(episodeKey);
            }

            if (!episodeByKey.TryGetValue(episodeKey, out var episode))
            {
                episode = new Episode
                {
                    AnimeId = anime.Id,
                    SeasonNumber = descriptor.SeasonNumber,
                    Number = descriptor.EpisodeNumber,
                    Title = episodeTitle ?? descriptor.EpisodeTitle
                };
                episodeByKey.Add(episodeKey, episode);
                db.Episodes.Add(episode);
            }
            else if (episodeTitle is not null &&
                     !string.Equals(
                         episode.Title,
                         episodeTitle,
                         StringComparison.Ordinal))
            {
                episode.Title = episodeTitle;
                updated++;
            }

            var lastWrite = file.LastWriteTimeUtc;
            if (existingFiles.TryGetValue(normalizedPath, out var mediaFile))
            {
                if (mediaFile.SizeBytes != file.Length || mediaFile.LastWriteTimeUtc != lastWrite)
                {
                    mediaFile.SizeBytes = file.Length;
                    mediaFile.LastWriteTimeUtc = lastWrite;
                    updated++;
                }
            }
            else
            {
                mediaFile = new MediaFile
                {
                    LibraryRootId = rootId,
                    EpisodeId = episode.Id,
                    Path = normalizedPath,
                    SizeBytes = file.Length,
                    LastWriteTimeUtc = lastWrite
                };
                db.MediaFiles.Add(mediaFile);
                existingFiles.Add(normalizedPath, mediaFile);
                discovered++;
            }

            subtitleCandidates.Add(new SubtitleCandidate(
                episode.Id,
                normalizedPath,
                lastWrite));
        }

        var staleMediaFiles = existingFiles.Values
            .Where(mediaFile => !observedMediaPaths.Contains(mediaFile.Path))
            .ToArray();

        if (staleMediaFiles.Length > 0)
        {
            db.MediaFiles.RemoveRange(staleMediaFiles);
        }

        root.LastScannedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        if (metadataService is not null)
        {
            foreach (var animeId in newlyDiscoveredAnimeIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (nfoAniListIds.TryGetValue(animeId, out var aniListId))
                    {
                        await MatchNfoAniListIdAsync(
                            metadataService,
                            animeId,
                            aniListId,
                            cancellationToken);
                    }

                    var decision = await metadataService.AutoMatchAsync(
                        animeId,
                        cancellationToken);
                    if (decision.CanApply && decision.Candidate is not null)
                    {
                        logger.LogInformation(
                            "Automatically matched anime {AnimeId} to {Provider}:{ExternalId} with score {Score}.",
                            animeId,
                            decision.Candidate.Provider,
                            decision.Candidate.ExternalId,
                            decision.Score);
                    }

                    var episodeMapping = await metadataService.AutoMapEpisodeRangesAsync(
                        animeId,
                        cancellationToken);
                    if (episodeMapping.Applied)
                    {
                        logger.LogInformation(
                            "Automatically mapped {RangeCount} AniList episode range(s) for anime {AnimeId}.",
                            episodeMapping.Mappings.Count,
                            animeId);
                    }
                }
                catch (Exception exception) when (
                    exception is MetadataProviderException or InvalidOperationException)
                {
                    logger.LogWarning(
                        exception,
                        "Automatic metadata matching failed for anime {AnimeId}; the local library scan remains valid.",
                        animeId);
                }
            }
        }

        var removed = staleMediaFiles.Length;
        if (removed > 0)
        {
            var staleEpisodeIds = staleMediaFiles
                .Select(x => x.EpisodeId)
                .Distinct()
                .ToArray();

            var orphanEpisodeIds = await db.Episodes
                .Where(episode =>
                    staleEpisodeIds.Contains(episode.Id) &&
                    !db.MediaFiles.Any(media => media.EpisodeId == episode.Id))
                .Select(episode => episode.Id)
                .ToArrayAsync(cancellationToken);

            if (orphanEpisodeIds.Length > 0)
            {
                var orphanEpisodes = await db.Episodes
                    .Where(x => orphanEpisodeIds.Contains(x.Id))
                    .ToArrayAsync(cancellationToken);
                db.Episodes.RemoveRange(orphanEpisodes);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        await sonarrArtworkSync.SyncIfConfiguredAsync(cancellationToken);

        if (segmentSidecars is not null)
        {
            await segmentSidecars.ReconcileRootAsync(rootId, cancellationToken);
        }

        var localArtworkImported = 0;
        var localArtworkUnchanged = 0;
        foreach (var (animeId, animeDirectory) in artworkDirectories)
        {
            var artwork = await LocalAnimeArtworkImporter.ImportAsync(
                animeId,
                animeDirectory,
                cancellationToken);
            localArtworkImported += artwork.ImportedCount;
            localArtworkUnchanged += artwork.UnchangedCount;
        }

        var episodeIds = subtitleCandidates
            .Select(x => x.EpisodeId)
            .Distinct()
            .ToArray();

        var embeddedTracks = episodeIds.Length == 0
            ? new List<ExistingEmbeddedTrack>()
            : await db.SubtitleTracks
                .AsNoTracking()
                .Where(x =>
                    episodeIds.Contains(x.EpisodeId) &&
                    x.Path.StartsWith(EmbeddedSubtitleExtractor.SourcePrefix))
                .Select(x => new ExistingEmbeddedTrack(
                    x.EpisodeId,
                    x.Path,
                    x.SourceUpdatedAt))
                .ToListAsync(cancellationToken);

        var subtitleFiles = 0;
        var sidecarListings = new SubtitleSidecarDirectoryCache();
        foreach (var episodeCandidates in subtitleCandidates.GroupBy(x => x.EpisodeId))
        {
            var episodeId = episodeCandidates.Key;
            var mediaCandidates = episodeCandidates
                .OrderBy(x => x.MediaPath, StringComparer.Ordinal)
                .ToArray();

            var sidecar = await subtitleImport.ImportPreferredSidecarAsync(
                episodeId,
                mediaCandidates.Select(x => x.MediaPath).ToArray(),
                sidecarListings,
                cancellationToken);

            if (sidecar.Status == SubtitleSidecarImportStatus.Imported)
            {
                subtitleFiles++;
                continue;
            }

            if (sidecar.Status == SubtitleSidecarImportStatus.Unavailable)
            {
                continue;
            }

            foreach (var candidate in mediaCandidates)
            {
                var sourcePrefix = EmbeddedSubtitleExtractor.BuildSourcePrefix(candidate.MediaPath);
                var freshEmbeddedCount = embeddedTracks.Count(x =>
                    x.EpisodeId == episodeId &&
                    x.SourceUpdatedAt == candidate.SourceUpdatedAt &&
                    x.SourceKey.StartsWith(sourcePrefix, StringComparison.Ordinal));

                if (freshEmbeddedCount > 0)
                {
                    subtitleFiles += freshEmbeddedCount;
                    break;
                }

                var embedded = await embeddedSubtitleExtractor.ExtractPreferredJapaneseAsync(
                    candidate.MediaPath,
                    cancellationToken);

                if (embedded is null)
                {
                    continue;
                }

                await subtitleImport.ImportPreferredContentAsync(
                    episodeId,
                    embedded.SourceKey,
                    embedded.Format,
                    candidate.SourceUpdatedAt,
                    embedded.Content,
                    cancellationToken);
                subtitleFiles++;
                break;
            }
        }

        logger.LogInformation(
            "Library reconciliation completed for {Root}: {Discovered} new, {Updated} updated, {Removed} removed, {Skipped} skipped, {Subtitles} subtitle files, {ArtworkImported} local artwork imported, {ArtworkUnchanged} unchanged, {MetadataWarnings} NFO files ignored.",
            root.Path,
            discovered,
            updated,
            removed,
            skipped,
            subtitleFiles,
            localArtworkImported,
            localArtworkUnchanged,
            metadataWarnings);

        return new ScanResult(discovered, updated, skipped, subtitleFiles)
        {
            Removed = removed,
            MetadataWarnings = metadataWarnings
        };
    }

    // Precedence: an episode NFO whose numbers agree with the file name > the file-name title.
    // The first agreeing NFO (ordinal media path order) wins when several files share an episode.
    // A rejected NFO returns null so the current title is kept instead of flapping on a bad read.
    private string? ResolveEpisodeTitle(
        string? nfoPath,
        MediaDescriptor descriptor,
        bool alreadyTitledFromNfo,
        ref int metadataWarnings,
        out bool titleFromNfo)
    {
        titleFromNfo = false;
        if (alreadyTitledFromNfo)
        {
            return null;
        }

        if (nfoPath is null)
        {
            return descriptor.EpisodeTitle;
        }

        var nfo = NfoReader.ReadEpisodes(nfoPath);
        if (nfo.Value is null)
        {
            metadataWarnings++;
            LogRejectedNfo(nfoPath, nfo.Warning);
            return null;
        }

        var entry = nfo.Value.FirstOrDefault(x =>
            x.Describes(descriptor.SeasonNumber, descriptor.EpisodeNumber));
        if (entry is null)
        {
            logger.LogDebug(
                "Episode NFO {Path} describes different season/episode numbers than its media file name; the file name stays authoritative.",
                nfoPath);
            return descriptor.EpisodeTitle;
        }

        if (entry.Title is null)
        {
            return descriptor.EpisodeTitle;
        }

        titleFromNfo = true;
        return entry.Title;
    }

    // An AniList ID from tvshow.nfo replaces the fuzzy title search for a newly discovered anime.
    // It never replaces an existing match; a rejected ID falls through to automatic title matching.
    private async Task MatchNfoAniListIdAsync(
        AnimeMetadataService metadata,
        Guid animeId,
        string aniListId,
        CancellationToken cancellationToken)
    {
        if (await metadata.GetAsync(animeId, cancellationToken) is not null)
        {
            return;
        }

        var result = await metadata.MatchAsync(
            animeId,
            AniListMetadataProvider.ProviderKey,
            aniListId,
            cancellationToken);
        if (result.Success)
        {
            logger.LogInformation(
                "Matched anime {AnimeId} to {Provider}:{ExternalId} from its local tvshow.nfo.",
                animeId,
                AniListMetadataProvider.ProviderKey,
                aniListId);
            return;
        }

        logger.LogWarning(
            "The AniList ID {ExternalId} from the local tvshow.nfo of anime {AnimeId} was not applied: {Error} Automatic title matching continues.",
            aniListId,
            animeId,
            result.Error);
    }

    private void LogRejectedNfo(string path, string? reason) =>
        logger.LogWarning(
            "Ignored local NFO metadata {Path}: {Reason} The library scan continues without it.",
            path,
            reason);

    private sealed record SubtitleCandidate(
        Guid EpisodeId,
        string MediaPath,
        DateTime SourceUpdatedAt);

    private sealed record ExistingEmbeddedTrack(
        Guid EpisodeId,
        string SourceKey,
        DateTime SourceUpdatedAt);

    private static string? TryGetAnimeDirectory(
        string rootPath,
        string mediaPath)
    {
        var relative = Path.GetRelativePath(rootPath, mediaPath);
        var parts = relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            return null;
        }

        var directory = Path.GetFullPath(Path.Combine(rootPath, parts[0]));
        var normalizedRoot = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return directory.StartsWith(normalizedRoot, StringComparison.Ordinal)
            ? directory
            : null;
    }
}
