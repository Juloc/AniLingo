using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;

namespace AniLingo.Web.Features.ReaderThemes;

public sealed record ReaderThemeImageSources(
    string? Default,
    string? Mobile,
    string? Tablet,
    string? Desktop,
    string? Wide);

public sealed record ReaderThemeAssets(
    ReaderThemeImageSources? Page,
    ReaderThemeImageSources? ScrollStatic,
    ReaderThemeImageSources? ParallaxBack,
    ReaderThemeImageSources? ParallaxMid,
    ReaderThemeImageSources? ParallaxFront);

public sealed record ReaderThemeAppearance(
    double Overlay,
    double Brightness,
    double Contrast,
    double Saturation,
    double BlurPx,
    double Vignette,
    double Grain,
    double TextBackdrop,
    string Tint,
    double TintStrength,
    double PageShadow,
    double PageCurl);

public sealed record ReaderThemeMotion(
    string Effect,
    double EffectStrength,
    double ParallaxStrength,
    double BackSpeed,
    double MidSpeed,
    double FrontSpeed);

public sealed record ReaderThemeDescriptor(
    string Id,
    string Genre,
    string GenreLabel,
    string Variant,
    string Name,
    IReadOnlyList<string> Aliases,
    ReaderThemeAssets Assets,
    ReaderThemeAppearance Appearance,
    ReaderThemeMotion Motion,
    string? Extends)
{
    public bool HasParallax =>
        Assets.ParallaxBack is not null ||
        Assets.ParallaxMid is not null ||
        Assets.ParallaxFront is not null;
}

public sealed partial class ReaderThemeCatalog
{
    public const string AssetRoot = "reader-backgrounds";

    private static readonly string[] ImageExtensions =
        [".webp", ".avif", ".png", ".jpg", ".jpeg"];

