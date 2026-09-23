namespace AniLingo.Web.Features.Artwork;

public sealed record LocalArtworkImportResult(
    int ImportedCount,
    int UnchangedCount,
    int MissingCount);

public static class LocalAnimeArtworkImporter
{
    private static readonly string[] PosterBaseNames = ["poster", "folder", "cover"];
    private static readonly string[] FanartBaseNames = ["fanart", "backdrop", "background", "banner"];
    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".webp"];

    public static async Task<LocalArtworkImportResult> ImportAsync(
        Guid animeId,
        string animeDirectory,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(animeDirectory))
        {
            return new LocalArtworkImportResult(0, 0, 2);
        }

        var imported = 0;
        var unchanged = 0;
        var missing = 0;

        foreach (var kind in new[] { AnimeArtworkKind.Poster, AnimeArtworkKind.Fanart })
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourcePath = FindCandidate(animeDirectory, kind);
            if (sourcePath is null)
            {
                missing++;
                continue;
            }

            if (await AnimeArtworkStore.ImportFileIfChangedAsync(
                    animeId,
                    kind,
                    sourcePath,
                    cancellationToken))
            {
                imported++;
            }
            else
            {
                unchanged++;
            }
        }

        return new LocalArtworkImportResult(imported, unchanged, missing);
    }

    public static string? FindCandidate(
        string animeDirectory,
        AnimeArtworkKind kind)
    {
        if (!Directory.Exists(animeDirectory))
        {
            return null;
        }

        var files = Directory
            .EnumerateFiles(animeDirectory, "*", SearchOption.TopDirectoryOnly)
            .ToDictionary(
                path => Path.GetFileName(path),
                path => path,
                StringComparer.OrdinalIgnoreCase);

        var baseNames = kind == AnimeArtworkKind.Poster
            ? PosterBaseNames
            : FanartBaseNames;

        foreach (var baseName in baseNames)
        {
            foreach (var extension in Extensions)
            {
                if (files.TryGetValue(baseName + extension, out var candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
