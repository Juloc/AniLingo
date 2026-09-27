using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Jularr.Web.Features.Books;

/// <summary>One page of a PDF with the text Jularr could extract (empty for scanned pages).</summary>
public sealed record PdfPageText(int Number, string Text);

/// <summary>What Jularr reads from a PDF: its pages in order, Info metadata, language and a page-1 cover.</summary>
public sealed record PdfDocumentContent(
    IReadOnlyList<PdfPageText> Pages,
    string? Title,
    string? Author,
    string? Language,
    byte[]? CoverJpeg)
{
    public static readonly PdfDocumentContent Empty = new([], null, null, null, null);
}

/// <summary>
/// A small, tolerant PDF reader without a PDF library. It resolves objects (also inside
/// compressed object streams), walks the page tree, extracts the text of each page from its
/// content stream (with ToUnicode maps where the fonts have them) and picks up a full-page JPEG
/// on page 1 as cover. It never renders or changes the file; whatever it cannot read is
/// simply missing (a page without text, no cover).
/// </summary>
public static partial class PdfDocumentReader
{
    /// <summary>Larger files are stored and served, but not read (no text, page count or cover).</summary>
    public const long MaxReadBytes = 100L * 1024 * 1024;

    private const int MaxPages = 5000;

    public static bool HasPdfHeader(ReadOnlySpan<byte> head) =>
        head.IndexOf("%PDF-"u8) is >= 0 and < 1024;

    public static PdfDocumentContent Read(byte[] bytes)
    {
        try
        {
            return new Document(bytes).Read();
        }
        catch (Exception exception) when (exception is FormatException or InvalidDataException or ArgumentException
                                              or IndexOutOfRangeException or InvalidCastException or OverflowException
                                              or KeyNotFoundException or InvalidOperationException)
        {
            // A damaged or unusual file: it is still a valid book file, just without extracted content.
            return PdfDocumentContent.Empty;
        }
    }

    private sealed record Ref(int Number);

    private sealed record Name(string Value);

    private sealed record PdfString(string Latin1);

    private sealed record Keyword(string Value);

    private sealed record StreamObject(Dictionary<string, object?> Dictionary, int Start, int End);

    /// <summary>A font's code length, Unicode map and glyph widths (thousandths of an em; 0 = unknown).</summary>
    private sealed record Font(int CodeBytes, Dictionary<int, string> Unicode, Dictionary<int, double> Widths, double DefaultWidth)
    {
        public double Width(int code) => Widths.TryGetValue(code, out var width) ? width : DefaultWidth;
    }

    private sealed class Document
    {
        private readonly byte[] bytes;
        private readonly string text;
        private readonly Dictionary<int, int> starts = [];
        private readonly Dictionary<int, string> packed = [];
        private readonly Dictionary<int, object?> cache = [];
        private readonly Dictionary<int, Font> fonts = [];

        public Document(byte[] bytes)
        {
            this.bytes = bytes;
            // Latin-1 maps every byte to one char, so offsets and binary data survive.
            text = Encoding.Latin1.GetString(bytes);
            IndexObjects();
        }

        public PdfDocumentContent Read()
        {
            var info = Resolve(LastReference("Info")) as Dictionary<string, object?>;
            var catalog = Resolve(LastReference("Root")) as Dictionary<string, object?>
                ?? starts.Keys.Select(number => Resolve(new Ref(number))).OfType<Dictionary<string, object?>>()
                    .FirstOrDefault(dictionary => NameOf(dictionary, "Type") == "Catalog");

            var pages = new List<(Dictionary<string, object?> Page, Dictionary<string, object?>? Resources)>();
            if (catalog is not null && Resolve(Get(catalog, "Pages")) is Dictionary<string, object?> root)
            {
                CollectPages(root, null, pages, [], 0);
            }

            var texts = pages
                .Select((page, index) => new PdfPageText(index + 1, PageText(page.Page, page.Resources)))
                .ToArray();
            return new PdfDocumentContent(
                texts,
                info is null ? null : TextString(Resolve(Get(info, "Title"))),
                info is null ? null : TextString(Resolve(Get(info, "Author"))),
                catalog is null ? null : TextString(Resolve(Get(catalog, "Lang"))),
                pages.Count > 0 ? CoverImage(pages[0].Resources) : null);
        }

