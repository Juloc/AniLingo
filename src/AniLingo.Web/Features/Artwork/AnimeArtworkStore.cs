using SkiaSharp;

namespace AniLingo.Web.Features.Artwork;

public enum AnimeArtworkKind
{
    Poster,
    Fanart
}

public static class AnimeArtworkStore
{
    public const string RootPath = "/data/artwork/anime";
    public const int PosterMaxWidth = 512;
    public const int FanartMaxWidth = 1600;

    private const long MaxImageBytes = 20 * 1024 * 1024;
    private const int WebpQuality = 82;
    private const string DerivativeVersion = "v1:webp82:poster512:fanart1600";

    private static readonly string[] SupportedExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public static string? ResolvePosterUrl(Guid animeId, string? fallbackUrl) =>
        GetPublicUrl(animeId, AnimeArtworkKind.Poster) ?? fallbackUrl;

    public static string? ResolveFanartUrl(Guid animeId, string? fallbackUrl) =>
        GetPublicUrl(animeId, AnimeArtworkKind.Fanart) ?? fallbackUrl;

    public static string? GetPublicUrl(Guid animeId, AnimeArtworkKind kind)
    {
        var path = FindPath(animeId, kind);
        if (path is null)
        {
            return null;
        }

        var version = File.GetLastWriteTimeUtc(path).Ticks;
        return $"/artwork/anime/{animeId:D}/{ToSlug(kind)}?v={version}";
    }

