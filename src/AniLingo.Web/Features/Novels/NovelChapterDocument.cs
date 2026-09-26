using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;

namespace AniLingo.Web.Features.Novels;

/// <summary>
/// One inline run of chapter text. <see cref="Text"/> is the base text that
/// belongs to the paragraph plain text; <see cref="Ruby"/> is an optional
/// reading (furigana) rendered above it and never part of the plain text.
/// </summary>
public sealed record NovelInlineRun(
    [property: JsonPropertyName("t")] string Text,
    [property: JsonPropertyName("r")] string? Ruby = null,
    [property: JsonPropertyName("e")] bool Emphasis = false,
    [property: JsonPropertyName("b")] bool Strong = false);

/// <summary>
/// A sanitized content block: a paragraph (<c>p</c>), a heading (<c>h</c>) or
/// an inline illustration (<c>img</c>). Text blocks map one-to-one and in
/// order to the paragraphs of <see cref="NovelChapter.OriginalText"/>, so
/// progress anchors, bookmarks, highlights and translations keep using plain
/// paragraph indexes and character offsets.
/// </summary>
public sealed record NovelContentBlock(
    [property: JsonPropertyName("k")] string Kind,
    [property: JsonPropertyName("r")] IReadOnlyList<NovelInlineRun>? Runs = null,
    [property: JsonPropertyName("l")] int Level = 0,
    [property: JsonPropertyName("s")] string? Source = null,
    [property: JsonPropertyName("a")] string? Alt = null)
{
    public const string ParagraphKind = "p";
    public const string HeadingKind = "h";
    public const string ImageKind = "img";

    [JsonIgnore]
    public bool IsText => Kind is ParagraphKind or HeadingKind;

    [JsonIgnore]
    public string PlainText =>
        Runs is null ? "" : string.Concat(Runs.Select(run => run.Text));
}

/// <summary>A block as the reader renders it.</summary>
public sealed record NovelReaderBlock(
    int? ParagraphIndex,
    int HeadingLevel,
    IReadOnlyList<NovelInlineRun> Runs,
    string? ImageAsset,
    string? ImageAlt)
{
    public bool IsImage => ImageAsset is not null;
    public bool IsHeading => HeadingLevel > 0;
}