        private void IndexObjects()
        {
            var position = 0;
            while (ObjectHeader().Match(text, position) is { Success: true } header)
            {
                var number = int.Parse(header.Groups["n"].Value, CultureInfo.InvariantCulture);
                starts[number] = header.Index + header.Length;
                var end = text.IndexOf("endobj", header.Index + header.Length, StringComparison.Ordinal);
                position = end < 0 ? header.Index + header.Length : end + "endobj".Length;
            }

            // Objects packed in object streams fill the numbers no plain object defines.
            foreach (var number in starts.Keys.ToArray())
            {
                var start = starts[number];
                if (text.IndexOf("/ObjStm", start, Math.Min(400, text.Length - start), StringComparison.Ordinal) < 0
                    || Resolve(new Ref(number)) is not StreamObject stream
                    || NameOf(stream.Dictionary, "Type") != "ObjStm"
                    || Decode(stream) is not { } content)
                {
                    continue;
                }

                var first = (int)Number(Get(stream.Dictionary, "First"));
                var header = content[..Math.Min(first, content.Length)]
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => int.Parse(value, CultureInfo.InvariantCulture))
                    .ToArray();
                for (var index = 0; index + 1 < header.Length; index += 2)
                {
                    var from = first + header[index + 1];
                    var to = index + 3 < header.Length ? first + header[index + 3] : content.Length;
                    if (!starts.ContainsKey(header[index]) && from >= 0 && to <= content.Length && from < to)
                    {
                        packed[header[index]] = content[from..to];
                    }
                }
            }
        }

