using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jularr.Web.Features.Ai;
using SkiaSharp;

namespace Jularr.Web.Features.ChapterArtwork;

/// <summary>Owner settings that apply to every profile.</summary>
public sealed record ChapterArtworkGlobalSettings(
    bool Enabled,
    string? StorageRoot)
{
    public static ChapterArtworkGlobalSettings Default { get; } = new(true, null);
}

/// <summary>
/// Owner-level switch and the media-storage folder for chapter artwork, in
/// Jularr application data. <c>ChapterArtwork:StorageRoot</c> seeds the
/// folder when nothing has been saved yet.
/// </summary>
public sealed class ChapterArtworkGlobalSettingsStore(string path, string? configuredRoot = null)
{
    public const string DefaultPath = "/data/chapter-artwork/settings.json";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static ChapterArtworkGlobalSettingsStore FromConfiguration(IConfiguration configuration) =>
        new(
            configuration["ChapterArtwork:SettingsPath"]?.Trim() is { Length: > 0 } configured
                ? configured
                : DefaultPath,
            configuration["ChapterArtwork:StorageRoot"]?.Trim());

    public ChapterArtworkGlobalSettings Load()
    {
        try
        {
            if (File.Exists(path))
            {
                var stored = JsonSerializer.Deserialize<ChapterArtworkGlobalSettings>(File.ReadAllText(path));
                if (stored is not null)
                {
                    return stored;
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return ChapterArtworkGlobalSettings.Default with
        {
            StorageRoot = string.IsNullOrWhiteSpace(configuredRoot) ? null : configuredRoot
        };
    }

    public async Task SaveAsync(ChapterArtworkGlobalSettings settings, CancellationToken cancellationToken)
    {
        var root = settings.StorageRoot?.Trim();
        if (!string.IsNullOrWhiteSpace(root) && !Path.IsPathFullyQualified(root))
        {
            throw new InvalidOperationException("Enter an absolute folder path.");
        }

        await Gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(settings with { StorageRoot = string.IsNullOrWhiteSpace(root) ? null : root }),
                cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            Gate.Release();
        }
    }
}

public sealed record StoredChapterImage(
    string AssetPath,
    string MediaType,
    string ContentHash,
    long ByteSize);

/// <summary>
/// Places generated chapter images on media storage and keeps small reader
/// derivatives in a rebuildable local cache. Layout below the storage root:
/// <c>&lt;Book Title&gt; [id]/chapter-art/chapter-0001.webp</c> for accepted
/// images and <c>chapter-art/candidates/</c> for previews. Paths stored in the
/// database are relative to the root, so the root can move.
/// </summary>
public sealed class ChapterArtworkStorage(string? storageRoot, string cacheRoot)
{
    public const string DefaultCacheRoot = "/data/cache/artwork/chapter-art";
    public static readonly int[] DerivativeWidths = [640, 1280, 1920];
    private const int CanonicalQuality = 90;
    private const int DerivativeQuality = 80;

    public static ChapterArtworkStorage Create(
        ChapterArtworkGlobalSettings settings,
        IConfiguration configuration) =>
        new(
            settings.StorageRoot,
            configuration["ChapterArtwork:CachePath"]?.Trim() is { Length: > 0 } cache
                ? cache
                : DefaultCacheRoot);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(storageRoot);

    /// <summary>The configured root exists right now (NAS mounted and awake).</summary>
    public bool IsAvailable => IsConfigured && Directory.Exists(storageRoot);

    public static string WorkFolder(Guid workId, string title)
    {
        var builder = new StringBuilder();
        foreach (var character in title.Trim())
        {
            builder.Append(
                char.IsLetterOrDigit(character) || character is ' ' or '-' or '_' or '.' or ',' or '\'' or '(' or ')'
                    ? character
                    : ' ');
        }

        var name = string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' ');
        if (name.Length > 80)
        {
            name = name[..80].TrimEnd('.', ' ');
        }

        var suffix = $"[{workId.ToString("N")[..8]}]";
        return name.Length == 0 ? suffix : $"{name} {suffix}";
    }

    public static string CanonicalName(int chapterNumber) =>
        $"chapter-{chapterNumber:0000}.webp";

    public async Task<StoredChapterImage> SaveCandidateAsync(
        Guid workId,
        string workTitle,
        int chapterNumber,
        Guid artworkId,
        AiGeneratedImage image,
        CancellationToken cancellationToken)
    {
        var bytes = Encode(image.Bytes, maxWidth: null, CanonicalQuality)
            ?? throw new InvalidOperationException("The generated image could not be decoded.");

        var relative = Path.Combine(
            WorkFolder(workId, workTitle),
            "chapter-art",
            "candidates",
            $"chapter-{chapterNumber:0000}-{artworkId.ToString("N")[..12]}.webp");

        await WriteAtomicAsync(Resolve(relative), bytes, cancellationToken);
        return new StoredChapterImage(relative, "image/webp", Hash(bytes), bytes.LongLength);
    }

    /// <summary>Moves an accepted preview to the chapter's deterministic file name, replacing an older image.</summary>
    public string Promote(string candidatePath, int chapterNumber)
    {
        var source = Resolve(candidatePath);
        var chapterArt = Path.GetDirectoryName(Path.GetDirectoryName(candidatePath)!)!;
        var relative = Path.Combine(chapterArt, CanonicalName(chapterNumber));
        File.Move(source, Resolve(relative), overwrite: true);

        var candidates = Path.GetDirectoryName(source)!;
        if (Directory.Exists(candidates) && !Directory.EnumerateFileSystemEntries(candidates).Any())
        {
            Directory.Delete(candidates);
        }

        return relative;
    }

    public bool Exists(string? relativePath) =>
        relativePath is not null && IsAvailable && File.Exists(Resolve(relativePath));

    public void Delete(string? relativePath)
    {
        if (relativePath is null || !IsAvailable)
        {
            return;
        }

        var path = Resolve(relativePath);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        // Remove emptied candidates/ and chapter-art/ folders, never the book folder itself.
        var directory = Path.GetDirectoryName(path);
        for (var depth = 0; depth < 2 && directory is not null; depth++)
        {
            var name = Path.GetFileName(directory);
            if (name is not ("candidates" or "chapter-art")
                || Directory.EnumerateFileSystemEntries(directory).Any())
            {
                break;
            }

            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }

    /// <summary>
    /// A reader-sized WebP derivative, built once per content hash and width
    /// from the media-storage original. Served from the cache even while the
    /// media storage is offline.
    /// </summary>
    public async Task<string?> GetDerivativeAsync(
        string relativePath,
        string contentHash,
        int requestedWidth,
        CancellationToken cancellationToken)
    {
        var width = DerivativeWidths.FirstOrDefault(x => x >= requestedWidth, DerivativeWidths[^1]);
        var cached = Path.Combine(cacheRoot, $"{Safe(contentHash)}-{width}.webp");
        if (File.Exists(cached))
        {
            return cached;
        }

        if (!Exists(relativePath))
        {
            return null;
        }

        var original = await File.ReadAllBytesAsync(Resolve(relativePath), cancellationToken);
        var bytes = Encode(original, width, DerivativeQuality);
        if (bytes is null)
        {
            return null;
        }

        await WriteAtomicAsync(cached, bytes, cancellationToken);
        return cached;
    }

    public void DeleteDerivatives(string? contentHash)
    {
        if (contentHash is null || !Directory.Exists(cacheRoot))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(cacheRoot, Safe(contentHash) + "-*.webp"))
        {
            File.Delete(file);
        }
    }

    public string Resolve(string relativePath)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("No chapter artwork folder is configured.");
        }

        var root = Path.GetFullPath(storageRoot!);
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Artwork path escapes the storage folder.");
        }

        return full;
    }

    private static byte[]? Encode(byte[] source, int? maxWidth, int quality)
    {
        using var decoded = SKBitmap.Decode(source);
        if (decoded is null || decoded.Width <= 0 || decoded.Height <= 0)
        {
            return null;
        }

        SKBitmap output = decoded;
        SKBitmap? resized = null;
        try
        {
            if (maxWidth is int width && decoded.Width > width)
            {
                var height = Math.Max(1, (int)Math.Round(decoded.Height * (width / (double)decoded.Width)));
                resized = decoded.Resize(new SKSizeI(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
                if (resized is null)
                {
                    return null;
                }

                output = resized;
            }

            using var data = output.Encode(SKEncodedImageFormat.Webp, quality);
            return data is null || data.Size == 0 ? null : data.ToArray();
        }
        finally
        {
            resized?.Dispose();
        }
    }

    private static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes))[..32].ToLowerInvariant();

    private static string Safe(string value) =>
        new(value.Where(char.IsAsciiLetterOrDigit).Take(64).ToArray());
}
