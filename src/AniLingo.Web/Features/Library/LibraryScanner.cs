using AniLingo.Web.Data;
using AniLingo.Web.Features.Subtitles;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Library;

public sealed class LibraryScanner(
    AppDbContext db,
    SubtitleImportService subtitleImport,
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
        var subtitleCandidates = new List<(Guid EpisodeId, string MediaPath)>();

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

            var lastWrite = new DateTimeOffset(file.LastWriteTimeUtc);
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

            subtitleCandidates.Add((episode.Id, normalizedPath));
        }

        root.LastScannedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var subtitleFiles = 0;
        foreach (var candidate in subtitleCandidates)
        {
            foreach (var subtitlePath in FindJapaneseSubtitles(candidate.MediaPath))
            {
                await subtitleImport.ImportAsync(candidate.EpisodeId, subtitlePath, cancellationToken);
                subtitleFiles++;
            }
        }

        logger.LogInformation(
            "Library scan completed for {Root}: {Discovered} new, {Updated} updated, {Skipped} skipped, {Subtitles} subtitle files.",
            root.Path, discovered, updated, skipped, subtitleFiles);

        return new ScanResult(discovered, updated, skipped, subtitleFiles);
    }

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
