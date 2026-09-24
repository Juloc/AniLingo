using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AniLingo.Web.Features.Books;

public static partial class EpubBookParser
{
    private const long MaxArchiveBytes = 100L * 1024 * 1024;
    private const long MaxExpandedBytes = 250L * 1024 * 1024;
    private const long MaxEntryBytes = 20L * 1024 * 1024;
    private const int MaxEntries = 10_000;

    private static readonly HashSet<string> BlockElements = new(
        [
            "address", "article", "aside", "blockquote", "div", "figcaption",
            "figure", "footer", "h1", "h2", "h3", "h4", "h5", "h6",
            "header", "li", "main", "nav", "ol", "p", "pre", "section",
            "table", "td", "th", "tr", "ul"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static ParsedEpubBook Parse(
        Stream input,
        string fallbackTitle)
    {
        using var buffered = CopyBounded(input, MaxArchiveBytes);
        using var archive = new ZipArchive(
            buffered,
            ZipArchiveMode.Read,
            leaveOpen: false);

        ValidateArchive(archive);

        var container = FindEntry(archive, "META-INF/container.xml")
            ?? throw new InvalidOperationException(
                "EPUB is missing META-INF/container.xml.");

        var containerXml = LoadXml(container);
        var rootPath = containerXml
            .Descendants()
            .FirstOrDefault(x => x.Name.LocalName == "rootfile")
            ?.Attribute("full-path")
            ?.Value
            ?.Trim();

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new InvalidOperationException(
                "EPUB container does not declare a package document.");
        }

        var packageEntry = FindEntry(archive, rootPath)
            ?? throw new InvalidOperationException(
                "EPUB package document was not found.");

        var package = LoadXml(packageEntry);
        var packageDirectory = GetDirectory(rootPath);

        var metadata = package
            .Descendants()
            .FirstOrDefault(x => x.Name.LocalName == "metadata");

        var title = MetadataValue(metadata, "title")
            ?? Path.GetFileNameWithoutExtension(fallbackTitle)
            ?? "Untitled book";
        var author = MetadataValue(metadata, "creator");
        var description = MetadataValue(metadata, "description");
        var language = MetadataValue(metadata, "language");

        var subjects = metadata?
            .Descendants()
            .Where(x => x.Name.LocalName == "subject")
            .Select(x => NormalizeWhitespace(x.Value))
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToArray()
            ?? [];

        var manifest = package
            .Descendants()
            .Where(x => x.Name.LocalName == "item")
            .Select(x => new ManifestItem(
                Id: x.Attribute("id")?.Value?.Trim() ?? "",
                Href: x.Attribute("href")?.Value?.Trim() ?? "",
                MediaType: x.Attribute("media-type")?.Value?.Trim() ?? "",
                Properties: x.Attribute("properties")?.Value?.Trim() ?? ""))
            .Where(x => x.Id.Length > 0 && x.Href.Length > 0)
            .ToDictionary(x => x.Id, StringComparer.Ordinal);

        var spineIds = package
            .Descendants()
            .Where(x => x.Name.LocalName == "itemref")
            .Select(x => x.Attribute("idref")?.Value?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        if (spineIds.Length == 0)
        {
            throw new InvalidOperationException(
                "EPUB has no readable spine.");
        }

        var chapters = new List<ImportedBookChapter>();
        var number = 1;

        foreach (var id in spineIds)
        {
            if (id is null || !manifest.TryGetValue(id, out var item))
            {
                continue;
            }

            if (!item.MediaType.Equals(
                    "application/xhtml+xml",
                    StringComparison.OrdinalIgnoreCase)
                && !item.MediaType.Equals(
                    "text/html",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var entryPath = ResolveRelativePath(
                packageDirectory,
                item.Href);
            var entry = FindEntry(archive, entryPath);
            if (entry is null)
            {
                continue;
            }

            var chapter = ParseChapter(entry, number);
            if (chapter.Text.Length < 10)
            {
                continue;
            }

            chapters.Add(chapter);
            number++;
        }

        if (chapters.Count == 0)
        {
            throw new InvalidOperationException(
                "EPUB does not contain readable text chapters.");
        }

        var coverId = package
            .Descendants()
            .FirstOrDefault(x =>
                x.Name.LocalName == "meta"
                && string.Equals(
                    x.Attribute("name")?.Value,
                    "cover",
                    StringComparison.OrdinalIgnoreCase))
            ?.Attribute("content")
            ?.Value
            ?.Trim();

        var coverItem = manifest.Values.FirstOrDefault(x =>
                x.Properties.Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries)
                    .Contains(
                        "cover-image",
                        StringComparer.OrdinalIgnoreCase))
            ?? (coverId is not null
                && manifest.TryGetValue(coverId, out var legacyCover)
                    ? legacyCover
                    : null);

        byte[]? coverBytes = null;
        string? coverMediaType = null;
        if (coverItem is not null
            && coverItem.MediaType.StartsWith(
                "image/",
                StringComparison.OrdinalIgnoreCase))
        {
            var coverEntry = FindEntry(
                archive,
                ResolveRelativePath(
                    packageDirectory,
                    coverItem.Href));

            if (coverEntry is not null
                && coverEntry.Length is > 0 and <= 10 * 1024 * 1024)
            {
                using var coverStream = coverEntry.Open();
                using var coverMemory = new MemoryStream();
                coverStream.CopyTo(coverMemory);
                coverBytes = coverMemory.ToArray();
                coverMediaType = coverItem.MediaType;
            }
        }

        return new ParsedEpubBook(
            Clean(title, 500) ?? "Untitled book",
            Clean(author, 300),
            Clean(description, 4000),
            Clean(language, 16),
            subjects,
            chapters,
            coverBytes,
            coverMediaType);
    }

    private static ImportedBookChapter ParseChapter(
        ZipArchiveEntry entry,
        int number)
    {
        if (entry.Length > MaxEntryBytes)
        {
            throw new InvalidOperationException(
                $"EPUB chapter '{entry.FullName}' is unexpectedly large.");
        }

        string html;
        using (var stream = entry.Open())
        using (var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: false))
        {
            html = reader.ReadToEnd();
        }

        string text;
        string? heading = null;

        try
        {
            var document = XDocument.Parse(
                html,
                LoadOptions.PreserveWhitespace);

            var body = document
                .Descendants()
                .FirstOrDefault(x => x.Name.LocalName == "body")
                ?? document.Root;

            if (body is null)
            {
                text = "";
            }
            else
            {
                heading = body
                    .Descendants()
                    .FirstOrDefault(x =>
                        x.Name.LocalName is "h1" or "h2" or "h3")
                    ?.Value;

                var builder = new StringBuilder();
                foreach (var node in body.Nodes())
                {
                    AppendNode(node, builder);
                }

                text = NormalizeText(builder.ToString());
            }
        }
        catch
        {
            var withoutScripts = ScriptStyleRegex()
                .Replace(html, " ");
            var withBreaks = BlockTagRegex()
                .Replace(withoutScripts, "\n\n");
            var stripped = TagRegex()
                .Replace(withBreaks, " ");
            text = NormalizeText(WebUtility.HtmlDecode(stripped));
        }

        var title = Clean(heading, 500);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = $"Chapter {number}";
        }

        return new ImportedBookChapter(
            number,
            title,
            text);
    }

    private static void AppendNode(
        XNode node,
        StringBuilder builder)
    {
        if (node is XText text)
        {
            var value = InlineWhitespaceRegex()
                .Replace(text.Value, " ");
            if (value.Length > 0)
            {
                builder.Append(value);
            }

            return;
        }

        if (node is not XElement element)
        {
            return;
        }

        var localName = element.Name.LocalName;
        if (localName.Equals("br", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append('\n');
            return;
        }

        var block = BlockElements.Contains(localName);
        if (block)
        {
            EnsureParagraphBreak(builder);
        }

        foreach (var child in element.Nodes())
        {
            AppendNode(child, builder);
        }

        if (block)
        {
            EnsureParagraphBreak(builder);
        }
    }

    private static void EnsureParagraphBreak(StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            return;
        }

        while (builder.Length > 0
            && builder[^1] is ' ' or '\t')
        {
            builder.Length--;
        }

        if (builder.Length == 0)
        {
            return;
        }

        if (builder[^1] != '\n')
        {
            builder.AppendLine();
        }

        if (builder.Length < 2 || builder[^2] != '\n')
        {
            builder.AppendLine();
        }
    }

    private static string NormalizeText(string text)
    {
        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        normalized = SpacesAroundNewlineRegex()
            .Replace(normalized, "\n");
        normalized = ExcessNewlinesRegex()
            .Replace(normalized, "\n\n");

        var paragraphs = normalized
            .Split(
                "\n\n",
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .Select(x => InlineWhitespaceRegex().Replace(x, " ").Trim())
            .Where(x => x.Length > 0);

        return string.Join("\n\n", paragraphs);
    }

    private static void ValidateArchive(ZipArchive archive)
    {
        if (archive.Entries.Count == 0
            || archive.Entries.Count > MaxEntries)
        {
            throw new InvalidOperationException(
                "EPUB contains an invalid number of files.");
        }

        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > MaxEntryBytes)
            {
                throw new InvalidOperationException(
                    $"EPUB entry '{entry.FullName}' is too large.");
            }

            expanded += entry.Length;
            if (expanded > MaxExpandedBytes)
            {
                throw new InvalidOperationException(
                    "EPUB expands beyond the supported size limit.");
            }

            if (entry.FullName.Contains(
                    "..",
                    StringComparison.Ordinal)
                || entry.FullName.StartsWith(
                    "/",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "EPUB contains an unsafe path.");
            }
        }
    }

