using System.Text.RegularExpressions;

namespace Jularr.Web.Features.Novels;

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

    /// <summary>
    /// True when a chapter title is only a numbering ("Chapter 3", "第三章", "12"). The
    /// reader then shows it alone instead of under a second "Chapter N" label.
    /// </summary>
    public static bool IsNumberingTitle(string? title) =>
        !string.IsNullOrWhiteSpace(title) && NumberingTitle().IsMatch(title.Trim());

    /// <summary>
    /// True when a paragraph only repeats the chapter title (sources start the body with the
    /// heading the chapter opening already shows).
    /// </summary>
    public static bool IsTitleEcho(string? paragraph, string? title)
    {
        static string Key(string? value) =>
            Whitespace().Replace(value ?? "", "").Trim('.', '。', ':', '：').ToUpperInvariant();

        var key = Key(title);
        return key.Length > 0 && key == Key(paragraph);
    }

    [GeneratedRegex(
        @"^(?:(?:chapter|chap\.|kapitel|chapitre|cap[ií]tulo|part|teil)\s*[0-9ivxlcdm]+|第[0-9０-９一二三四五六七八九十百千〇零]+[章話部]|[0-9０-９]+)\.?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex NumberingTitle();

    [GeneratedRegex(@"\n\s*\n+", RegexOptions.CultureInvariant)]
    private static partial Regex ParagraphSeparator();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
