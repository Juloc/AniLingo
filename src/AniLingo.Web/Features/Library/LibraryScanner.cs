using AniLingo.Web.Data;
using AniLingo.Web.Features.Artwork;
using AniLingo.Web.Features.Metadata;
using AniLingo.Web.Features.Sonarr;
using AniLingo.Web.Features.Subtitles;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Library;

public sealed class LibraryScanner(
    AppDbContext db,
    SubtitleImportService subtitleImport,
    EmbeddedSubtitleExtractor embeddedSubtitleExtractor,
    MediaInventoryService mediaInventory,
    SonarrArtworkSyncService sonarrArtworkSync,
    ILogger<LibraryScanner> logger,
    AnimeMetadataService? metadataService = null)
{
    internal static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".webm"
    };

    public Task<ScanResult> ScanAsync(Guid rootId, CancellationToken cancellationToken) =>
        ReconcileAsync(rootId, null, null, cancellationToken);

    public Task<ScanResult> ScanAsync(
        Guid rootId,
        LibraryScanProgressHandler? progress,
        CancellationToken cancellationToken) =>
        ReconcileAsync(rootId, null, progress, cancellationToken);

    // Reconciles one folder below the root (normally an anime directory) with exactly the
    // rules of a full scan, but only touches media files inside that folder. A folder that
    // no longer exists reconciles as a deletion of its media.
    public Task<ScanResult> ScanFolderAsync(
        Guid rootId,
        string relativeFolder,
        LibraryScanProgressHandler? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeFolder);
        return ReconcileAsync(rootId, relativeFolder, progress, cancellationToken);
    }

    private async Task<ScanResult> ReconcileAsync(
        Guid rootId,
        string? relativeFolder,
        LibraryScanProgressHandler? progress,
        CancellationToken cancellationToken)
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

        var scopePath = ResolveScopePath(rootPath, relativeFolder);
        var scopePrefix = scopePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        await ReportAsync(progress, LibraryScanPhase.Enumerating, 0, 0, cancellationToken);

        var candidates = new List<FileInfo>();
        var nfoFiles = new NfoFileIndex();
        try
        {
            foreach (var path in Directory.Exists(scopePath)
                         ? Directory.EnumerateFiles(scopePath, "*", SearchOption.AllDirectories)
                         : [])
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

        if (relativeFolder is not null)
        {
            existingFiles = existingFiles
                .Where(pair => pair.Key.StartsWith(scopePrefix, StringComparison.Ordinal))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }
        else if (existingFiles.Count > 0 && candidates.Count == 0)
        {
            throw new IOException(
                "Library root returned no media files while AniLingo still has known media for it. " +
                "Reconciliation was stopped to avoid treating an unavailable NAS mount as a mass deletion.");
        }

        var warnings = new List<ScanWarning>();
        var warningCount = 0;
        var errors = 0;
        var processed = 0;

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

        await ReportAsync(progress, LibraryScanPhase.Reconciling, 0, candidates.Count, cancellationToken);

        foreach (var file in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ReportAsync(progress, LibraryScanPhase.Reconciling, processed++, candidates.Count, cancellationToken);

            var normalizedPath = Path.GetFullPath(file.FullName);
            if (!MediaPathParser.TryParse(rootPath, normalizedPath, out var descriptor))
            {
                skipped++;
                AddWarning(warnings, ref warningCount, rootPath, normalizedPath, "Unmatched media file");
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
                    AddWarning(warnings, ref warningCount, rootPath, showNfoPath, "Ignored NFO");
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
            var episodeNfoPath = nfoFiles.FindEpisode(normalizedPath);
            var episodeTitle = ResolveEpisodeTitle(
                episodeNfoPath,
                descriptor,
                nfoTitledEpisodes.Contains(episodeKey),
                ref metadataWarnings,
                out var titleFromNfo,
                out var nfoRejected);
            if (nfoRejected)
            {
                AddWarning(warnings, ref warningCount, rootPath, episodeNfoPath!, "Ignored NFO");
            }
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

        if (relativeFolder is null)
        {
            root.LastScannedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (metadataService is not null)
        {
            var matched = 0;
            foreach (var animeId in newlyDiscoveredAnimeIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ReportAsync(progress, LibraryScanPhase.Metadata, matched++, newlyDiscoveredAnimeIds.Count, cancellationToken);
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
                    errors++;
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

        await ReportAsync(progress, LibraryScanPhase.Artwork, 0, artworkDirectories.Count, cancellationToken);

        // The Sonarr artwork sync is library-wide; a folder scan only needs it for anime it
        // just discovered, everything else was synced by an earlier full reconciliation.
        if (relativeFolder is null || newlyDiscoveredAnimeIds.Count > 0)
        {
            await sonarrArtworkSync.SyncIfConfiguredAsync(cancellationToken);
        }

        var localArtworkImported = 0;
        var localArtworkUnchanged = 0;
        var artworkProcessed = 0;
        foreach (var (animeId, animeDirectory) in artworkDirectories)
        {
            await ReportAsync(progress, LibraryScanPhase.Artwork, artworkProcessed++, artworkDirectories.Count, cancellationToken);
            var artwork = await LocalAnimeArtworkImporter.ImportAsync(
                animeId,
                animeDirectory,
                cancellationToken);
            localArtworkImported += artwork.ImportedCount;
            localArtworkUnchanged += artwork.UnchangedCount;
        }

        // Runs ffprobe only for new/changed files or after a probe version bump; invalid media is
        // recorded as a diagnostic on its analysis instead of failing the root. Embedded subtitle
        // selection below reads this inventory.
        await ReportAsync(progress, LibraryScanPhase.Analyzing, 0, 0, cancellationToken);
        var inventory = await mediaInventory.ReconcileAsync(
            rootId,
            relativeFolder is null ? null : scopePrefix,
            cancellationToken);

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
        var subtitleEpisodes = 0;
        var sidecarListings = new SubtitleSidecarDirectoryCache();
        await ReportAsync(progress, LibraryScanPhase.Subtitles, 0, episodeIds.Length, cancellationToken);
        foreach (var episodeCandidates in subtitleCandidates.GroupBy(x => x.EpisodeId))
        {
            await ReportAsync(progress, LibraryScanPhase.Subtitles, subtitleEpisodes++, episodeIds.Length, cancellationToken);
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
            "Library reconciliation completed for {Root}: {Discovered} new, {Updated} updated, {Removed} removed, {Skipped} skipped, {Subtitles} subtitle files, {ArtworkImported} local artwork imported, {ArtworkUnchanged} unchanged, {MetadataWarnings} NFO files ignored, {MediaAnalyzed} media analysed, {MediaAnalysisFailed} media analyses failed, {MediaAnalysisDeferred} deferred, {MediaAnalysisUnchanged} unchanged.",
            root.Path,
            discovered,
            updated,
            removed,
            skipped,
            subtitleFiles,
            localArtworkImported,
            localArtworkUnchanged,
            metadataWarnings,
            inventory.Analyzed,
            inventory.Failed,
            inventory.Deferred,
            inventory.Unchanged);

        await ReportAsync(progress, LibraryScanPhase.Completed, candidates.Count, candidates.Count, cancellationToken);

        return new ScanResult(discovered, updated, skipped, subtitleFiles)
        {
            Removed = removed,
            MetadataWarnings = metadataWarnings,
            MediaFiles = candidates.Count,
            ArtworkImported = localArtworkImported,
            Errors = errors,
            Warnings = warnings,
            WarningCount = warningCount,
            MediaInventory = inventory
        };
    }

    // The scope must stay inside the root; a relative folder is never allowed to escape it.
    private static string ResolveScopePath(string rootPath, string? relativeFolder)
    {
        if (relativeFolder is null)
        {
            return rootPath;
        }

        var normalizedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var scopePath = Path.GetFullPath(Path.Combine(rootPath, relativeFolder));
        if (Path.IsPathRooted(relativeFolder) ||
            !scopePath.StartsWith(normalizedRoot, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The folder to scan must be a relative path inside the library root.",
                nameof(relativeFolder));
        }

        return scopePath;
    }

    private static Task ReportAsync(
        LibraryScanProgressHandler? progress,
        LibraryScanPhase phase,
        int processed,
        int total,
        CancellationToken cancellationToken) =>
        progress is null
            ? Task.CompletedTask
            : progress(new LibraryScanProgress(phase, processed, total), cancellationToken);

    private static void AddWarning(
        List<ScanWarning> warnings,
        ref int warningCount,
        string rootPath,
        string path,
        string reason)
    {
        warningCount++;
        if (warnings.Count < ScanResult.MaxRecordedWarnings)
        {
            warnings.Add(new ScanWarning(reason, ToRelativePath(rootPath, path)));
        }
    }

    internal static string ToRelativePath(string rootPath, string path) =>
        Path.GetRelativePath(rootPath, path).Replace('\\', '/');

    // Precedence: an episode NFO whose numbers agree with the file name > the file-name title.
    // The first agreeing NFO (ordinal media path order) wins when several files share an episode.
    // A rejected NFO returns null so the current title is kept instead of flapping on a bad read.
    private string? ResolveEpisodeTitle(
        string? nfoPath,
        MediaDescriptor descriptor,
        bool alreadyTitledFromNfo,
        ref int metadataWarnings,
        out bool titleFromNfo,
        out bool nfoRejected)
    {
        titleFromNfo = false;
        nfoRejected = false;
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
            nfoRejected = true;
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