        private Ref? LastReference(string key)
        {
            var match = Regex.Matches(text, $@"/{key}\s+(\d+)\s+\d+\s+R", RegexOptions.CultureInvariant).LastOrDefault();
            return match is null ? null : new Ref(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        }

        private object? Resolve(object? value, int depth = 0)
        {
            if (value is not Ref reference || depth > 8)
            {
                return value;
            }

            if (cache.TryGetValue(reference.Number, out var cached))
            {
                return cached;
            }

            cache[reference.Number] = null;
            object? result = null;
            if (starts.TryGetValue(reference.Number, out var start))
            {
                var position = start;
                result = Parse(text, ref position);
                SkipWhitespace(text, ref position);
                if (result is Dictionary<string, object?> dictionary
                    && string.CompareOrdinal(text, position, "stream", 0, 6) == 0)
                {
                    result = Stream(dictionary, position + 6, depth);
                }
            }
            else if (packed.TryGetValue(reference.Number, out var body))
            {
                var position = 0;
                result = Parse(body, ref position);
            }

            result = Resolve(result, depth + 1);
            cache[reference.Number] = result;
            return result;
        }

        private StreamObject Stream(Dictionary<string, object?> dictionary, int position, int depth)
        {
            if (position < text.Length && text[position] == '\r')
            {
                position++;
            }

            if (position < text.Length && text[position] == '\n')
            {
                position++;
            }

            var declared = Resolve(Get(dictionary, "Length"), depth + 1) is double length ? (int)length : -1;
            if (declared >= 0 && position + declared <= text.Length
                && text.IndexOf("endstream", position + declared, Math.Min(32, text.Length - position - declared), StringComparison.Ordinal) >= 0)
            {
                return new StreamObject(dictionary, position, position + declared);
            }

            var end = text.IndexOf("endstream", position, StringComparison.Ordinal);
            end = end < 0 ? text.Length : end;
            while (end > position && text[end - 1] is '\r' or '\n')
            {
                end--;
            }

            return new StreamObject(dictionary, position, end);
        }

        /// <summary>The stream's data after its filters, or null for filters Jularr does not decode.</summary>
        private string? Decode(StreamObject stream)
        {
            var filters = Resolve(Get(stream.Dictionary, "Filter")) switch
            {
                Name name => [name.Value],
                List<object?> list => list.Select(item => (Resolve(item) as Name)?.Value ?? "").ToArray(),
                _ => Array.Empty<string>()
            };
            var data = bytes.AsSpan(stream.Start, stream.End - stream.Start).ToArray();
            foreach (var filter in filters)
            {
                if (filter is not ("FlateDecode" or "Fl"))
                {
                    return null;
                }

                data = Inflate(data);
            }

            return Encoding.Latin1.GetString(data);
        }

        private static byte[] Inflate(byte[] data)
        {
            try
            {
                return Inflate(new ZLibStream(new MemoryStream(data), CompressionMode.Decompress));
            }
            catch (InvalidDataException) when (data.Length > 2)
            {
                // Some writers omit or damage the zlib header; the raw deflate data usually still reads.
                return Inflate(new DeflateStream(new MemoryStream(data, 2, data.Length - 2), CompressionMode.Decompress));
            }
        }

        private static byte[] Inflate(Stream decompressor)
        {
            using (decompressor)
            using (var output = new MemoryStream())
            {
                var buffer = new byte[81920];
                int read;
                while ((read = decompressor.Read(buffer)) > 0 && output.Length < 64 * 1024 * 1024)
                {
                    output.Write(buffer, 0, read);
                }

                return output.ToArray();
            }
        }

        private void CollectPages(
            Dictionary<string, object?> node,
            Dictionary<string, object?>? inherited,
            List<(Dictionary<string, object?>, Dictionary<string, object?>?)> pages,
            HashSet<Dictionary<string, object?>> seen,
            int depth)
        {
            if (depth > 32 || pages.Count >= MaxPages || !seen.Add(node))
            {
                return;
            }

            var resources = Resolve(Get(node, "Resources")) as Dictionary<string, object?> ?? inherited;
            if (Resolve(Get(node, "Kids")) is List<object?> kids)
            {
                foreach (var kid in kids)
                {
                    if (Resolve(kid) is Dictionary<string, object?> child)
                    {
                        CollectPages(child, resources, pages, seen, depth + 1);
                    }
                }

                return;
            }

            pages.Add((node, resources));
        }

        private string PageText(Dictionary<string, object?> page, Dictionary<string, object?>? resources)
        {
            var parts = Resolve(Get(page, "Contents")) switch
            {
                StreamObject stream => [stream],
                List<object?> list => list.Select(item => Resolve(item)).OfType<StreamObject>().ToArray(),
                _ => Array.Empty<StreamObject>()
            };
            var content = string.Join("\n", parts.Select(Decode).OfType<string>());
            if (content.Length == 0)
            {
                return "";
            }

            var pageFonts = new Dictionary<string, Font>(StringComparer.Ordinal);
            if (resources is not null && Resolve(Get(resources, "Font")) is Dictionary<string, object?> fontResources)
            {
                foreach (var (key, value) in fontResources)
                {
                    pageFonts[key] = LoadFont(value);
                }
            }

            return new TextCollector(pageFonts).Collect(content);
        }

        private Font LoadFont(object? value)
        {
            if (value is Ref reference && fonts.TryGetValue(reference.Number, out var known))
            {
                return known;
            }

            var dictionary = Resolve(value) as Dictionary<string, object?> ?? [];
            var codeBytes = NameOf(dictionary, "Subtype") == "Type0" ? 2 : 1;
            var unicode = new Dictionary<int, string>();
            if (Resolve(Get(dictionary, "ToUnicode")) is StreamObject map && Decode(map) is { } cmap)
            {
                codeBytes = ReadCMap(cmap, unicode, codeBytes);
            }

            var widths = new Dictionary<int, double>();
            double defaultWidth = 0;
            if (Resolve(Get(dictionary, "Widths")) is List<object?> simple)
            {
                var firstChar = (int)Number(Resolve(Get(dictionary, "FirstChar")));
                for (var index = 0; index < simple.Count; index++)
                {
                    widths[firstChar + index] = Number(Resolve(simple[index]));
                }
            }
            else if (Resolve(Get(dictionary, "DescendantFonts")) is List<object?> { Count: > 0 } descendants
                     && Resolve(descendants[0]) is Dictionary<string, object?> cidFont)
            {
                defaultWidth = Get(cidFont, "DW") is double dw ? dw : 1000;
                if (Resolve(Get(cidFont, "W")) is List<object?> cidWidths)
                {
                    ReadCidWidths(cidWidths, widths);
                }
            }

            var font = new Font(codeBytes, unicode, widths, defaultWidth);
            if (value is Ref fontReference)
            {
                fonts[fontReference.Number] = font;
            }

            return font;
        }

        /// <summary>A CIDFont /W array: <c>c [w1 w2 …]</c> or <c>cFirst cLast w</c> entries.</summary>
        private void ReadCidWidths(List<object?> entries, Dictionary<int, double> widths)
        {
            for (var index = 0; index + 1 < entries.Count && widths.Count < 65536;)
            {
                var first = (int)Number(Resolve(entries[index]));
                if (Resolve(entries[index + 1]) is List<object?> list)
                {
                    for (var offset = 0; offset < list.Count; offset++)
                    {
                        widths[first + offset] = Number(Resolve(list[offset]));
                    }

                    index += 2;
                    continue;
                }

                if (index + 2 >= entries.Count)
                {
                    break;
                }

                var last = Math.Min((int)Number(Resolve(entries[index + 1])), first + 65535);
                var width = Number(Resolve(entries[index + 2]));
                for (var code = first; code <= last; code++)
                {
                    widths[code] = width;
                }

                index += 3;
            }
        }

        private static int ReadCMap(string cmap, Dictionary<int, string> unicode, int codeBytes)
        {
            if (CodeSpace().Match(cmap) is { Success: true } space)
            {
                codeBytes = Math.Max(1, space.Groups[1].Value.Length / 2);
            }

            foreach (Match block in CharBlock().Matches(cmap))
            {
                foreach (Match pair in CharPair().Matches(block.Groups[1].Value))
                {
                    unicode[Convert.ToInt32(pair.Groups[1].Value, 16)] = Utf16(pair.Groups[2].Value);
                }
            }

            foreach (Match block in RangeBlock().Matches(cmap))
            {
                foreach (Match range in RangeEntry().Matches(block.Groups[1].Value))
                {
                    var low = Convert.ToInt32(range.Groups["low"].Value, 16);
                    var high = Math.Min(Convert.ToInt32(range.Groups["high"].Value, 16), low + 65535);
                    if (range.Groups["array"].Success)
                    {
                        var targets = HexString().Matches(range.Groups["array"].Value);
                        for (var index = 0; index < targets.Count && low + index <= high; index++)
                        {
                            unicode[low + index] = Utf16(targets[index].Groups[1].Value);
                        }

                        continue;
                    }

                    var target = Utf16(range.Groups["target"].Value).ToCharArray();
                    for (var code = low; code <= high && target.Length > 0; code++)
                    {
                        unicode[code] = new string(target);
                        target[^1]++;
                    }
                }
            }

            return codeBytes;
        }

        private static string Utf16(string hex)
        {
            if (hex.Length % 4 != 0)
            {
                hex = hex.PadRight(hex.Length + (4 - hex.Length % 4), '0');
            }

            return Encoding.BigEndianUnicode.GetString(Convert.FromHexString(hex));
        }

        /// <summary>The largest JPEG image drawn on page 1, when it is big enough to be a cover.</summary>
        private byte[]? CoverImage(Dictionary<string, object?>? resources)
        {
            if (resources is null || Resolve(Get(resources, "XObject")) is not Dictionary<string, object?> objects)
            {
                return null;
            }

            var best = objects.Values
                .Select(value => Resolve(value))
                .OfType<StreamObject>()
                .Where(image => NameOf(image.Dictionary, "Subtype") == "Image"
                    && Resolve(Get(image.Dictionary, "Filter")) switch
                    {
                        Name name => name.Value == "DCTDecode",
                        List<object?> list => list.Count == 1 && (Resolve(list[0]) as Name)?.Value == "DCTDecode",
                        _ => false
                    })
                .Select(image => (Image: image, Area: Number(Resolve(Get(image.Dictionary, "Width"))) * Number(Resolve(Get(image.Dictionary, "Height")))))
                .Where(candidate => candidate.Area >= 200 * 300)
                .OrderByDescending(candidate => candidate.Area)
                .Select(candidate => candidate.Image)
                .FirstOrDefault();
            if (best is null || best.End - best.Start < 4 || bytes[best.Start] != 0xFF || bytes[best.Start + 1] != 0xD8)
            {
                return null;
            }

            return bytes.AsSpan(best.Start, best.End - best.Start).ToArray();
        }
    }