    private static MemoryStream CopyBounded(
        Stream input,
        long maxBytes)
    {
        var result = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;

        while (true)
        {
            var read = input.Read(
                buffer,
                0,
                buffer.Length);
            if (read <= 0)
            {
                break;
            }

            total += read;
            if (total > maxBytes)
            {
                result.Dispose();
                throw new InvalidOperationException(
                    "EPUB exceeds the 100 MB import limit.");
            }

            result.Write(buffer, 0, read);
        }

        result.Position = 0;
        return result;
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxEntryBytes)
        {
            throw new InvalidOperationException(
                $"EPUB XML entry '{entry.FullName}' is too large.");
        }

        using var stream = entry.Open();
        return XDocument.Load(
            stream,
            LoadOptions.PreserveWhitespace);
    }

    private static ZipArchiveEntry? FindEntry(
        ZipArchive archive,
        string path)
    {
        var normalized = NormalizeZipPath(path);
        return archive.Entries.FirstOrDefault(
            x => NormalizeZipPath(x.FullName)
                .Equals(
                    normalized,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveRelativePath(
        string directory,
        string href)
    {
        var withoutFragment = href.Split('#', 2)[0];
        var decoded = Uri.UnescapeDataString(withoutFragment);
        var combined = string.IsNullOrWhiteSpace(directory)
            ? decoded
            : directory.TrimEnd('/') + "/" + decoded;

        var segments = new List<string>();
        foreach (var segment in combined.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    throw new InvalidOperationException(
                        "EPUB contains an unsafe relative path.");
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join("/", segments);
    }

    private static string NormalizeZipPath(string value) =>
        value.Replace('\\', '/').TrimStart('/');

    private static string GetDirectory(string path)
    {
        var normalized = NormalizeZipPath(path);
        var slash = normalized.LastIndexOf('/');
        return slash < 0 ? "" : normalized[..slash];
    }

    private static string? MetadataValue(
        XElement? metadata,
        string localName)
    {
        var value = metadata?
            .Descendants()
            .FirstOrDefault(x =>
                x.Name.LocalName.Equals(
                    localName,
                    StringComparison.OrdinalIgnoreCase))
            ?.Value;

        return Clean(value, 4000);
    }

    private static string NormalizeWhitespace(string value) =>
        InlineWhitespaceRegex()
            .Replace(WebUtility.HtmlDecode(value), " ")
            .Trim();

    private static string? Clean(
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var clean = NormalizeWhitespace(value);
        return clean.Length <= maxLength
            ? clean
            : clean[..maxLength].TrimEnd();
    }

    private sealed record ManifestItem(
        string Id,
        string Href,
        string MediaType,
        string Properties);

    [GeneratedRegex(@"\s+")]
    private static partial Regex InlineWhitespaceRegex();

    [GeneratedRegex(@"[ \t]*\n[ \t]*")]
    private static partial Regex SpacesAroundNewlineRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcessNewlinesRegex();

    [GeneratedRegex(@"(?is)<(script|style)\b.*?</\1>")]
    private static partial Regex ScriptStyleRegex();

    [GeneratedRegex(@"(?is)</?(p|div|section|article|h[1-6]|li|blockquote|br)\b[^>]*>")]
    private static partial Regex BlockTagRegex();

    [GeneratedRegex(@"(?is)<[^>]+>")]
    private static partial Regex TagRegex();
}