    public static string? FindPath(Guid animeId, AnimeArtworkKind kind)
    {
        var directory = GetArtworkDirectory(animeId);
        var name = ToSlug(kind);

        foreach (var extension in SupportedExtensions)
        {
            var path = Path.Combine(directory, name + extension);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    public static string GetContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };

    public static bool IsOptimizedDerivative(Guid animeId, AnimeArtworkKind kind)
    {
        var path = FindPath(animeId, kind);
        if (path is null ||
            !Path.GetExtension(path).Equals(".webp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var markerPath = GetDerivativeMarkerPath(animeId, kind);
        try
        {
            return File.Exists(markerPath) &&
                   string.Equals(
                       File.ReadAllText(markerPath).Trim(),
                       DerivativeVersion,
                       StringComparison.Ordinal);
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

    public static async Task<bool> EnsureOptimizedAsync(
        Guid animeId,
        AnimeArtworkKind kind,
        CancellationToken cancellationToken)
    {
        if (IsOptimizedDerivative(animeId, kind))
        {
            return false;
        }

        var existingPath = FindPath(animeId, kind);
        if (existingPath is null)
        {
            return false;
        }

        var existing = new FileInfo(existingPath);
        if (!existing.Exists || existing.Length == 0 || existing.Length > MaxImageBytes)
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(existingPath, cancellationToken);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        await using var source = new MemoryStream(bytes, writable: false);
        return await SaveAsync(
            animeId,
            kind,
            source,
            GetContentType(existingPath),
            cancellationToken);
    }

    public static async Task<bool> ImportFileIfChangedAsync(
        Guid animeId,
        AnimeArtworkKind kind,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var source = new FileInfo(sourcePath);
        if (!source.Exists)
        {
            return false;
        }

        var contentType = GetContentType(source.FullName);
        if (!IsSupportedContentType(contentType))
        {
            return false;
        }

        var sourceIdentity = BuildSourceIdentity(source);
        if (IsOptimizedDerivative(animeId, kind) &&
            string.Equals(
                await ReadSourceIdentityAsync(animeId, kind, cancellationToken),
                sourceIdentity,
                StringComparison.Ordinal))
        {
            return false;
        }

        await using var stream = new FileStream(
            source.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (!await SaveAsync(
                animeId,
                kind,
                stream,
                contentType,
                cancellationToken))
        {
            return false;
        }

        var importedPath = FindPath(animeId, kind);
        if (importedPath is not null)
        {
            File.SetLastWriteTimeUtc(importedPath, source.LastWriteTimeUtc);
        }

        await WriteTextAtomicAsync(
            GetSourceMarkerPath(animeId, kind),
            sourceIdentity,
            cancellationToken);

        return true;
    }

    public static async Task<bool> SaveAsync(
        Guid animeId,
        AnimeArtworkKind kind,
        Stream source,
        string? contentType,
        CancellationToken cancellationToken)
    {
        if (!IsSupportedContentType(contentType))
        {
            return false;
        }

        var directory = GetArtworkDirectory(animeId);
        Directory.CreateDirectory(directory);

        var finalPath = Path.Combine(directory, ToSlug(kind) + ".webp");
        var temporaryPath = finalPath + ".tmp";

        try
        {
            await using (var output = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                if (!await CreateOptimizedDerivativeAsync(
                        kind,
                        source,
                        output,
                        cancellationToken))
                {
                    return false;
                }
            }

            foreach (var supportedExtension in SupportedExtensions)
            {
                var existingPath = Path.Combine(directory, ToSlug(kind) + supportedExtension);
                if (!string.Equals(existingPath, finalPath, StringComparison.Ordinal) &&
                    File.Exists(existingPath))
                {
                    File.Delete(existingPath);
                }
            }

            File.Move(temporaryPath, finalPath, true);

            // Direct saves (for example Sonarr) replace any previously tracked
            // local source. Local imports write their source identity again
            // immediately after this method succeeds.
            var sourceMarkerPath = GetSourceMarkerPath(animeId, kind);
            if (File.Exists(sourceMarkerPath))
            {
                File.Delete(sourceMarkerPath);
            }

            await WriteTextAtomicAsync(
                GetDerivativeMarkerPath(animeId, kind),
                DerivativeVersion,
                cancellationToken);
            return true;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static async Task<bool> CreateOptimizedDerivativeAsync(
        AnimeArtworkKind kind,
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        await using var buffered = new MemoryStream();
        var buffer = new byte[64 * 1024];
        long total = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > MaxImageBytes)
            {
                return false;
            }

            await buffered.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (total == 0)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        SKBitmap? decoded;
        try
        {
            decoded = SKBitmap.Decode(buffered.ToArray());
        }
        catch (ArgumentNullException)
        {
            return false;
        }

        using var bitmap = decoded;
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            return false;
        }

        var maxWidth = kind == AnimeArtworkKind.Poster
            ? PosterMaxWidth
            : FanartMaxWidth;

        SKBitmap outputBitmap = bitmap;
        SKBitmap? resized = null;

        if (bitmap.Width > maxWidth)
        {
            var height = Math.Max(
                1,
                (int)Math.Round(bitmap.Height * (maxWidth / (double)bitmap.Width)));

            resized = bitmap.Resize(
                new SKSizeI(maxWidth, height),
                new SKSamplingOptions(SKCubicResampler.Mitchell));

            if (resized is null)
            {
                return false;
            }

            outputBitmap = resized;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var data = outputBitmap.Encode(
                SKEncodedImageFormat.Webp,
                WebpQuality);

            if (data is null || data.Size == 0)
            {
                return false;
            }

            var bytes = data.ToArray();
            await destination.WriteAsync(bytes, cancellationToken);
            return true;
        }
        finally
        {
            resized?.Dispose();
        }
    }

    private static string GetArtworkDirectory(Guid animeId) =>
        Path.Combine(RootPath, animeId.ToString("N"));

    private static string GetDerivativeMarkerPath(Guid animeId, AnimeArtworkKind kind) =>
        Path.Combine(
            GetArtworkDirectory(animeId),
            ToSlug(kind) + ".derivative");

    private static string GetSourceMarkerPath(Guid animeId, AnimeArtworkKind kind) =>
        Path.Combine(
            GetArtworkDirectory(animeId),
            ToSlug(kind) + ".source");

    private static string BuildSourceIdentity(FileInfo source) =>
        $"{source.Length}:{source.LastWriteTimeUtc.Ticks}";

    private static async Task<string?> ReadSourceIdentityAsync(
        Guid animeId,
        AnimeArtworkKind kind,
        CancellationToken cancellationToken)
    {
        var path = GetSourceMarkerPath(animeId, kind);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return (await File.ReadAllTextAsync(path, cancellationToken)).Trim();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task WriteTextAtomicAsync(
        string path,
        string value,
        CancellationToken cancellationToken)
    {
        var temporaryPath = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, value, cancellationToken);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool IsSupportedContentType(string? contentType) =>
        contentType?.Split(';', 2)[0].Trim().ToLowerInvariant() is
            "image/jpeg" or
            "image/jpg" or
            "image/png" or
            "image/webp";

    private static string ToSlug(AnimeArtworkKind kind) =>
        kind == AnimeArtworkKind.Poster ? "poster" : "fanart";
}