    /// <summary>Turns the text operators of one content stream into paragraphs.</summary>
    private sealed class TextCollector(Dictionary<string, Font> fonts)
    {
        // Average glyph width in em: close enough to tell a word gap from a split text run.
        private const double GlyphWidth = 0.5;

        private readonly List<(double Y, StringBuilder Text)> lines = [];
        private Font? font;
        private double fontSize = 1;
        private double leading;

        // The text line matrix origin in user space and its scale; end of the text shown on it.
        private double lineX;
        private double lineY;
        private double scale = 1;
        private double shownEnd = double.NaN;

        public string Collect(string content)
        {
            var operands = new List<object?>();
            var position = 0;
            while (true)
            {
                SkipWhitespace(content, ref position);
                if (position >= content.Length)
                {
                    break;
                }

                var value = Parse(content, ref position);
                if (value is not Keyword keyword)
                {
                    operands.Add(value);
                    continue;
                }

                if (keyword.Value == "BI")
                {
                    SkipInlineImage(content, ref position);
                }
                else
                {
                    Apply(keyword.Value, operands);
                }

                operands.Clear();
            }

            return Paragraphs();
        }

        private void Apply(string op, List<object?> operands)
        {
            double Operand(int index) => index < operands.Count && operands[index] is double value ? value : 0;

            switch (op)
            {
                case "BT":
                    // A text object starts with the identity text matrix.
                    (lineX, lineY, scale) = (0, 0, 1);
                    break;
                case "Tf" when operands.Count >= 2 && operands[0] is Name name:
                    font = fonts.GetValueOrDefault(name.Value);
                    fontSize = Math.Abs(Operand(1)) > 0 ? Math.Abs(Operand(1)) : 1;
                    break;
                case "TL":
                    leading = Operand(0);
                    break;
                case "Td":
                    MoveTo(lineX + Operand(0) * scale, lineY + Operand(1) * scale);
                    break;
                case "TD":
                    leading = -Operand(1);
                    MoveTo(lineX + Operand(0) * scale, lineY + Operand(1) * scale);
                    break;
                case "Tm" when operands.Count >= 6:
                    scale = Math.Abs(Operand(0)) > 0 ? Math.Abs(Operand(0)) : 1;
                    MoveTo(Operand(4), Operand(5));
                    break;
                case "T*":
                    MoveTo(lineX, lineY - leading * scale);
                    break;
                case "Tj" when operands.Count >= 1:
                    Show(operands[^1]);
                    break;
                case "'" or "\"" when operands.Count >= 1:
                    MoveTo(lineX, lineY - leading * scale);
                    Show(operands[^1]);
                    break;
                case "TJ" when operands.Count >= 1 && operands[^1] is List<object?> items:
                    foreach (var item in items)
                    {
                        if (item is double adjustment)
                        {
                            // Adjustments are thousandths of an em; a large one is a word gap.
                            shownEnd -= adjustment / 1000 * fontSize * scale;
                            if (adjustment < -250)
                            {
                                Space();
                            }
                        }
                        else
                        {
                            Show(item);
                        }
                    }

                    break;
            }
        }

