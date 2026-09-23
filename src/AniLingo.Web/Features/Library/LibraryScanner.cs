using AniLingo.Web.Data;
using AniLingo.Web.Features.Sonarr;
using AniLingo.Web.Features.Subtitles;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Library;

public sealed class LibraryScanner(
    AppDbContext db,
    SubtitleImportService subtitleImport,
    EmbeddedSubtitleExtractor embeddedSubtitleExtractor,
    SonarrArtworkSyncService sonarrArtworkSync,
    ILogger<LibraryScanner> logger)
{
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
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

        var candidates = Directory
            .EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Where(path => MediaExtensions.Contains(Path.GetExtension(path)))
            .Select(path => new FileInfo(path))
            .ToList();

        var existingFiles = await db.MediaFiles
            .Where(x => x.LibraryRootId == rootId)
            .ToDictionaryAsync(x => x.Path, StringComparer.Ordinal, cancellationToken);

        var animeByKey = await db.Anime.ToDictionaryAsync(x => x.Key, StringComparer.Ordinal, cancellationToken);
        var episodes = await db.Episodes.ToListAsync(cancellationToken);
        var episodeByKey = episodes.ToDictionary(
            x => (x.AnimeId, x.SeasonNumber, x.Number),
            x => x);

        var discovered = 0;
        var updated = 0;
        var skipped = 0;
        var subtitleCandidates = new List<SubtitleCandidate>();

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
            }

            var episodeKey = (anime.Id, descriptor.SeasonNumber, descriptor.EpisodeNumber);
            if (!episodeByKey.TryGetValue(episodeKey, out var episode))
            {
                episode = new Episode
                {
                    AnimeId = anime.Id,
                    SeasonNumber = descriptor.SeasonNumber,
                    Number = descriptor.EpisodeNumber,
                    Title = descriptor.EpisodeTitle
                };
                episodeByKey.Add(episodeKey, episode);
                db.Episodes.Add(episode);
            }
            else if (!string.Equals(
                         episode.Title,
                         descriptor.EpisodeTitle,
                         StringComparison.Ordinal))
            {
                episode.Title = descriptor.EpisodeTitle;
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

        root.LastScannedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await sonarrArtworkSync.SyncIfConfiguredAsync(cancellationToken);

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
        foreach (var candidate in subtitleCandidates)
        {
            var externalSubtitle = FindJapaneseSubtitles(candidate.MediaPath).FirstOrDefault();
            if (externalSubtitle is not null)
            {
                await subtitleImport.ImportAsync(candidate.EpisodeId, externalSubtitle, cancellationToken);
                subtitleFiles++;
                continue;
            }

            var sourcePrefix = EmbeddedSubtitleExtractor.BuildSourcePrefix(candidate.MediaPath);
            var freshEmbeddedCount = embeddedTracks.Count(x =>
                x.EpisodeId == candidate.EpisodeId &&
                x.SourceUpdatedAt == candidate.SourceUpdatedAt &&
                x.SourceKey.StartsWith(sourcePrefix, StringComparison.Ordinal));

            if (freshEmbeddedCount > 0)
            {
                subtitleFiles += freshEmbeddedCount;
                continue;
            }

            var embedded = await embeddedSubtitleExtractor.ExtractPreferredJapaneseAsync(
                candidate.MediaPath,
                cancellationToken);

            if (embedded is null)
            {
                continue;
            }

            await subtitleImport.ImportPreferredContentAsync(
                candidate.EpisodeId,
                embedded.SourceKey,
                embedded.Format,
                candidate.SourceUpdatedAt,
                embedded.Content,
                cancellationToken);
            subtitleFiles++;
        }

        logger.LogInformation(
            "Library scan completed for {Root}: {Discovered} new, {Updated} updated, {Skipped} skipped, {Subtitles} subtitle files.",
            root.Path, discovered, updated, skipped, subtitleFiles);

        return new ScanResult(discovered, updated, skipped, subtitleFiles);
    }

    private sealed record SubtitleCandidate(
        Guid EpisodeId,
        string MediaPath,
        DateTime SourceUpdatedAt);

    private sealed record ExistingEmbeddedTrack(
        Guid EpisodeId,
        string SourceKey,
        DateTime SourceUpdatedAt);

    private static IEnumerable<string> FindJapaneseSubtitles(string mediaPath)
    {
        var directory = Path.GetDirectoryName(mediaPath)!;
        var baseName = Path.GetFileNameWithoutExtension(mediaPath);
        var suffixes = new[]
        {
            ".ja.srt", ".jpn.srt", ".japanese.srt",
            ".ja.ass", ".jpn.ass", ".japanese.ass"
        };

        foreach (var suffix in suffixes)
        {
            var path = Path.Combine(directory, baseName + suffix);
            if (File.Exists(path))
            {
                yield return path;
            }
        }
    }
}
