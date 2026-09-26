using System.Globalization;
using System.Text;

namespace AniLingo.Web.Features.Appearance;

/// <summary>One selectable accent in Settings → Appearance. The label comes from the UI catalog.</summary>
public sealed record AccentPreset(string Key, string Seed);

/// <summary>
/// The per-profile accent colour. A profile stores only its seed (or nothing, meaning the Jularr
/// brand red); every other colour in the interface is derived from that seed by
/// <see cref="AccentPalette"/> on each request, so nothing derived is ever persisted.
/// </summary>
public static class AppAccent
{
    /// <summary>Jularr red — the ink-seal red of the brand mark and the default accent.</summary>
    public const string BrandSeed = "#c8102e";

    public static IReadOnlyList<AccentPreset> Presets { get; } =
    [
        new("jularr", BrandSeed),
        new("sakura", "#d9577a"),
        new("kohaku", "#e0a31a"),
        new("matcha", "#5b8a3c"),
        new("sora", "#1b8aa0"),
        new("ai", "#2c55a8"),
        new("fuji", "#7a58b0"),
        new("sumi", "#1b1a19"),
        new("gin", "#8a8a8a"),
    ];

    /// <summary>
    /// Normalises a user-supplied accent. An empty value is valid and means "brand default";
    /// anything else must be a #rrggbb colour.
    /// </summary>
    public static bool TryNormalize(string? value, out string? normalized)
    {
        var text = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (text.Length == 0)
        {
            normalized = null;
            return true;
        }

        if (text.Length == 6 && ColorMath.IsHex("#" + text))
        {
            text = "#" + text;
        }

        normalized = ColorMath.IsHex(text) ? text : null;
        return normalized is not null;
    }

    /// <summary>The seed actually used for rendering: the stored accent, or the brand red.</summary>
    public static string Effective(string? stored) =>
        TryNormalize(stored, out var normalized) && normalized is not null ? normalized : BrandSeed;
}

/// <summary>
/// Turns one seed colour into the complete light and dark token sets of the app shell — accent
/// scale, tinted neutrals, chart colours and artwork tint — following the FullWorth theme engine
/// (OKLCH lightness targets per step, monochrome handling below C 0.025, neutrals tinted towards the
/// seed hue). Unlike a fixed lightness table, every text/background pair is then verified against
/// WCAG contrast and nudged in lightness until it passes, so any colour a user picks stays readable.
/// </summary>
public sealed class AccentPalette
{
    /// <summary>Normal-size text on its background (WCAG AA).</summary>
    public const double TextContrast = 4.5;

    /// <summary>Secondary body text keeps a comfortable margin above AA.</summary>
    public const double SecondaryTextContrast = 7.0;

    private const double MonochromeChroma = 0.025;
    private const string LightInk = "#ffffff";

    private static readonly double BrandArtHue = ColorMath.HslHue(AppAccent.BrandSeed);

    private AccentPalette(
        string seed,
        bool isMonochrome,
        IReadOnlyDictionary<string, string> light,
        IReadOnlyDictionary<string, string> dark)
    {
        Seed = seed;
        IsMonochrome = isMonochrome;
        Light = light;
        Dark = dark;
    }

    public string Seed { get; }
    public bool IsMonochrome { get; }
    public IReadOnlyDictionary<string, string> Light { get; }
    public IReadOnlyDictionary<string, string> Dark { get; }

    public IReadOnlyDictionary<string, string> For(string mode) =>
        mode == AppTheme.Dark ? Dark : Light;

    public static AccentPalette Build(string? storedAccent)
    {
        var seed = AppAccent.Effective(storedAccent);
        var oklch = ColorMath.HexToOklch(seed);
        var mono = oklch.C < MonochromeChroma;
        return new AccentPalette(
            seed,
            mono,
            BuildMode(oklch, mono, dark: false),
            BuildMode(oklch, mono, dark: true));
    }

