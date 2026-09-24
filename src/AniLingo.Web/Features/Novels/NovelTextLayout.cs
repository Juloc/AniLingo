using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Novels;

public static partial class NovelTextLayout
{
    public const int AnchorTextLimit = 180;

    public static IReadOnlyList<string> SplitParagraphs(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        return ParagraphSeparator()
            .Split(normalized)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToArray();
    }

    public static string? CreateAnchorText(string? paragraph)
    {
        if (string.IsNullOrWhiteSpace(paragraph))
        {
            return null;
        }

        var normalized = Whitespace()
            .Replace(paragraph.Trim(), " ");

        return normalized.Length <= AnchorTextLimit
            ? normalized
            : normalized[..AnchorTextLimit];
    }

    public static string NormalizeForAnchor(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? ""
            : Whitespace().Replace(text.Trim(), " ");

    [GeneratedRegex(@"\n\s*\n+", RegexOptions.CultureInvariant)]
    private static partial Regex ParagraphSeparator();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
