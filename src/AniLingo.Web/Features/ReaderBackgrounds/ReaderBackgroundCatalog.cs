using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Hosting;

namespace AniLingo.Web.Features.ReaderBackgrounds;

public sealed record ReaderBackgroundAsset(
    string Id,
    string Genre,
    string GenreLabel,
    string Variant,
    string Name,
    string Url);

public sealed class ReaderBackgroundCatalog
{
    public const string AssetRoot = "reader-backgrounds";

    private static readonly HashSet<string> ImageExtensions = new(
        [".webp", ".avif", ".png", ".jpg", ".jpeg"],
        StringComparer.OrdinalIgnoreCase);

    private readonly string _assetRootPath;

    public ReaderBackgroundCatalog(IWebHostEnvironment environment)
        : this(Path.Combine(
            environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"),
            AssetRoot))
    {
    }

    public ReaderBackgroundCatalog(string assetRootPath)
    {
        _assetRootPath = Path.GetFullPath(assetRootPath);
    }

    public IReadOnlyList<ReaderBackgroundAsset> GetAll()
    {
        if (!Directory.Exists(_assetRootPath))
        {
            return [];
        }

        var items = new List<ReaderBackgroundAsset>();

        foreach (var genreDirectory in Directory
                     .EnumerateDirectories(_assetRootPath)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var genreKey = NormalizeKey(Path.GetFileName(genreDirectory));
            if (genreKey.Length == 0)
            {
                continue;
            }

            var genreLabel = Humanize(genreKey);

            foreach (var path in Directory
                         .EnumerateFiles(genreDirectory, "*", SearchOption.AllDirectories)
                         .Where(IsSupportedImage)
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var relativeToGenre = Path
                    .GetRelativePath(genreDirectory, path)
                    .Replace('\\', '/');
                var extension = Path.GetExtension(relativeToGenre);
                var variantPath = relativeToGenre[..^extension.Length];
                var variantKey = NormalizeKey(variantPath.Replace('/', '-'));
                if (variantKey.Length == 0)
                {
                    continue;
                }

                var relativeToRoot = Path
                    .GetRelativePath(_assetRootPath, path)
                    .Replace('\\', '/');
                var url = "/" + AssetRoot + "/" + string.Join(
                    "/",
                    relativeToRoot
                        .Split('/', StringSplitOptions.RemoveEmptyEntries)
                        .Select(Uri.EscapeDataString));

                items.Add(new ReaderBackgroundAsset(
                    $"{genreKey}/{variantKey}",
                    genreKey,
                    genreLabel,
                    variantKey,
                    Humanize(variantKey),
                    url));
            }
        }

        return items;
    }

    public ReaderBackgroundAsset? FindBestMatch(IEnumerable<string?> genres)
    {
        var items = GetAll();
        if (items.Count == 0)
        {
            return null;
        }

        foreach (var genre in genres)
        {
            var key = NormalizeKey(genre);
            if (key.Length == 0)
            {
                continue;
            }

            var match = items.FirstOrDefault(
                item => string.Equals(item.Genre, key, StringComparison.Ordinal));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    public ReaderBackgroundAsset? FindById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var normalized = id.Trim().Replace('\\', '/').Trim('/');
        return GetAll().FirstOrDefault(
            item => string.Equals(item.Id, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var input = value.Trim().Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(input.Length);
        var separatorPending = false;

        foreach (var character in input)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                if (separatorPending && result.Length > 0)
                {
                    result.Append('-');
                }

                result.Append(char.ToLowerInvariant(character));
                separatorPending = false;
                continue;
            }

            separatorPending = result.Length > 0;
        }

        return result.ToString().Trim('-').Normalize(NormalizationForm.FormC);
    }

    private static bool IsSupportedImage(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path));

    private static string Humanize(string key)
    {
        var words = key.Replace('-', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(words);
    }
}