    /// <summary>
    /// CSS that installs the palette on <c>:root</c> for all three theme modes. Rendered inline in
    /// the document head, so the first paint already uses the profile's colours.
    /// </summary>
    public string ToStyleSheet()
    {
        var builder = new StringBuilder(4096);
        AppendBlock(builder, ":root,:root[data-app-theme=\"light\"]", "light", Light);
        AppendBlock(builder, ":root[data-app-theme=\"dark\"]", "dark", Dark);
        builder.Append("@media (prefers-color-scheme: dark){");
        AppendBlock(builder, ":root[data-app-theme=\"system\"]", "dark", Dark);
        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendBlock(
        StringBuilder builder,
        string selector,
        string colorScheme,
        IReadOnlyDictionary<string, string> tokens)
    {
        builder.Append(selector).Append("{color-scheme:").Append(colorScheme).Append(';');
        foreach (var (name, value) in tokens)
        {
            builder.Append(name).Append(':').Append(value).Append(';');
        }

        builder.Append('}');
    }

    private static Dictionary<string, string> BuildMode(OklchColor seed, bool mono, bool dark)
    {
        var hue = seed.H;
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);

        // Neutrals: a whisper of the seed hue (FullWorth: min(0.007, C·0.035)); the washi paper
        // texture adds the warmth. None at all for monochrome seeds.
        var solidChroma = mono ? 0 : Math.Clamp(seed.C, 0.09, 0.25);
        var neutralChroma = mono ? 0 : Math.Min(0.007, solidChroma * 0.035);
        string Neutral(double l, double chromaFactor = 1) =>
            ColorMath.OklchToHex(new OklchColor(l, neutralChroma * chromaFactor, hue));

        var n = dark
            ? new NeutralSteps(0.165, 0.205, 0.245, 0.285, 0.185, 0.18, 0.18, 0.195, 0.235, 0.315, 0.40, 0.955, 0.82, 0.71)
            : new NeutralSteps(0.962, 0.992, 0.948, 0.918, 0.975, 0.978, 0.995, 0.985, 0.952, 0.875, 0.80, 0.21, 0.41, 0.50);

        var bg = Neutral(n.Bg);
        var surface = Neutral(n.Surface);
        var surface2 = Neutral(n.Surface2);
        var surface3 = Neutral(n.Surface3);
        var raised = Neutral(n.Raised);
        var sidebar = Neutral(n.Sidebar);
        var field = Neutral(n.Field);
        var row = Neutral(n.Row);
        var rowHover = Neutral(n.RowHover);
        string[] textGrounds = [bg, surface, surface2, raised, sidebar, field, row, rowHover];

        var textStep = dark ? 0.01 : -0.01;
        var text = EnsureContrast(new OklchColor(n.Text, neutralChroma * 1.2, hue), textGrounds, 12, textStep);
        var textSecondary = EnsureContrast(new OklchColor(n.TextSecondary, neutralChroma, hue), textGrounds, SecondaryTextContrast, textStep);
        var muted = EnsureContrast(new OklchColor(n.Muted, neutralChroma, hue), [.. textGrounds, surface3], TextContrast, textStep);

        tokens["--bg"] = bg;
        tokens["--surface"] = surface;
        tokens["--surface-2"] = surface2;
        tokens["--surface-3"] = surface3;
        tokens["--surface-strong"] = surface3;
        tokens["--surface-raised"] = raised;
        tokens["--sidebar-bg"] = sidebar;
        tokens["--field-bg"] = field;
        tokens["--row-bg"] = row;
        tokens["--row-hover"] = rowHover;
        tokens["--border"] = Neutral(n.Border);
        tokens["--border-strong"] = Neutral(n.BorderStrong);
        tokens["--text"] = text;
        tokens["--text-secondary"] = textSecondary;
        tokens["--muted"] = muted;
        tokens["--soft-fill"] = text + "10";
        tokens["--panel-overlay"] = surface + "f5";
        tokens["--panel-overlay-strong"] = surface + "fa";
        tokens["--browser-theme-color"] = bg;

        // Accent: keep the user's own lightness when it works in this mode, so the chosen colour
        // appears as chosen; clamp it into a usable band otherwise.
        OklchColor solid;
        if (mono)
        {
            // FullWorth's monochrome branch: black/white/grey seeds produce a genuinely
            // black-and-white interface (near-black actions in light mode, near-white in dark).
            solid = new OklchColor(dark ? 0.93 : 0.22, 0, hue);
        }
        else
        {
            solid = new OklchColor(
                dark ? Math.Clamp(seed.L, 0.50, 0.80) : Math.Clamp(seed.L, 0.40, 0.82),
                solidChroma,
                hue);
        }

        var (solidHex, onAccent) = ResolveSolid(solid);
        var solidFinal = ColorMath.HexToOklch(solidHex);
        var hoverDirection = onAccent == LightInk ? -0.05 : 0.05;
        var hoverHex = ColorMath.OklchToHex(solidFinal with { L = solidFinal.L + hoverDirection });
        if (ColorMath.Contrast(hoverHex, onAccent) < TextContrast)
        {
            hoverHex = solidHex;
        }

        var soft = ColorMath.OklchToHex(dark
            ? new OklchColor(0.285, Math.Min(solidChroma * 0.35, 0.06), hue)
            : new OklchColor(0.935, Math.Min(solidChroma * 0.22, 0.04), hue));
        var accentBorder = ColorMath.OklchToHex(dark
            ? new OklchColor(0.46, solidChroma * 0.45, hue)
            : new OklchColor(0.80, solidChroma * 0.40, hue));

        var accentTextStart = mono
            ? new OklchColor(dark ? 0.90 : 0.25, 0, hue)
            : new OklchColor(dark ? Math.Max(solidFinal.L, 0.74) : Math.Min(solidFinal.L, 0.50), solidChroma * 0.85, hue);
        var accentText = EnsureContrast(accentTextStart, [.. textGrounds, soft], TextContrast, textStep);

        tokens["--accent"] = solidHex;
        tokens["--accent-hover"] = hoverHex;
        tokens["--on-accent"] = onAccent;
        tokens["--accent-soft"] = soft;
        tokens["--accent-border"] = accentBorder;
        tokens["--accent-text"] = accentText;
        tokens["--accent-label"] = accentText;
        tokens["--accent-glow"] = solidHex + "59";
        tokens["--focus-ring"] = accentText;
        tokens["--selection"] = solidHex + (dark ? "66" : "40");

        // Semantic status colours stay fixed per mode: success must look like success whatever
        // the accent is.
        tokens["--success-text"] = dark ? "#8dd2a6" : "#1f6b37";
        tokens["--danger-text"] = dark ? "#f0a3a3" : "#a12a2a";
        tokens["--warning-text"] = dark ? "#f2c65e" : "#7a5300";
        tokens["--info-text"] = dark ? "#9fc1ff" : "#255aa3";

        // Six chart colours rotated from the seed hue; monochrome seeds keep a colourful fixed
        // set, because six greys cannot be told apart in a chart.
        double[] offsets = [0, 55, 118, 182, 245, 310];
        double[] monoHues = [25, 255, 145, 325, 195, 75];
        double[] lightL = [0.56, 0.62, 0.60, 0.58, 0.61, 0.64];
        double[] darkL = [0.72, 0.76, 0.74, 0.73, 0.76, 0.78];
        double[] dataC = [0.20, 0.18, 0.17, 0.19, 0.16, 0.15];
        for (var index = 0; index < offsets.Length; index++)
        {
            var dataHue = mono ? monoHues[index] : (hue + offsets[index]) % 360;
            tokens[$"--data-{index + 1}"] = ColorMath.OklchToHex(
                new OklchColor(dark ? darkL[index] : lightL[index], dataC[index], dataHue));
        }

        // Artwork (ink paintings with red blossoms, the brand ring) is drawn in Jularr red. CSS
        // hue-rotate works in HSL, so the rotation is the HSL hue distance from the brand red;
        // monochrome seeds turn the artwork into pure ink.
        var rotation = ColorMath.HslHue(solidHex) - BrandArtHue;
        rotation = ((rotation % 360) + 540) % 360 - 180;
        tokens["--art-hue-rotate"] = (mono ? 0 : rotation).ToString("0.#", CultureInfo.InvariantCulture) + "deg";
        tokens["--art-saturate"] = mono ? "0" : "1";

        return tokens;
    }