        private double Em => fontSize * scale;

        /// <summary>
        /// Moves the text line to (<paramref name="x"/>, <paramref name="y"/>) in user space. On the
        /// same line a gap after the text shown so far separates words; a split run does not.
        /// </summary>
        private void MoveTo(double x, double y)
        {
            var sameLine = lines.Count > 0 && Math.Abs(y - lines[^1].Y) <= Math.Max(0.5, Em * 0.2);
            if (sameLine && (double.IsNaN(shownEnd) || x - shownEnd > Em * 0.2 || x < shownEnd - Em * 2))
            {
                Space();
            }
            else if (!sameLine)
            {
                lines.Add((y, new StringBuilder()));
            }

            (lineX, lineY) = (x, y);
            shownEnd = x;
        }

        private void Space()
        {
            if (lines.Count > 0 && lines[^1].Text is { Length: > 0 } line && line[^1] != ' ')
            {
                line.Append(' ');
            }
        }

        private void Show(object? value)
        {
            if (value is not PdfString text)
            {
                return;
            }

            if (lines.Count == 0)
            {
                lines.Add((lineY, new StringBuilder()));
            }

            var line = lines[^1].Text;
            var codeBytes = font?.CodeBytes ?? 1;
            var advance = 0d;
            for (var index = 0; index + codeBytes <= text.Latin1.Length; index += codeBytes)
            {
                var code = 0;
                for (var offset = 0; offset < codeBytes; offset++)
                {
                    code = (code << 8) | (text.Latin1[index + offset] & 0xFF);
                }

                var width = font?.Width(code) ?? 0;
                advance += width > 0 ? width / 1000 : GlyphWidth;

                if (font?.Unicode.TryGetValue(code, out var mapped) == true)
                {
                    line.Append(mapped);
                }
                else if (codeBytes == 1 && code >= 0x20)
                {
                    line.Append(WinAnsi((char)code));
                }
            }

            shownEnd = (double.IsNaN(shownEnd) ? lineX : shownEnd) + advance * Em;
        }

