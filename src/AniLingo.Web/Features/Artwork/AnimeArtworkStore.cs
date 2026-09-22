namespace AniLingo.Web.Features.Artwork;

public enum AnimeArtworkKind
{
    Poster,
    Fanart
}

public static class AnimeArtworkStore
{
    public const string RootPath = "/data/artwork/anime";
    private const long MaxImageBytes = 20 * 1024 * 1024;

    private static readonly string[] SupportedExtensions = [".jpg", ".png", ".webp"];

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
        var directory = Path.Combine(RootPath, animeId.ToString("N"));
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

    public static async Task<bool> SaveAsync(
        Guid animeId,
        AnimeArtworkKind kind,
        Stream source,
        string? contentType,
        CancellationToken cancellationToken)
    {
        var extension = ExtensionForContentType(contentType);
        if (extension is null)
        {
            return false;
        }

        var directory = Path.Combine(RootPath, animeId.ToString("N"));
        Directory.CreateDirectory(directory);

        var finalPath = Path.Combine(directory, ToSlug(kind) + extension);
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

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                if (total == 0)
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

    private static string ToSlug(AnimeArtworkKind kind) =>
        kind == AnimeArtworkKind.Poster ? "poster" : "fanart";

    private static string? ExtensionForContentType(string? contentType) =>
        contentType?.Split(';', 2)[0].Trim().ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => null
        };
}
