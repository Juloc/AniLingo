using System.Globalization;
using System.Text.RegularExpressions;

namespace Jularr.Tests;

// The owner does not want coloured stripes on the left edge of cards and rows. State is shown
// with a tinted background, font weight or a small dot instead. Full borders, 1px dividers and
// soft blurred shading stay allowed.
[TestClass]
public sealed partial class CssAccentStripeTests
{
    [TestMethod]
    public void StylesheetsHaveNoLeftEdgeStripes()
    {
        var cssRoot = Path.Combine(FindRepositoryRoot(), "src", "Jularr.Web", "wwwroot", "css");
        var violations = new List<string>();

        foreach (var path in Directory.EnumerateFiles(cssRoot, "*.css", SearchOption.AllDirectories))
        {
            var css = Comment().Replace(File.ReadAllText(path), "");
            foreach (Match rule in InnermostRule().Matches(css))
            {
                var selector = Whitespace().Replace(rule.Groups["selector"].Value, " ").Trim();
                foreach (var declaration in rule.Groups["body"].Value.Split(';'))
                {
                    var colon = declaration.IndexOf(':', StringComparison.Ordinal);
                    if (colon < 0) continue;

                    var property = declaration[..colon].Trim().ToLowerInvariant();
                    var value = declaration[(colon + 1)..].Trim().ToLowerInvariant();
                    if (IsLeftStripe(property, value))
                    {
                        violations.Add($"{Path.GetRelativePath(cssRoot, path)}: {selector} {{ {property}: {value} }}");
                    }
                }
            }
        }

        Assert.AreEqual(
            0,
            violations.Count,
            "Left-edge stripes are not allowed; use a tinted background, font weight or a small dot:\n"
            + string.Join("\n", violations));
    }

    [TestMethod]
    [DataRow("border-left", "3px solid var(--accent)", true)]
    [DataRow("border-inline-start", "2px solid #c8102e", true)]
    [DataRow("border-left", "thick solid red", true)]
    [DataRow("border-left-width", "4px", true)]
    [DataRow("border-inline-start", ".2rem solid color-mix(in srgb, var(--accent) 50%, transparent)", true)]
    [DataRow("box-shadow", "inset rgba(200, 16, 46, .8) 3px 0", true)]
    [DataRow("box-shadow", "inset 3px 0 0 var(--accent)", true)]
    [DataRow("box-shadow", "0 1px 2px rgba(0,0,0,.2), inset 4px 0 var(--accent)", true)]
    [DataRow("border-left", "1px solid var(--border)", false)]
    [DataRow("border-left", "0", false)]
    [DataRow("border-left", "3px solid transparent", false)]
    [DataRow("border-left", "3px dashed var(--accent)", false)]
    [DataRow("border", "3px solid var(--accent)", false)]
    [DataRow("box-shadow", "inset 0 0 0 1px var(--accent)", false)]
    [DataRow("box-shadow", "inset 0 -3px 0 var(--accent)", false)]
    [DataRow("box-shadow", "inset 18px 0 30px -34px rgba(0,0,0,.45)", false)]
    public void DetectsStripeDeclarations(string property, string value, bool expected)
    {
        Assert.AreEqual(expected, IsLeftStripe(property, value));
    }

    private static bool IsLeftStripe(string property, string value) => property switch
    {
        "border-left" or "border-inline-start" => IsSolidBorderStripe(WithoutFunctions(value)),
        "border-left-width" or "border-inline-start-width" => WidthAtLeastTwoPixels(WithoutFunctions(value)),
        "box-shadow" => SplitLayers(value).Any(layer => IsInsetLeftStripe(WithoutFunctions(layer))),
        _ => false
    };

    // Colours are removed first (var(), rgba(), color-mix() ...), so "transparent" here is the
    // border colour keyword and not an argument of a colour function.
    private static bool IsSolidBorderStripe(string value) =>
        Regex.IsMatch(value, @"\bsolid\b")
        && !Regex.IsMatch(value, @"\btransparent\b")
        && WidthAtLeastTwoPixels(value);

    private static bool WidthAtLeastTwoPixels(string value) =>
        Regex.IsMatch(value, @"\b(medium|thick)\b")
        || Length().Matches(value).Any(match => Pixels(match) >= 2);

    // inset <x> <y> [blur] [spread] <colour>: a hard (unblurred) inset shadow with a positive x
    // and no vertical offset paints a solid bar along the left edge.
    private static bool IsInsetLeftStripe(string layer)
    {
        if (!Regex.IsMatch(layer, @"\binset\b")) return false;

        var lengths = Length().Matches(layer).Select(Pixels).ToArray();
        if (lengths.Length < 2) return false;

        var x = lengths[0];
        var y = lengths[1];
        var blur = lengths.Length > 2 ? lengths[2] : 0;
        return x >= 2 && y == 0 && blur == 0;
    }

    private static IEnumerable<string> SplitLayers(string value)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '(') depth++;
            else if (value[i] == ')') depth--;
            else if (value[i] == ',' && depth == 0)
            {
                yield return value[start..i];
                start = i + 1;
            }
        }

        yield return value[start..];
    }

    private static string WithoutFunctions(string value)
    {
        var previous = "";
        while (previous != value)
        {
            previous = value;
            value = InnermostFunction().Replace(value, " ");
        }

        return value;
    }

    private static double Pixels(Match match)
    {
        var number = double.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture);
        return match.Groups["unit"].Value is "em" or "rem" ? number * 16 : number;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Jularr.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Jularr repository root.");
    }

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"(?<selector>[^{};]+)\{(?<body>[^{}]*)\}")]
    private static partial Regex InnermostRule();

    // Unitless numbers count as pixels so "inset 3px 0 0 red" parses as x=3, y=0, blur=0.
    [GeneratedRegex(@"(?<![\w.#-])(?<number>-?\d*\.?\d+)(?:(?<unit>px|rem|em)\b|(?![\w.%]))")]
    private static partial Regex Length();

    [GeneratedRegex(@"[\w-]+\([^()]*\)")]
    private static partial Regex InnermostFunction();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
