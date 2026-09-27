using System.Security.Cryptography;
using SkiaSharp;

namespace Jularr.Web.Features.Artwork;

public enum AnimeArtworkKind
{
    Poster,
    Fanart,
    Banner
}

/// <summary>
/// Canonical anime artwork files beside the media on the library storage: series artwork in the
/// series folder, season artwork in the season's own folder. These files are the durable copy;
/// everything under /data/cache/artwork is derived from them.
/// </summary>
public static class AnimeArtworkFiles
{
    public const long MaxImageBytes = 20 * 1024 * 1024;

    private static readonly string[] PosterBaseNames = ["poster", "folder", "cover"];
    private static readonly string[] FanartBaseNames = ["fanart", "backdrop", "background"];
    private static readonly string[] BannerBaseNames = ["banner"];
    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".webp"];

    /// <summary>File name Jularr writes for a kind, without extension.</summary>
    public static string BaseName(AnimeArtworkKind kind) =>
        kind switch
        {
            AnimeArtworkKind.Poster => "poster",
            AnimeArtworkKind.Fanart => "fanart",
            _ => "banner"
        };

    /// <summary>
    /// The file shown for a kind: the first match in name order (poster, folder, cover; fanart,
    /// backdrop, background, then banner as the fanart fallback) and extension order.
    /// </summary>
    public static string? FindCandidate(string directory, AnimeArtworkKind kind) =>
        FindDisplayCandidates(directory, kind).FirstOrDefault();

    /// <summary>Every file that can be shown for a kind, in precedence order.</summary>
    public static IReadOnlyList<string> FindDisplayCandidates(string directory, AnimeArtworkKind kind) =>
        FindNamed(
            directory,
            kind == AnimeArtworkKind.Fanart
                ? [.. FanartBaseNames, .. BannerBaseNames]
                : BaseNames(kind));

    /// <summary>Files of exactly this kind (a banner is not fanart here), in precedence order.</summary>
    public static IReadOnlyList<string> FindFiles(string directory, AnimeArtworkKind kind) =>
        FindNamed(directory, BaseNames(kind));

    /// <summary>
    /// Season artwork: the season folder's own files first, then the Kodi/Sonarr style
    /// "season01-poster.jpg" (specials: "season-specials-poster.jpg") in the series folder.
    /// </summary>
    public static IReadOnlyList<string> FindSeasonCandidates(
        string seriesDirectory,
        string? seasonDirectory,
        int seasonNumber,
        AnimeArtworkKind kind)
    {
        var candidates = new List<string>();
        if (seasonDirectory is not null &&
            !PathsEqual(seasonDirectory, seriesDirectory))
        {
            candidates.AddRange(FindDisplayCandidates(seasonDirectory, kind));
        }

        var suffix = "-" + BaseName(kind);
        string[] seriesNames = seasonNumber == 0
            ? ["season-specials" + suffix, "season00" + suffix]
            : [$"season{seasonNumber:00}{suffix}"];
        candidates.AddRange(FindNamed(seriesDirectory, seriesNames));
        return candidates;
    }

    /// <summary>True for poster/fanart/banner files that belong to the folder they are in.</summary>
    public static bool IsArtworkFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        return Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase) &&
               PosterBaseNames.Concat(FanartBaseNames).Concat(BannerBaseNames)
                   .Contains(baseName, StringComparer.OrdinalIgnoreCase);
    }

    public static string GetContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };

    /// <summary>The file extension of the actual image bytes; null for anything but JPEG, PNG or WebP.</summary>
    public static string? DetectExtension(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return null;
        }

        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        return codec?.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => ".jpg",
            SKEncodedImageFormat.Png => ".png",
            SKEncodedImageFormat.Webp => ".webp",
            _ => null
        };
    }

    /// <summary>Reads at most <see cref="MaxImageBytes"/>; null when the image is larger.</summary>
    public static async Task<byte[]?> ReadLimitedAsync(Stream source, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > MaxImageBytes)
            {
                return null;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
    }

    /// <summary>
    /// Writes into a temporary file in the target folder and renames it over the target, so a
    /// reader (or a media server) never sees a half-written image.
    /// </summary>
    public static async Task<FileInfo> WriteAtomicAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)!;
        var temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var output = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous))
            {
                await output.WriteAsync(bytes, cancellationToken);
                output.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporary);
        }

        return new FileInfo(path);
    }

    /// <summary>Re-reads a written file and compares its SHA-256 with the bytes that were meant to land.</summary>
    public static async Task<bool> ContentEqualsAsync(
        string path,
        byte[] expected,
        CancellationToken cancellationToken)
    {
        var written = await File.ReadAllBytesAsync(path, cancellationToken);
        return SHA256.HashData(written).AsSpan().SequenceEqual(SHA256.HashData(expected));
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string[] BaseNames(AnimeArtworkKind kind) =>
        kind switch
        {
            AnimeArtworkKind.Poster => PosterBaseNames,
            AnimeArtworkKind.Fanart => FanartBaseNames,
            _ => BannerBaseNames
        };

    private static IReadOnlyList<string> FindNamed(string directory, IReadOnlyList<string> baseNames)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        // Ordinal order keeps the choice deterministic when a case-sensitive filesystem holds
        // both "Poster.jpg" and "poster.jpg".
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory
                     .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                     .Order(StringComparer.Ordinal))
        {
            files.TryAdd(Path.GetFileName(path), path);
        }

        var result = new List<string>();
        foreach (var baseName in baseNames)
        {
            foreach (var extension in Extensions)
            {
                if (files.TryGetValue(baseName + extension, out var candidate))
                {
                    result.Add(candidate);
                }
            }
        }

        return result;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.Ordinal);
}