        private static char WinAnsi(char code) => code switch
        {
            '\u0091' or '\u0092' => '’',
            '\u0093' or '\u0094' => '"',
            '\u0096' => '–',
            '\u0097' => '—',
            '\u0085' => '…',
            _ => code
        };

        /// <summary>Lines closer together than a clear paragraph gap are one paragraph.</summary>
        private string Paragraphs()
        {
            var kept = lines
                .Select(line => (line.Y, Text: Regex.Replace(line.Text.ToString(), @"[\s\p{Cc}]+", " ").Trim()))
                .Where(line => line.Text.Length > 0)
                .ToArray();
            if (kept.Length == 0)
            {
                return "";
            }

            var gaps = kept.Zip(kept.Skip(1), (upper, lower) => upper.Y - lower.Y).Where(gap => gap > 0).Order().ToArray();
            var typical = gaps.Length > 0 ? gaps[(gaps.Length - 1) / 2] : 0;
            var result = new StringBuilder(kept[0].Text);
            for (var index = 1; index < kept.Length; index++)
            {
                var gap = kept[index - 1].Y - kept[index].Y;
                if (gap <= 0 || (typical > 0 && gap > typical * 1.6))
                {
                    result.Append("\n\n");
                }
                else if (result.Length > 1 && result[^1] == '-' && char.IsLetter(result[^2]))
                {
                    // A word hyphenated across the line break.
                    result.Length--;
                }
                else
                {
                    result.Append(' ');
                }

                result.Append(kept[index].Text);
            }

            return result.ToString();
        }

