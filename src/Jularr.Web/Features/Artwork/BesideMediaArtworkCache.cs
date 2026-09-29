using SkiaSharp;

namespace Jularr.Web.Features.Artwork;

/// <summary>A cached artwork derivative ready to serve, with its media type.</summary>
public sealed record ArtworkThumbnail(string Path, string MediaType);

/// <summary>
/// Local thumbnail cache in front of <see cref="BesideMediaArtworkStore.ResolveAsync"/> (issue #570).
/// It generates small Jularr-owned WebP variants of the canonical artwork that lives beside a work's
/// media on the NAS and keeps them under the local <c>/data</c> volume, so library rows/cards keep
/// rendering thumbnails even while the NAS is asleep/offline or an external provider is unreachable.
///
/// This wraps the existing single read path; it never introduces a second artwork write path. Cache
/// files are keyed deterministically by (scope, owner, kind, width) so duplicate identities do not
/// create uncontrolled duplicates, and a small stamp records the source size/last-write so a changed
/// source regenerates its derivative instead of accumulating stale copies.
/// </summary>
public sealed class BesideMediaArtworkCache
{
    public const string DerivativeVersion = "v1-webp82";
    private const int DerivativeQuality = 82;

    private readonly BesideMediaArtworkStore store;
    private readonly string root;

    public BesideMediaArtworkCache(BesideMediaArtworkStore store)
        : this(store, System.IO.Path.Combine("/data", "cache", "artwork", "beside"))
    {
    }

    public BesideMediaArtworkCache(BesideMediaArtworkStore store, string cacheRoot)
    {
        this.store = store;
        root = cacheRoot;
    }

    /// <summary>
    /// The thumbnail to serve for one owner/kind at the requested width. Prefers a fresh cached
    /// derivative; regenerates it when the canonical source changed; falls back to the cached
    /// derivative when the source is offline, and to the full canonical file when no derivative
    /// exists yet. Returns null only when neither a derivative nor a source is available.
    /// </summary>
    public Task<ArtworkThumbnail?> GetThumbnailAsync(
        string scope,
        Guid ownerId,
        string kind,
        string folder,
        int width,
        CancellationToken cancellationToken) =>
        GetThumbnailForSourceAsync(
            CacheKey(scope, ownerId, kind),
            () => store.ResolveAsync(scope, ownerId, kind, folder, cancellationToken),
            width,
            cancellationToken);

    /// <summary>
    /// Thumbnail for a canonical artwork file whose path is resolved elsewhere (e.g. a Books cover
    /// resolved through <c>BookCatalogService</c>). <paramref name="cacheKey"/> must identify the
    /// media/size deterministically; <paramref name="resolveSource"/> yields the current canonical
    /// path (or null when the NAS is offline). Prefers a fresh cached derivative, regenerates it
    /// when the source changed, and keeps serving the cached one when the source is unavailable.
    /// </summary>
    public async Task<ArtworkThumbnail?> GetThumbnailForSourceAsync(
        string cacheKey,
        Func<Task<string?>> resolveSource,
        int width,
        CancellationToken cancellationToken)
    {
        var safeWidth = Math.Clamp(width, 16, 1600);
        var cachePath = Path.Combine(root, $"{cacheKey}-{safeWidth}.webp");
        var stampPath = cachePath + ".stamp";

        var source = await resolveSource();
        if (source is not null && File.Exists(source))
        {
            var info = new FileInfo(source);
            var stamp = Stamp(info, safeWidth);
            if (!await IsFreshAsync(cachePath, stampPath, stamp, cancellationToken))
            {
                await GenerateAsync(source, cachePath, stampPath, stamp, safeWidth, cancellationToken);
            }
        }

        if (File.Exists(cachePath))
        {
            return new ArtworkThumbnail(cachePath, "image/webp");
        }

        // No derivative yet: while the source is reachable serve it directly; otherwise the caller
        // shows a placeholder.
        return source is not null && File.Exists(source)
            ? new ArtworkThumbnail(source, ContentType(source))
            : null;
    }

    /// <summary>
    /// Removes cache files whose key is not in <paramref name="liveKeys"/> (obtained from
    /// <see cref="CacheKey"/>), so derivatives for deleted media or dropped sizes are reclaimed.
    /// </summary>
    public int CleanupOrphans(IReadOnlySet<string> liveKeys)
    {
        if (!Directory.Exists(root))
        {
            return 0;
        }

        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*.webp", SearchOption.TopDirectoryOnly))
        {
            // File names are "{cacheKey}-{width}.webp"; the live set is keyed by cacheKey.
            var name = System.IO.Path.GetFileNameWithoutExtension(file);
            var dash = name.LastIndexOf('-');
            var key = dash > 0 ? name[..dash] : name;
            if (liveKeys.Contains(key))
            {
                continue;
            }

            TryDelete(file);
            TryDelete(file + ".stamp");
            removed++;
        }

        return removed;
    }

    /// <summary>The deterministic cache key for one owner/kind; the width is added to the file name.</summary>
    public static string CacheKey(string scope, Guid ownerId, string kind)
    {
        var raw = string.Join('\u001f', DerivativeVersion, scope, ownerId.ToString("N"), kind);
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(hash)[..32];
    }

    private static string Stamp(FileInfo info, int width) =>
        $"{info.Length}:{info.LastWriteTimeUtc.Ticks}:{DerivativeVersion}:{width}";

    private static async Task<bool> IsFreshAsync(
        string cachePath,
        string stampPath,
        string expected,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(cachePath) || !File.Exists(stampPath))
        {
            return false;
        }

        try
        {
            var actual = await File.ReadAllTextAsync(stampPath, cancellationToken);
            return string.Equals(actual, expected, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task GenerateAsync(
        string source,
        string cachePath,
        string stampPath,
        string stamp,
        int width,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(source, cancellationToken);
            var webp = Encode(bytes, width);
            if (webp is null)
            {
                return;
            }

            Directory.CreateDirectory(root);
            await WriteAtomicAsync(cachePath, webp, cancellationToken);
            await WriteAtomicAsync(stampPath, System.Text.Encoding.UTF8.GetBytes(stamp), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A NAS that disappears mid-generation must not break the request; the caller falls back.
        }
    }

    private static byte[]? Encode(byte[] source, int maxWidth)
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
            if (decoded.Width > maxWidth)
            {
                var height = Math.Max(1, (int)Math.Round(decoded.Height * (maxWidth / (double)decoded.Width)));
                resized = decoded.Resize(new SKSizeI(maxWidth, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
                if (resized is null)
                {
                    return null;
                }

                output = resized;
            }

            using var data = output.Encode(SKEncodedImageFormat.Webp, DerivativeQuality);
            return data is null || data.Size == 0 ? null : data.ToArray();
        }
        finally
        {
            resized?.Dispose();
        }
    }

    private static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static string ContentType(string path) =>
        System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };

    private static void TryDelete(string path)
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
}