    private static readonly HashSet<string> AllowedEffects =
        new(
            ["none", "fog", "rain", "embers", "stars", "shimmer"],
            StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _assetRootPath;

    public ReaderThemeCatalog(IWebHostEnvironment environment)
        : this(Path.Combine(
            environment.WebRootPath ??
                Path.Combine(environment.ContentRootPath, "wwwroot"),
            AssetRoot))
    {
    }

    public ReaderThemeCatalog(string assetRootPath)
    {
        _assetRootPath = Path.GetFullPath(assetRootPath);
    }

    public IReadOnlyList<ReaderThemeDescriptor> GetAll()
    {
        if (!Directory.Exists(_assetRootPath))
        {
            return [];
        }

        var pending = new Dictionary<string, PendingTheme>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var genreDirectory in Directory
                     .EnumerateDirectories(_assetRootPath)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var genre = NormalizeKey(Path.GetFileName(genreDirectory));
            if (genre.Length == 0)
            {
                continue;
            }

            foreach (var flatImage in Directory
                         .EnumerateFiles(genreDirectory, "*", SearchOption.TopDirectoryOnly)
                         .Where(IsSupportedImage)
                         .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var theme = BuildFlatTheme(genre, flatImage);
                if (theme is not null)
                {
                    pending[theme.Id] = theme;
                }
            }

            foreach (var themeDirectory in Directory
                         .EnumerateDirectories(genreDirectory)
                         .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var theme = BuildDirectoryTheme(genre, themeDirectory);
                if (theme is not null)
                {
                    // A directory theme intentionally replaces a same-id legacy flat image.
                    pending[theme.Id] = theme;
                }
            }
        }

        var resolved = new Dictionary<string, ReaderThemeDescriptor>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var id in pending.Keys)
        {
            ResolveTheme(
                id,
                pending,
                resolved,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        return resolved.Values
            .OrderBy(x => x.GenreLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public ReaderThemeDescriptor? FindById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var normalized = NormalizeThemeId(id);
        if (normalized.Length == 0)
        {
            return null;
        }

        return GetAll().FirstOrDefault(
            x => string.Equals(x.Id, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public ReaderThemeDescriptor? FindBestMatch(IEnumerable<string?> genres)
    {
        var themes = GetAll();
        if (themes.Count == 0)
        {
            return null;
        }

        foreach (var rawGenre in genres)
        {
            var genre = NormalizeKey(rawGenre);
            if (genre.Length == 0)
            {
                continue;
            }

            var match = themes.FirstOrDefault(theme =>
                string.Equals(theme.Genre, genre, StringComparison.Ordinal) ||
                theme.Aliases.Contains(genre, StringComparer.OrdinalIgnoreCase));

            if (match is not null)
            {
                return match;
            }
        }

        return null;
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
            }
            else
            {
                separatorPending = result.Length > 0;
            }
        }

        return result.ToString().Trim('-').Normalize(NormalizationForm.FormC);
    }

    private PendingTheme? BuildFlatTheme(
        string genre,
        string imagePath)
    {
        var variant = NormalizeKey(Path.GetFileNameWithoutExtension(imagePath));
        if (variant.Length == 0)
        {
            return null;
        }

        var source = new ReaderThemeImageSources(
            UrlFor(imagePath),
            null,
            null,
            null,
            null);

        return new PendingTheme(
            $"{genre}/{variant}",
            genre,
            Humanize(genre),
            variant,
            Humanize(variant),
            [],
            new ReaderThemeAssets(source, source, null, null, null),
            null,
            null,
            null);
    }

    private PendingTheme? BuildDirectoryTheme(
        string genre,
        string themeDirectory)
    {
        var variant = NormalizeKey(Path.GetFileName(themeDirectory));
        if (variant.Length == 0)
        {
            return null;
        }

        var manifest = ReadManifest(themeDirectory);
        var page = FindSources(themeDirectory, "page");
        var scroll = FindSources(themeDirectory, "scroll");
        var back = FindSources(themeDirectory, "parallax-back");
        var mid = FindSources(themeDirectory, "parallax-mid");
        var front = FindSources(themeDirectory, "parallax-front");
        var extends = NormalizeThemeId(manifest?.Extends ?? "");

        if (page is null &&
            scroll is null &&
            back is null &&
            mid is null &&
            front is null &&
            extends.Length == 0)
        {
            return null;
        }

        var aliases = (manifest?.Aliases ?? [])
            .Select(NormalizeKey)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PendingTheme(
            $"{genre}/{variant}",
            genre,
            Humanize(genre),
            variant,
            SafeLabel(manifest?.Label, Humanize(variant)),
            aliases,
            new ReaderThemeAssets(page, scroll, back, mid, front),
            manifest?.Appearance,
            manifest?.Motion,
            extends.Length == 0 ? null : extends);
    }

    private ReaderThemeDescriptor? ResolveTheme(
        string id,
        IReadOnlyDictionary<string, PendingTheme> pending,
        IDictionary<string, ReaderThemeDescriptor> resolved,
        ISet<string> resolving)
    {
        if (resolved.TryGetValue(id, out var existing))
        {
            return existing;
        }

        if (!pending.TryGetValue(id, out var theme) || !resolving.Add(id))
        {
            return null;
        }

        ReaderThemeDescriptor? parent = null;
        if (theme.Extends is not null)
        {
            parent = ResolveTheme(theme.Extends, pending, resolved, resolving);
            if (parent is null)
            {
                resolving.Remove(id);
                return null;
            }
        }

        var assets = ApplyAssetFallbacks(
            MergeAssets(parent?.Assets, theme.Assets));
        if (!HasAnyAsset(assets))
        {
            resolving.Remove(id);
            return null;
        }

        var descriptor = new ReaderThemeDescriptor(
            theme.Id,
            theme.Genre,
            theme.GenreLabel,
            theme.Variant,
            theme.Name,
            theme.Aliases,
            assets,
            ResolveAppearance(theme.Appearance, parent?.Appearance),
            ResolveMotion(theme.Motion, parent?.Motion),
            theme.Extends);

        resolved[id] = descriptor;
        resolving.Remove(id);
        return descriptor;
    }

    private static ReaderThemeAssets MergeAssets(
        ReaderThemeAssets? parent,
        ReaderThemeAssets child) =>
        new(
            MergeSources(parent?.Page, child.Page),
            MergeSources(parent?.ScrollStatic, child.ScrollStatic),
            MergeSources(parent?.ParallaxBack, child.ParallaxBack),
            MergeSources(parent?.ParallaxMid, child.ParallaxMid),
            MergeSources(parent?.ParallaxFront, child.ParallaxFront));

    private static ReaderThemeImageSources? MergeSources(
        ReaderThemeImageSources? parent,
        ReaderThemeImageSources? child)
    {
        if (parent is null)
        {
            return child;
        }

        if (child is null)
        {
            return parent;
        }

        return new ReaderThemeImageSources(
            child.Default ?? parent.Default,
            child.Mobile ?? parent.Mobile,
            child.Tablet ?? parent.Tablet,
            child.Desktop ?? parent.Desktop,
            child.Wide ?? parent.Wide);
    }

    private static ReaderThemeAssets ApplyAssetFallbacks(
        ReaderThemeAssets assets)
    {
        var page = assets.Page ?? assets.ScrollStatic ?? assets.ParallaxBack;
        var scroll = assets.ScrollStatic ?? page;

        return new ReaderThemeAssets(
            page,
            scroll,
            assets.ParallaxBack,
            assets.ParallaxMid,
            assets.ParallaxFront);
    }

    private static bool HasAnyAsset(ReaderThemeAssets assets) =>
        assets.Page is not null ||
        assets.ScrollStatic is not null ||
        assets.ParallaxBack is not null ||
        assets.ParallaxMid is not null ||
        assets.ParallaxFront is not null;

    private ThemeManifest? ReadManifest(string themeDirectory)
    {
        var path = Path.Combine(themeDirectory, "theme.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<ThemeManifest>(stream, ManifestJson);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private ReaderThemeImageSources? FindSources(string themeDirectory, string stem)
    {
        string? Find(string suffix)
        {
            foreach (var extension in ImageExtensions)
            {
                var path = Path.Combine(themeDirectory, $"{stem}{suffix}{extension}");
                if (File.Exists(path))
                {
                    return UrlFor(path);
                }
            }

            return null;
        }

        var @default = Find("");
        var mobile = Find("-mobile");
        var tablet = Find("-tablet");
        var desktop = Find("-desktop");
        var wide = Find("-wide");

        return @default is null &&
               mobile is null &&
               tablet is null &&
               desktop is null &&
               wide is null
            ? null
            : new ReaderThemeImageSources(
                @default,
                mobile,
                tablet,
                desktop,
                wide);
    }

    private string UrlFor(string path)
    {
        var relative = Path.GetRelativePath(_assetRootPath, path)
            .Replace('\\', '/');

        return "/" + AssetRoot + "/" + string.Join(
            "/",
            relative
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
    }

    private static ReaderThemeAppearance ResolveAppearance(
        ThemeAppearanceManifest? source,
        ReaderThemeAppearance? inherited = null)
    {
        var fallback = inherited ?? DefaultAppearance();
        return new ReaderThemeAppearance(
            Clamp(source?.Overlay, 0, .8, fallback.Overlay),
            Clamp(source?.Brightness, .5, 1.5, fallback.Brightness),
            Clamp(source?.Contrast, .5, 1.5, fallback.Contrast),
            Clamp(source?.Saturation, 0, 1.5, fallback.Saturation),
            Clamp(source?.BlurPx, 0, 12, fallback.BlurPx),
            Clamp(source?.Vignette, 0, .8, fallback.Vignette),
            Clamp(source?.Grain, 0, .25, fallback.Grain),
            Clamp(source?.TextBackdrop, 0, .65, fallback.TextBackdrop),
            NormalizeColor(source?.Tint, fallback.Tint),
            Clamp(source?.TintStrength, 0, .5, fallback.TintStrength),
            Clamp(source?.PageShadow, 0, .65, fallback.PageShadow),
            Clamp(source?.PageCurl, 0, 1.5, fallback.PageCurl));
    }

    private static ReaderThemeMotion ResolveMotion(
        ThemeMotionManifest? source,
        ReaderThemeMotion? inherited = null)
    {
        var fallback = inherited ?? DefaultMotion();
        var effect = source?.Effect?.Trim().ToLowerInvariant();
        if (effect is null)
        {
            effect = fallback.Effect;
        }
        else if (!AllowedEffects.Contains(effect))
        {
            effect = "none";
        }

        return new ReaderThemeMotion(
            effect,
            Clamp(source?.EffectStrength, 0, 1, fallback.EffectStrength),
            Clamp(source?.ParallaxStrength, 0, 1, fallback.ParallaxStrength),
            Clamp(source?.BackSpeed, 0, .35, fallback.BackSpeed),
            Clamp(source?.MidSpeed, 0, .45, fallback.MidSpeed),
            Clamp(source?.FrontSpeed, 0, .6, fallback.FrontSpeed));
    }

    private static ReaderThemeAppearance DefaultAppearance() =>
        new(
            Overlay: .08,
            Brightness: .92,
            Contrast: .96,
            Saturation: .9,
            BlurPx: 0,
            Vignette: .12,
            Grain: .02,
            TextBackdrop: .12,
            Tint: "#000000",
            TintStrength: 0,
            PageShadow: .18,
            PageCurl: .8);

    private static ReaderThemeMotion DefaultMotion() =>
        new(
            Effect: "none",
            EffectStrength: 0,
            ParallaxStrength: .55,
            BackSpeed: .08,
            MidSpeed: .16,
            FrontSpeed: .28);

    private static string NormalizeColor(string? value, string fallback)
    {
        var normalized = value?.Trim();
        return normalized is not null && HexColor().IsMatch(normalized)
            ? normalized.ToLowerInvariant()
            : fallback;
    }

    private static string SafeLabel(string? value, string fallback)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? fallback
            : normalized[..Math.Min(80, normalized.Length)];
    }

    private static double Clamp(
        double? value,
        double min,
        double max,
        double fallback) =>
        Math.Round(Math.Clamp(value ?? fallback, min, max), 3);

    private static bool IsSupportedImage(string path) =>
        ImageExtensions.Contains(
            Path.GetExtension(path),
            StringComparer.OrdinalIgnoreCase);

    private static string Humanize(string key) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
            key.Replace('-', ' '));

    private static string NormalizeThemeId(string value)
    {
        var parts = value.Trim()
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length == 2
            ? $"{NormalizeKey(parts[0])}/{NormalizeKey(parts[1])}"
            : "";
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColor();

    private sealed class ThemeManifest
    {
        public string? Label { get; set; }
        public string? Extends { get; set; }
        public string[]? Aliases { get; set; }
        public ThemeAppearanceManifest? Appearance { get; set; }
        public ThemeMotionManifest? Motion { get; set; }
    }

    private sealed class ThemeAppearanceManifest
    {
        public double? Overlay { get; set; }
        public double? Brightness { get; set; }
        public double? Contrast { get; set; }
        public double? Saturation { get; set; }
        public double? BlurPx { get; set; }
        public double? Vignette { get; set; }
        public double? Grain { get; set; }
        public double? TextBackdrop { get; set; }
        public string? Tint { get; set; }
        public double? TintStrength { get; set; }
        public double? PageShadow { get; set; }
        public double? PageCurl { get; set; }
    }

    private sealed class ThemeMotionManifest
    {
        public string? Effect { get; set; }
        public double? EffectStrength { get; set; }
        public double? ParallaxStrength { get; set; }
        public double? BackSpeed { get; set; }
        public double? MidSpeed { get; set; }
        public double? FrontSpeed { get; set; }
    }

    private sealed record PendingTheme(
        string Id,
        string Genre,
        string GenreLabel,
        string Variant,
        string Name,
        IReadOnlyList<string> Aliases,
        ReaderThemeAssets Assets,
        ThemeAppearanceManifest? Appearance,
        ThemeMotionManifest? Motion,
        string? Extends);

}
