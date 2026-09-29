namespace Jularr.Web.Features.Appearance;

/// <summary>
/// The finite, built-in set of application themes. A theme changes presentation tokens only;
/// routes, navigation, markup and media data stay owned by the application.
/// </summary>
public sealed record AppThemeDefinition(
    string Id,
    string NameKey,
    string DescriptionKey,
    string LogoPath,
    bool UsesOriginalArtwork);

public static class ThemeCatalog
{
    public const string Original = "original";
    public const string CleanPurple = "clean-purple";
    public const string CleanSummit = "clean-summit";
    public const string CleanOrbit = "clean-orbit";
    public const string CleanHorizon = "clean-horizon";

    public static IReadOnlyList<AppThemeDefinition> All { get; } =
    [
        new(Original, "theme.catalog.original.name", "theme.catalog.original.description", "/brand/jularr-mark.svg", true),
        new(CleanPurple, "theme.catalog.cleanPurple.name", "theme.catalog.cleanPurple.description", "/brand/jularr-play.svg", false),
        new(CleanSummit, "theme.catalog.cleanSummit.name", "theme.catalog.cleanSummit.description", "/brand/themes/clean-summit.png", false),
        new(CleanOrbit, "theme.catalog.cleanOrbit.name", "theme.catalog.cleanOrbit.description", "/brand/themes/clean-orbit.png", false),
        new(CleanHorizon, "theme.catalog.cleanHorizon.name", "theme.catalog.cleanHorizon.description", "/brand/themes/clean-horizon.png", false)
    ];

    public static bool TryGet(string? id, out AppThemeDefinition theme)
    {
        theme = All.FirstOrDefault(candidate =>
            candidate.Id.Equals(id?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? All[0];
        return theme.Id.Equals(id?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeOrOriginal(string? id) =>
        TryGet(id, out var theme) ? theme.Id : Original;
}