        private static void SkipInlineImage(string content, ref int position)
        {
            var data = content.IndexOf("ID", position, StringComparison.Ordinal);
            if (data < 0)
            {
                position = content.Length;
                return;
            }

            var end = InlineImageEnd().Match(content, data + 2);
            position = end.Success ? end.Index + end.Length : content.Length;
        }
    }

    private static object? Get(Dictionary<string, object?> dictionary, string key) =>
        dictionary.TryGetValue(key, out var value) ? value : null;

    private static string? NameOf(Dictionary<string, object?> dictionary, string key) =>
        (Get(dictionary, key) as Name)?.Value;

    private static double Number(object? value) => value is double number ? number : 0;

    /// <summary>A PDF text string: UTF-16 with a byte order mark, otherwise PDFDocEncoding (close to Latin-1).</summary>
    private static string? TextString(object? value)
    {
        if (value is not PdfString text)
        {
            return null;
        }

        var raw = Encoding.Latin1.GetBytes(text.Latin1);
        var decoded = raw is [0xFE, 0xFF, ..]
            ? Encoding.BigEndianUnicode.GetString(raw, 2, raw.Length - 2)
            : text.Latin1;
        decoded = Regex.Replace(decoded, @"\s+", " ").Trim().Trim('\0');
        return decoded.Length == 0 ? null : decoded;
    }

    private static void SkipWhitespace(string text, ref int position)
    {
        while (position < text.Length)
        {
            var character = text[position];
            if (character == '%')
            {
                while (position < text.Length && text[position] is not ('\r' or '\n'))
                {
                    position++;
                }
            }
            else if (character is ' ' or '\t' or '\r' or '\n' or '\f' or '\0')
            {
                position++;
            }
            else
            {
                return;
            }
        }
    }

    private static bool IsDelimiter(char character) =>
        character is ' ' or '\t' or '\r' or '\n' or '\f' or '\0' or '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '/' or '%';

    /// <summary>Parses one PDF value (or operator keyword) at <paramref name="position"/>.</summary>
    private static object? Parse(string text, ref int position, int depth = 0)
    {
        SkipWhitespace(text, ref position);
        if (position >= text.Length || depth > 64)
        {
            return null;
        }

        var character = text[position];
        switch (character)
        {
            case '/':
            {
                var start = ++position;
                while (position < text.Length && !IsDelimiter(text[position]))
                {
                    position++;
                }

                return new Name(Regex.Replace(text[start..position], "#([0-9A-Fa-f]{2})", match => ((char)Convert.ToByte(match.Groups[1].Value, 16)).ToString()));
            }
            case '<' when position + 1 < text.Length && text[position + 1] == '<':
            {
                position += 2;
                var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
                while (true)
                {
                    SkipWhitespace(text, ref position);
                    if (position >= text.Length)
                    {
                        return dictionary;
                    }

                    if (text[position] == '>')
                    {
                        position += 2;
                        return dictionary;
                    }

                    if (Parse(text, ref position, depth + 1) is not Name key)
                    {
                        position++;
                        continue;
                    }

                    dictionary[key.Value] = Parse(text, ref position, depth + 1);
                }
            }
            case '<':
            {
                var close = text.IndexOf('>', position);
                close = close < 0 ? text.Length : close;
                var digits = new string(text[(position + 1)..close].Where(Uri.IsHexDigit).ToArray());
                position = Math.Min(close + 1, text.Length);
                if (digits.Length % 2 == 1)
                {
                    digits += "0";
                }

                return new PdfString(Encoding.Latin1.GetString(Convert.FromHexString(digits)));
            }
            case '(':
                return new PdfString(Literal(text, ref position));
            case '[':
            {
                position++;
                var list = new List<object?>();
                while (true)
                {
                    SkipWhitespace(text, ref position);
                    if (position >= text.Length)
                    {
                        return list;
                    }

                    if (text[position] == ']')
                    {
                        position++;
                        return list;
                    }

                    var before = position;
                    list.Add(Parse(text, ref position, depth + 1));
                    if (position == before)
                    {
                        position++;
                    }
                }
            }
            case '>' or ']' or ')' or '{' or '}':
                position++;
                return null;
        }

