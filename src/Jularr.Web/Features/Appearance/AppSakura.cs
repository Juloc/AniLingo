namespace Jularr.Web.Features.Appearance;

/// <summary>
/// The optional Sakura petal effect (#387): a per-profile setting with three states. Petal colour
/// always comes from the profile's accent tokens; this type only owns the on/off/density choice.
/// </summary>
public static class AppSakura
{
    public const string Off = "off";
    public const string Subtle = "subtle";
    public const string Full = "full";

    /// <summary>Recommended default from #387: the normal background effect starts subtle.</summary>
    public const string Default = Subtle;

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized is Off or Subtle or Full)
        {
            return true;
        }

        normalized = Default;
        return false;
    }

    public static string NormalizeOrDefault(string? value) =>
        TryNormalize(value, out var normalized)
            ? normalized
            : Default;
}
