using System.IO.Compression;
using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Manga;

public sealed partial class MangaUploadService
{
    public const string UploadRoot = "/data/manga-imports";
    private const int MaximumFiles = 200;
    private const long MaximumFileBytes = 1024L * 1024 * 1024;
    private const long MaximumTotalBytes = 4L * 1024 * 1024 * 1024;

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".avif", ".gif"
        };

    private readonly string uploadRoot;

    public MangaUploadService(string? uploadRoot = null)
    {
        this.uploadRoot = Path.GetFullPath(uploadRoot ?? UploadRoot);
    }

    public async Task<string> SaveSeriesAsync(
        string? seriesTitle,
        IReadOnlyList<IFormFile> archives,
        CancellationToken cancellationToken)
    {
        if (archives.Count == 0)
        {
            throw new InvalidOperationException("Choose at least one CBZ or ZIP file.");
        }

        if (archives.Count > MaximumFiles)
        {
            throw new InvalidOperationException(
                $"A single upload can contain at most {MaximumFiles} archives.");
        }

        var usable = archives
            .Where(file => file.Length > 0)
            .ToArray();

        if (usable.Length == 0)
        {
            throw new InvalidOperationException("The selected Manga archives are empty.");
        }

        if (usable.Any(file => !IsArchive(file.FileName)))
        {
            throw new InvalidOperationException("Manga uploads must be CBZ or ZIP files.");
        }

        if (usable.Any(file => file.Length > MaximumFileBytes))
        {
            throw new InvalidOperationException(
                $"A Manga archive may be at most {MaximumFileBytes / 1024 / 1024} MB.");
        }

        var totalBytes = usable.Sum(file => file.Length);
        if (totalBytes > MaximumTotalBytes)
        {
            throw new InvalidOperationException(
                $"The total Manga upload may be at most {MaximumTotalBytes / 1024 / 1024 / 1024} GB.");
        }

        var normalizedTitle = NormalizeSeriesTitle(seriesTitle, usable);
        var seriesDirectory = Path.Combine(uploadRoot, SanitizeSegment(normalizedTitle));
        EnsureInsideRoot(seriesDirectory);
        Directory.CreateDirectory(seriesDirectory);

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var archive in usable)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = SanitizeFileName(archive.FileName);
            if (!seenNames.Add(fileName))
            {
                throw new InvalidOperationException(
                    $"The upload contains the file name '{fileName}' more than once.");
            }

            var destination = Path.GetFullPath(
                Path.Combine(seriesDirectory, fileName));
            EnsureInsideRoot(destination);

            var temporary = destination + $".upload-{Guid.NewGuid():N}.tmp";

            try
            {
                await using (var input = archive.OpenReadStream())
                await using (var output = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    1024 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await input.CopyToAsync(output, cancellationToken);
                }

                ValidateArchive(temporary);
                File.Move(temporary, destination, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    try
                    {
                        File.Delete(temporary);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
        }

        return seriesDirectory;
    }

    private static string NormalizeSeriesTitle(
        string? seriesTitle,
        IReadOnlyList<IFormFile> archives)
    {
        var normalized = seriesTitle?.Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        if (archives.Count > 1)
        {
            throw new InvalidOperationException(
                "Enter a series name when uploading multiple Manga archives.");
        }

        var fileName = Path.GetFileNameWithoutExtension(
            Path.GetFileName(archives[0].FileName));
        normalized = DisplaySeparatorRegex().Replace(fileName, " ").Trim();
        normalized = WhitespaceRegex().Replace(normalized, " ");

        return string.IsNullOrWhiteSpace(normalized)
            ? "Manga"
            : normalized;
    }

    private static string SanitizeFileName(string suppliedName)
    {
        var fileName = Path.GetFileName(suppliedName);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not ".cbz" and not ".zip")
        {
            throw new InvalidOperationException("Manga uploads must be CBZ or ZIP files.");
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        stem = InvalidSegmentRegex().Replace(stem, "_").Trim(' ', '.', '_');
        if (stem.Length == 0)
        {
            stem = "chapter";
        }

        if (stem.Length > 140)
        {
            stem = stem[..140];
        }

        return stem + extension;
    }

    private static string SanitizeSegment(string value)
    {
        var sanitized = InvalidSegmentRegex().Replace(value, "_")
            .Trim(' ', '.', '_');

        if (sanitized.Length == 0)
        {
            sanitized = "Manga";
        }

        return sanitized.Length <= 120
            ? sanitized
            : sanitized[..120];
    }

    private static bool IsArchive(string suppliedName)
    {
        var extension = Path.GetExtension(
            Path.GetFileName(suppliedName));
        return extension.Equals(".cbz", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateArchive(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var hasImage = archive.Entries.Any(entry =>
                entry.Length > 0 &&
                ImageExtensions.Contains(Path.GetExtension(entry.Name)));

            if (!hasImage)
            {
                throw new InvalidOperationException(
                    "A selected Manga archive contains no supported image pages.");
            }
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidOperationException(
                "A selected Manga archive is not a valid CBZ/ZIP file.",
                exception);
        }
    }

    private void EnsureInsideRoot(string path)
    {
        var root = uploadRoot
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);

        if (!full.StartsWith(root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid Manga upload path.");
        }
    }

    [GeneratedRegex(@"[^\p{L}\p{N} ._()\[\]-]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidSegmentRegex();

    [GeneratedRegex(@"[._-]+", RegexOptions.CultureInvariant)]
    private static partial Regex DisplaySeparatorRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