    /// <summary>
    /// Picks the label colour for filled accent surfaces (white preferred, as in the brand) and, if
    /// neither white nor ink reaches AA on the chosen colour, moves the colour's lightness until
    /// one of them does.
    /// </summary>
    private static (string Solid, string OnAccent) ResolveSolid(OklchColor solid)
    {
        var inkHex = ColorMath.OklchToHex(new OklchColor(0.18, Math.Min(solid.C * 0.2, 0.02), solid.H));
        var candidate = solid;
        for (var step = 0; step < 60; step++)
        {
            var hex = ColorMath.OklchToHex(candidate);
            if (ColorMath.Contrast(hex, LightInk) >= TextContrast)
            {
                return (hex, LightInk);
            }

            if (ColorMath.Contrast(hex, inkHex) >= TextContrast)
            {
                return (hex, inkHex);
            }

            // Mid-lightness colours fail both; move towards whichever end is closer.
            candidate = candidate with { L = candidate.L + (solid.L >= 0.62 ? 0.01 : -0.01) };
        }

        return (ColorMath.OklchToHex(candidate), LightInk);
    }

    private static string EnsureContrast(OklchColor start, IReadOnlyList<string> grounds, double target, double step)
    {
        var candidate = start;
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var hex = ColorMath.OklchToHex(candidate);
            if (grounds.All(ground => ColorMath.Contrast(hex, ground) >= target))
            {
                return hex;
            }

            var next = candidate.L + step;
            if (next is < 0 or > 1)
            {
                return hex;
            }

            candidate = candidate with { L = next };
        }

        return ColorMath.OklchToHex(candidate);
    }

    private readonly record struct NeutralSteps(
        double Bg,
        double Surface,
        double Surface2,
        double Surface3,
        double Raised,
        double Sidebar,
        double Field,
        double Row,
        double RowHover,
        double Border,
        double BorderStrong,
        double Text,
        double TextSecondary,
        double Muted);
}
