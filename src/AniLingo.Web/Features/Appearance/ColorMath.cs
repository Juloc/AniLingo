using System.Globalization;

namespace AniLingo.Web.Features.Appearance;

/// <summary>A colour in OKLCH (Björn Ottosson's OKLab in polar form, CSS Color 4).</summary>
public readonly record struct OklchColor(double L, double C, double H);

/// <summary>
/// Pure colour maths behind the accent palette: sRGB hex ⇄ OKLCH, gamut mapping and WCAG contrast.
/// OKLCH is used because equal steps in L look equally bright across hues, so one set of lightness
/// targets produces a balanced palette for any accent the user picks.
/// </summary>
public static class ColorMath
{
    public static bool IsHex(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;

    public static (double R, double G, double B) HexToRgb(string hex)
    {
        if (!IsHex(hex))
        {
            throw new ArgumentException("Expected a #rrggbb colour.", nameof(hex));
        }

        return (
            Convert.ToInt32(hex.Substring(1, 2), 16) / 255d,
            Convert.ToInt32(hex.Substring(3, 2), 16) / 255d,
            Convert.ToInt32(hex.Substring(5, 2), 16) / 255d);
    }

    public static string RgbToHex(double r, double g, double b) =>
        string.Create(CultureInfo.InvariantCulture, $"#{Byte(r):x2}{Byte(g):x2}{Byte(b):x2}");

    public static OklchColor HexToOklch(string hex)
    {
        var (sr, sg, sb) = HexToRgb(hex);
        var (l, a, b) = LinearRgbToOklab(ToLinear(sr), ToLinear(sg), ToLinear(sb));
        var chroma = Math.Sqrt(a * a + b * b);
        var hue = Math.Atan2(b, a) * 180 / Math.PI;
        return new OklchColor(l, chroma, hue < 0 ? hue + 360 : hue);
    }

    /// <summary>
    /// OKLCH → sRGB hex. Out-of-gamut colours keep their lightness and hue and lose chroma until they
    /// fit (binary search), instead of clipping channels — clipping shifts the hue of saturated
    /// accents, which is exactly the colour the user chose.
    /// </summary>
    public static string OklchToHex(OklchColor color)
    {
        var lightness = Math.Clamp(color.L, 0, 1);
        var target = color with { L = lightness, C = Math.Max(0, color.C) };
        if (TryToRgb(target, out var rgb))
        {
            return RgbToHex(rgb.R, rgb.G, rgb.B);
        }

        double low = 0, high = target.C;
        var best = (R: lightness, G: lightness, B: lightness);
        for (var iteration = 0; iteration < 24; iteration++)
        {
            var mid = (low + high) / 2;
            if (TryToRgb(target with { C = mid }, out var candidate))
            {
                low = mid;
                best = candidate;
            }
            else
            {
                high = mid;
            }
        }

        if (low == 0)
        {
            TryToRgb(target with { C = 0 }, out best);
        }

        return RgbToHex(best.R, best.G, best.B);
    }

    /// <summary>WCAG 2.x relative luminance.</summary>
    public static double RelativeLuminance(string hex)
    {
        var (r, g, b) = HexToRgb(hex);
        return 0.2126 * ToLinear(r) + 0.7152 * ToLinear(g) + 0.0722 * ToLinear(b);
    }

    /// <summary>WCAG 2.x contrast ratio, 1–21.</summary>
    public static double Contrast(string first, string second)
    {
        var a = RelativeLuminance(first);
        var b = RelativeLuminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>HSL hue in degrees, the space CSS <c>hue-rotate()</c> approximately works in.</summary>
    public static double HslHue(string hex)
    {
        var (r, g, b) = HexToRgb(hex);
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        if (delta < 1e-9)
        {
            return 0;
        }

        double hue;
        if (max == r)
        {
            hue = 60 * (((g - b) / delta) % 6);
        }
        else if (max == g)
        {
            hue = 60 * (((b - r) / delta) + 2);
        }
        else
        {
            hue = 60 * (((r - g) / delta) + 4);
        }

        return hue < 0 ? hue + 360 : hue;
    }

    private static bool TryToRgb(OklchColor color, out (double R, double G, double B) rgb)
    {
        const double epsilon = 1e-6;
        var radians = color.H * Math.PI / 180;
        var a = color.C * Math.Cos(radians);
        var b = color.C * Math.Sin(radians);

        var l_ = color.L + 0.3963377774 * a + 0.2158037573 * b;
        var m_ = color.L - 0.1055613458 * a - 0.0638541728 * b;
        var s_ = color.L - 0.0894841775 * a - 1.2914855480 * b;

        var l = l_ * l_ * l_;
        var m = m_ * m_ * m_;
        var s = s_ * s_ * s_;

        var lr = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
        var lg = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
        var lb = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;

        rgb = (FromLinear(lr), FromLinear(lg), FromLinear(lb));
        return lr >= -epsilon && lr <= 1 + epsilon
            && lg >= -epsilon && lg <= 1 + epsilon
            && lb >= -epsilon && lb <= 1 + epsilon;
    }

    private static (double L, double A, double B) LinearRgbToOklab(double r, double g, double b)
    {
        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);

        return (
            0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    private static double ToLinear(double channel) =>
        channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

    private static double FromLinear(double channel)
    {
        var clamped = Math.Clamp(channel, 0, 1);
        return clamped <= 0.0031308 ? clamped * 12.92 : 1.055 * Math.Pow(clamped, 1 / 2.4) - 0.055;
    }

    private static int Byte(double channel) => (int)Math.Round(Math.Clamp(channel, 0, 1) * 255);
}
