using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Manga;

public sealed partial class MangaImportService
{
    public const string CacheRoot = "/data/manga-cache";
    private readonly MangaRepository repository;
    private readonly string cacheRoot;

    public MangaImportService(
        MangaRepository repository,
        string? cacheRoot = null)
    {
        this.repository = repository;
        this.cacheRoot = Path.GetFullPath(cacheRoot ?? CacheRoot);
    }
    private const int MaximumPagesPerChapter = 2000;
    private const long MaximumPageBytes = 100L * 1024 * 1024;
    private const long MaximumChapterBytes = 4L * 1024 * 1024 * 1024;

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".avif", ".gif"
        };

    private static readonly HashSet<string> ArchiveExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".cbz", ".zip"
        };

    public async Task<MangaImportResult> ImportAsync(
        string source,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new InvalidOperationException("Choose a manga CBZ/ZIP file or directory.");
        }

        var sourcePath = Path.GetFullPath(source.Trim());
        if (!Directory.Exists(sourcePath) && !File.Exists(sourcePath))
        {
            throw new InvalidOperationException($"Manga source does not exist: {sourcePath}");
        }

        if (File.Exists(sourcePath) &&
            !ArchiveExtensions.Contains(Path.GetExtension(sourcePath)))
        {
            throw new InvalidOperationException("A manga file import must be CBZ or ZIP.");
        }

        var seriesId = DeterministicGuid("series:" + sourcePath);
        var title = File.Exists(sourcePath)
            ? CleanTitle(Path.GetFileNameWithoutExtension(sourcePath))
            : new DirectoryInfo(sourcePath).Name;

        await repository.UpsertSeriesAsync(
            seriesId,
            title,
            sourcePath,
            cancellationToken);

        var sources = DiscoverChapterSources(sourcePath);
        if (sources.Count == 0)
        {
            throw new InvalidOperationException(
                "No CBZ/ZIP archives or image chapters were found in this manga source.");
        }

        var existing = await repository.GetChapterSourcesAsync(
            seriesId,
            cancellationToken);
        var existingByPath = existing.ToDictionary(
            x => x.SourcePath,
            x => x.Id,
            StringComparer.Ordinal);

        var observed = new HashSet<string>(StringComparer.Ordinal);
        var totalPages = 0;
        var updated = 0;

        for (var index = 0; index < sources.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceItem = sources[index];
            observed.Add(sourceItem.Path);

            var chapterId = DeterministicGuid("chapter:" + sourceItem.Path);
            var number = TryParseChapterNumber(sourceItem.Path) ?? index + 1;
            var volume = TryParseVolumeNumber(sourceItem.Path);
            var chapterTitle = BuildChapterTitle(sourceItem.Path, number);
            var updatedAt = GetSourceUpdatedAt(sourceItem);
            var cacheDirectory = GetChapterCacheDirectory(seriesId, chapterId);

            var cached = sourceItem.Kind == "archive"
                ? await CacheArchiveAsync(
                    sourceItem.Path,
                    cacheDirectory,
                    chapterId,
                    cancellationToken)
                : await CacheDirectoryAsync(
                    sourceItem.Path,
                    cacheDirectory,
                    chapterId,
                    cancellationToken);

            if (cached.Pages.Count == 0)
            {
                continue;
            }

            var chapter = new MangaChapterItem(
                chapterId,
                seriesId,
                number,
                volume,
                chapterTitle,
                cached.Pages.Count,
                sourceItem.Kind,
                updatedAt);

            await repository.UpsertChapterAsync(
                chapter,
                sourceItem.Path,
                cancellationToken);
            await repository.ReplacePagesAsync(
                chapterId,
                cached.Pages,
                cached.SourceEntries,
                cancellationToken);

            totalPages += cached.Pages.Count;
            if (existingByPath.ContainsKey(sourceItem.Path))
            {
                updated++;
            }
        }

        foreach (var stale in existing.Where(x => !observed.Contains(x.SourcePath)))
        {
            await repository.RemoveChapterAsync(stale.Id, cancellationToken);
            var staleCache = GetChapterCacheDirectory(seriesId, stale.Id);
            TryDeleteDirectory(staleCache);
        }

        return new MangaImportResult(
            seriesId,
            observed.Count,
            totalPages,
            updated);
    }

    private static List<ChapterSource> DiscoverChapterSources(string sourcePath)
    {
        if (File.Exists(sourcePath))
        {
            return
            [
                new ChapterSource(sourcePath, "archive")
            ];
        }

        var archives = Directory
            .EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories)
            .Where(path => ArchiveExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, NaturalPathComparer.Instance)
            .Select(path => new ChapterSource(Path.GetFullPath(path), "archive"))
            .ToList();

        var imageDirectories = Directory
            .EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories)
            .Prepend(sourcePath)
            .Where(HasDirectImages)
            .OrderBy(path => path, NaturalPathComparer.Instance)
            .Select(path => new ChapterSource(Path.GetFullPath(path), "directory"))
            .ToList();

        // Prefer archives when a folder contains both an archive and extracted copies.
        if (archives.Count > 0)
        {
            var archiveDirectories = archives
                .Select(x => Path.GetDirectoryName(x.Path)!)
                .ToHashSet(StringComparer.Ordinal);

            imageDirectories.RemoveAll(x => archiveDirectories.Contains(x.Path));
        }

        return archives
            .Concat(imageDirectories)
            .OrderBy(x => x.Path, NaturalPathComparer.Instance)
            .ToList();
    }

    private static bool HasDirectImages(string directory)
    {
        try
        {
            return Directory
                .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Any(path => ImageExtensions.Contains(Path.GetExtension(path)));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static DateTime GetSourceUpdatedAt(ChapterSource source)
    {
        if (source.Kind == "archive")
        {
            return File.GetLastWriteTimeUtc(source.Path);
        }

        return Directory
            .EnumerateFiles(source.Path, "*", SearchOption.TopDirectoryOnly)
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path)))
            .Select(File.GetLastWriteTimeUtc)
            .DefaultIfEmpty(Directory.GetLastWriteTimeUtc(source.Path))
            .Max();
    }

    private static async Task<CachedChapter> CacheArchiveAsync(
        string archivePath,
        string cacheDirectory,
        Guid chapterId,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entries = archive.Entries
            .Where(entry =>
                entry.Length > 0 &&
                ImageExtensions.Contains(Path.GetExtension(entry.Name)))
            .OrderBy(entry => entry.FullName, NaturalPathComparer.Instance)
            .ToArray();

        ValidateArchive(entries);

        var temporary = cacheDirectory + ".tmp-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(temporary);

        try
        {
            var pages = new List<MangaPageItem>(entries.Length);
            var sourceEntries = new List<string>(entries.Length);

            for (var index = 0; index < entries.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = entries[index];
                var extension = NormalizeExtension(Path.GetExtension(entry.Name));
                var cachedPath = Path.Combine(
                    temporary,
                    $"{index:D5}{extension}");

                await using var input = entry.Open();
                await using var output = new FileStream(
                    cachedPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await input.CopyToAsync(output, cancellationToken);

                pages.Add(new MangaPageItem(
                    chapterId,
                    index,
                    Path.Combine(cacheDirectory, Path.GetFileName(cachedPath)),
                    GetMimeType(extension)));
                sourceEntries.Add(entry.FullName);
            }

            ReplaceCacheDirectory(temporary, cacheDirectory);
            return new CachedChapter(pages, sourceEntries);
        }
        catch
        {
            TryDeleteDirectory(temporary);
            throw;
        }
    }

    private static async Task<CachedChapter> CacheDirectoryAsync(
        string sourceDirectory,
        string cacheDirectory,
        Guid chapterId,
        CancellationToken cancellationToken)
    {
        var files = Directory
            .EnumerateFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, NaturalPathComparer.Instance)
            .ToArray();

        if (files.Length > MaximumPagesPerChapter)
        {
            throw new InvalidOperationException(
                $"Manga chapter contains {files.Length} pages; the limit is {MaximumPagesPerChapter}.");
        }

        var total = files.Sum(path => new FileInfo(path).Length);
        if (files.Any(path => new FileInfo(path).Length > MaximumPageBytes) ||
            total > MaximumChapterBytes)
        {
            throw new InvalidOperationException("Manga chapter exceeds the safe import size limit.");
        }

        var temporary = cacheDirectory + ".tmp-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(temporary);

        try
        {
            var pages = new List<MangaPageItem>(files.Length);
            var sourceEntries = new List<string>(files.Length);

            for (var index = 0; index < files.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var source = files[index];
                var extension = NormalizeExtension(Path.GetExtension(source));
                var cachedPath = Path.Combine(
                    temporary,
                    $"{index:D5}{extension}");

                await using var input = new FileStream(
                    source,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var output = new FileStream(
                    cachedPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await input.CopyToAsync(output, cancellationToken);

                pages.Add(new MangaPageItem(
                    chapterId,
                    index,
                    Path.Combine(cacheDirectory, Path.GetFileName(cachedPath)),
                    GetMimeType(extension)));
                sourceEntries.Add(Path.GetFileName(source));
            }

            ReplaceCacheDirectory(temporary, cacheDirectory);
            return new CachedChapter(pages, sourceEntries);
        }
        catch
        {
            TryDeleteDirectory(temporary);
            throw;
        }
    }

    private static void ValidateArchive(ZipArchiveEntry[] entries)
    {
        if (entries.Length > MaximumPagesPerChapter)
        {
            throw new InvalidOperationException(
                $"Manga archive contains {entries.Length} pages; the limit is {MaximumPagesPerChapter}.");
        }

        long total = 0;
        foreach (var entry in entries)
        {
            if (entry.Length > MaximumPageBytes)
            {
                throw new InvalidOperationException(
                    $"Manga page '{entry.Name}' exceeds the safe page size limit.");
            }

            total += entry.Length;
            if (total > MaximumChapterBytes)
            {
                throw new InvalidOperationException("Manga archive exceeds the safe chapter size limit.");
            }
        }
    }

    private static void ReplaceCacheDirectory(
        string temporary,
        string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        TryDeleteDirectory(destination);
        Directory.Move(temporary, destination);
    }

    private string GetChapterCacheDirectory(
        Guid seriesId,
        Guid chapterId) =>
        Path.Combine(
            cacheRoot,
            seriesId.ToString("N"),
            chapterId.ToString("N"));

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // A later import can retry cleanup; never mutate the source manga.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static double? TryParseChapterNumber(string path)
    {
        var name = Directory.Exists(path)
            ? new DirectoryInfo(path).Name
            : Path.GetFileNameWithoutExtension(path);
        var match = ChapterNumberRegex().Match(name);
        if (!match.Success)
        {
            match = LeadingNumberRegex().Match(name);
        }

        return match.Success &&
               double.TryParse(
                   match.Groups["number"].Value,
                   System.Globalization.NumberStyles.AllowDecimalPoint,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out var value)
            ? value
            : null;
    }

    private static int? TryParseVolumeNumber(string path)
    {
        foreach (var part in Path.GetFullPath(path)
                     .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     .Reverse())
        {
            var match = VolumeNumberRegex().Match(part);
            if (match.Success &&
                int.TryParse(match.Groups["number"].Value, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static string BuildChapterTitle(string path, double number)
    {
        var name = File.Exists(path)
            ? Path.GetFileNameWithoutExtension(path)
            : new DirectoryInfo(path).Name;
        var cleaned = CleanTitle(name);

        return string.IsNullOrWhiteSpace(cleaned)
            ? $"Chapter {number:0.##}"
            : cleaned;
    }

    private static string CleanTitle(string value)
    {
        var cleaned = SeparatorRegex().Replace(value, " ").Trim();
        return WhitespaceRegex().Replace(cleaned, " ");
    }

    private static string NormalizeExtension(string extension)
    {
        var normalized = extension.ToLowerInvariant();
        return normalized == ".jpeg" ? ".jpg" : normalized;
    }

    private static string GetMimeType(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".jpg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".avif" => "image/avif",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };

    private static Guid DeterministicGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        return new Guid(bytes);
    }

    private sealed record ChapterSource(string Path, string Kind);
    private sealed record CachedChapter(
        IReadOnlyList<MangaPageItem> Pages,
        IReadOnlyList<string> SourceEntries);

    private sealed class NaturalPathComparer : IComparer<string>
    {
        public static NaturalPathComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var left = NaturalPartsRegex().Matches(x);
            var right = NaturalPartsRegex().Matches(y);
            var count = Math.Min(left.Count, right.Count);

            for (var i = 0; i < count; i++)
            {
                var a = left[i].Value;
                var b = right[i].Value;

                if (long.TryParse(a, out var an) && long.TryParse(b, out var bn))
                {
                    var numeric = an.CompareTo(bn);
                    if (numeric != 0) return numeric;
                    continue;
                }

                var text = StringComparer.OrdinalIgnoreCase.Compare(a, b);
                if (text != 0) return text;
            }

            return left.Count != right.Count
                ? left.Count.CompareTo(right.Count)
                : StringComparer.OrdinalIgnoreCase.Compare(x, y);
        }
    }

    [GeneratedRegex(@"(?:chapter|chap|ch|c)[\s._-]*(?<number>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChapterNumberRegex();

    [GeneratedRegex(@"^\D*(?<number>\d+(?:\.\d+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingNumberRegex();

    [GeneratedRegex(@"(?:volume|vol|v)[\s._-]*(?<number>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VolumeNumberRegex();

    [GeneratedRegex(@"[._-]+", RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"\d+|\D+", RegexOptions.CultureInvariant)]
    private static partial Regex NaturalPartsRegex();
}