public static partial class NovelChapterDocument
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    // Encodes every HTML-significant character but keeps Japanese text readable.
    private static readonly HtmlEncoder ReaderEncoder = HtmlEncoder.Create(UnicodeRanges.All);

    public static string Serialize(IReadOnlyList<NovelContentBlock> blocks) =>
        JsonSerializer.Serialize(blocks, JsonOptions);

    public static IReadOnlyList<NovelContentBlock> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<NovelContentBlock>>(json, JsonOptions)
            ?? [];
    }

    /// <summary>
    /// The canonical plain text of a block list: text blocks joined as
    /// paragraphs. Import stores exactly this as the chapter's OriginalText.
    /// </summary>
    public static string ToPlainText(IEnumerable<NovelContentBlock> blocks) =>
        string.Join(
            "\n\n",
            blocks.Where(block => block.IsText).Select(block => block.PlainText));

    /// <summary>
    /// Normalizes inline whitespace of a text block the same way paragraph
    /// splitting does: runs of whitespace collapse to one space and the block is
    /// trimmed. Returns null when no base text remains.
    /// </summary>
    public static NovelContentBlock? NormalizeTextBlock(
        string kind,
        int level,
        IEnumerable<NovelInlineRun> runs)
    {
        var normalized = new List<NovelInlineRun>();
        var previousEndsWithSpace = true;

        foreach (var run in runs)
        {
            var text = Whitespace().Replace(run.Text, " ");
            if (previousEndsWithSpace)
            {
                text = text.TrimStart();
            }

            if (text.Length == 0)
            {
                continue;
            }

            var ruby = run.Ruby is null
                ? null
                : Whitespace().Replace(run.Ruby, " ").Trim();

            // A reading only makes sense on non-space base text.
            if (string.IsNullOrEmpty(ruby) || text.Trim().Length == 0)
            {
                ruby = null;
            }

            var candidate = new NovelInlineRun(text, ruby, run.Emphasis, run.Strong);
            if (normalized.Count > 0 &&
                ruby is null &&
                normalized[^1] is { Ruby: null } last &&
                last.Emphasis == candidate.Emphasis &&
                last.Strong == candidate.Strong)
            {
                normalized[^1] = last with { Text = last.Text + text };
            }
            else
            {
                normalized.Add(candidate);
            }

            previousEndsWithSpace = text.EndsWith(' ');
        }

        while (normalized.Count > 0)
        {
            var last = normalized[^1];
            var trimmed = last.Text.TrimEnd();
            if (trimmed.Length == last.Text.Length)
            {
                break;
            }

            if (trimmed.Length == 0)
            {
                normalized.RemoveAt(normalized.Count - 1);
                continue;
            }

            normalized[^1] = last with { Text = trimmed };
            break;
        }

        return normalized.Count == 0
            ? null
            : new NovelContentBlock(kind, normalized, level);
    }

    /// <summary>
    /// Builds the reader block sequence for a chapter. Structured EPUB content
    /// renders its headings, emphasis, ruby and illustrations; plain sources
    /// render their paragraphs. Both yield the same paragraph indexes.
    /// </summary>
    public static IReadOnlyList<NovelReaderBlock> BuildReaderBlocks(
        string originalText,
        string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            return NovelTextLayout.SplitParagraphs(originalText)
                .Select((paragraph, index) => new NovelReaderBlock(
                    index,
                    0,
                    [new NovelInlineRun(paragraph)],
                    null,
                    null))
                .ToArray();
        }

        var blocks = new List<NovelReaderBlock>();
        var paragraphIndex = 0;
        foreach (var block in Deserialize(contentJson))
        {
            if (block.Kind == NovelContentBlock.ImageKind)
            {
                if (!string.IsNullOrWhiteSpace(block.Source))
                {
                    blocks.Add(new NovelReaderBlock(null, 0, [], block.Source, block.Alt));
                }

                continue;
            }

            if (!block.IsText || block.Runs is not { Count: > 0 })
            {
                continue;
            }

            blocks.Add(new NovelReaderBlock(
                paragraphIndex,
                block.Kind == NovelContentBlock.HeadingKind
                    ? Math.Clamp(block.Level, 1, 6)
                    : 0,
                block.Runs,
                null,
                null));
            paragraphIndex++;
        }

        return blocks;
    }

    /// <summary>
    /// Renders inline runs as HTML. Only encoded text, <c>em</c>,
    /// <c>strong</c> and <c>ruby</c> are emitted. Readings are carried in a
    /// data attribute and drawn by CSS, so the paragraph's DOM text equals the
    /// plain paragraph text that offsets refer to.
    /// </summary>
    public static IHtmlContent RenderRuns(IReadOnlyList<NovelInlineRun> runs)
    {
        var encoder = ReaderEncoder;
        var html = new StringBuilder();

        foreach (var run in runs)
        {
            if (run.Strong)
            {
                html.Append("<strong>");
            }

            if (run.Emphasis)
            {
                html.Append("<em>");
            }

            if (run.Ruby is { Length: > 0 } ruby)
            {
                html.Append("<ruby>")
                    .Append(encoder.Encode(run.Text))
                    .Append("<rt data-rt=\"")
                    .Append(encoder.Encode(ruby))
                    .Append("\"></rt></ruby>");
            }
            else
            {
                html.Append(encoder.Encode(run.Text));
            }

            if (run.Emphasis)
            {
                html.Append("</em>");
            }

            if (run.Strong)
            {
                html.Append("</strong>");
            }
        }

        return new HtmlString(html.ToString());
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