        if (character is (>= '0' and <= '9') or '+' or '-' or '.')
        {
            var start = position++;
            while (position < text.Length && text[position] is (>= '0' and <= '9') or '.')
            {
                position++;
            }

            var number = double.TryParse(text[start..position], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
            // "12 0 R" is a reference.
            if (Reference().Match(text, start) is { Success: true } reference && reference.Index == start)
            {
                position = start + reference.Length;
                return new Ref(int.Parse(reference.Groups[1].Value, CultureInfo.InvariantCulture));
            }

            return number;
        }

        var word = position;
        while (position < text.Length && !IsDelimiter(text[position]))
        {
            position++;
        }

        if (position == word)
        {
            position++;
            return null;
        }

        return text[word..position] switch
        {
            "true" => true,
            "false" => false,
            "null" => null,
            var keyword => new Keyword(keyword)
        };
    }

    private static string Literal(string text, ref int position)
    {
        var result = new StringBuilder();
        var depth = 0;
        for (; position < text.Length; position++)
        {
            var character = text[position];
            if (character == '\\' && position + 1 < text.Length)
            {
                var next = text[++position];
                if (next is >= '0' and <= '7')
                {
                    var value = next - '0';
                    for (var digits = 1; digits < 3 && position + 1 < text.Length && text[position + 1] is >= '0' and <= '7'; digits++)
                    {
                        value = value * 8 + (text[++position] - '0');
                    }

                    result.Append((char)(value & 0xFF));
                }
                else if (next is '\r' or '\n')
                {
                    // A line continuation.
                    if (next == '\r' && position + 1 < text.Length && text[position + 1] == '\n')
                    {
                        position++;
                    }
                }
                else
                {
                    result.Append(next switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        'b' => '\b',
                        'f' => '\f',
                        _ => next
                    });
                }

                continue;
            }

            if (character == '(' && depth++ == 0)
            {
                continue;
            }

            if (character == ')' && --depth == 0)
            {
                position++;
                return result.ToString();
            }

            result.Append(character);
        }

        return result.ToString();
    }

    [GeneratedRegex(@"(?<![0-9])(?<n>\d+)\s+\d+\s+obj\b", RegexOptions.CultureInvariant)]
    private static partial Regex ObjectHeader();

    [GeneratedRegex(@"\G(\d+)\s+\d+\s+R(?![A-Za-z])", RegexOptions.CultureInvariant)]
    private static partial Regex Reference();

    [GeneratedRegex(@"begincodespacerange\s*<([0-9A-Fa-f]+)>", RegexOptions.CultureInvariant)]
    private static partial Regex CodeSpace();

    [GeneratedRegex(@"beginbfchar(.*?)endbfchar", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex CharBlock();

    [GeneratedRegex(@"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]*)>", RegexOptions.CultureInvariant)]
    private static partial Regex CharPair();

    [GeneratedRegex(@"beginbfrange(.*?)endbfrange", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex RangeBlock();

    [GeneratedRegex(@"<(?<low>[0-9A-Fa-f]+)>\s*<(?<high>[0-9A-Fa-f]+)>\s*(?:<(?<target>[0-9A-Fa-f]*)>|(?<array>\[[^\]]*\]))", RegexOptions.CultureInvariant)]
    private static partial Regex RangeEntry();

    [GeneratedRegex(@"<([0-9A-Fa-f]*)>", RegexOptions.CultureInvariant)]
    private static partial Regex HexString();

    [GeneratedRegex(@"\sEI(?=[\s]|$)", RegexOptions.CultureInvariant)]
    private static partial Regex InlineImageEnd();
}
