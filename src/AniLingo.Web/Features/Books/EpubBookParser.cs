using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using AniLingo.Web.Features.Novels;

namespace AniLingo.Web.Features.Books;

/// <summary>
/// The one EPUB parser. It reads package metadata, spine order, chapters,
/// cover and (on request) embedded images. Chapter XHTML is converted into
/// plain paragraphs plus a sanitized block structure (headings, emphasis,
/// ruby, illustrations); scripts, styles, event handlers, links and external
/// resources never survive. DRM-protected EPUBs are rejected.
/// </summary>
public static partial class EpubBookParser
{
    private const long MaxArchiveBytes = 100L * 1024 * 1024;
    private const long MaxExpandedBytes = 250L * 1024 * 1024;
    private const long MaxEntryBytes = 20L * 1024 * 1024;
    private const long MaxImageBytes = 10L * 1024 * 1024;
    private const int MaxEntries = 10_000;

    private static readonly HashSet<string> BlockElements = new(
        [
            "address", "article", "aside", "blockquote", "div", "figcaption",
            "figure", "footer", "h1", "h2", "h3", "h4", "h5", "h6",
            "header", "li", "main", "nav", "ol", "p", "pre", "section",
            "table", "td", "th", "tr", "ul"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> SkippedElements = new(
        [
            "script", "style", "head", "title", "noscript", "template",
            "iframe", "object", "embed", "audio", "video", "canvas", "form",
            "input", "button", "select", "textarea", "math", "rp", "rt", "rtc"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> EmphasisElements = new(
        ["em", "i", "cite", "dfn", "var"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> StrongElements = new(
        ["strong", "b"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> RasterImageTypes = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/jpg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp"
    };

    private static readonly HashSet<string> FontObfuscationAlgorithms = new(
        [
            "http://www.idpf.org/2008/embedding",
            "http://ns.adobe.com/pdf/enc#RC"
        ],
        StringComparer.Ordinal);

    public static ParsedEpubBook Parse(
        Stream input,
        string fallbackTitle,
        bool includeAssets = false)
    {
        using var buffered = CopyBounded(input, MaxArchiveBytes);
        using var archive = OpenArchive(buffered);

        ValidateArchive(archive);
        RejectDrm(archive);

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
        var publisher = MetadataValue(metadata, "publisher");
        var publishedDate = MetadataValue(metadata, "date");
        var (isbn10, isbn13) = ExtractIsbn(metadata);
        var uniqueIdentifier = ExtractUniqueIdentifier(package.Root, metadata);
        var (seriesTitle, seriesIndex) = ExtractSeries(metadata);

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
            .GroupBy(x => x.Id, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        var mediaTypesByPath = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in manifest.Values)
        {
            if (TryResolveRelativePath(packageDirectory, item.Href, out var itemPath))
            {
                mediaTypesByPath.TryAdd(itemPath, item.MediaType);
            }
        }

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
        var pendingImages = new List<NovelContentBlock>();
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

            var chapter = ParseChapter(
                entry,
                NormalizeZipPath(entryPath),
                number,
                archive,
                mediaTypesByPath);

            if (chapter.Text.Length < 10)
            {
                // Illustration-only pages (colour inserts, frontispieces) have
                // no readable text; their images open the next text chapter.
                pendingImages.AddRange(chapter.Blocks.Where(
                    block => block.Kind == NovelContentBlock.ImageKind));
                continue;
            }

            if (pendingImages.Count > 0)
            {
                chapter = chapter with
                {
                    Blocks = [.. pendingImages, .. chapter.Blocks]
                };
                pendingImages.Clear();
            }

            chapters.Add(chapter);
            number++;
        }

        if (chapters.Count == 0)
        {
            throw new InvalidOperationException(
                "EPUB does not contain readable text chapters.");
        }

        if (pendingImages.Count > 0)
        {
            chapters[^1] = chapters[^1] with
            {
                Blocks = [.. chapters[^1].Blocks, .. pendingImages]
            };
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
                StringComparison.OrdinalIgnoreCase)
            && TryResolveRelativePath(packageDirectory, coverItem.Href, out var coverPath))
        {
            var coverEntry = FindEntry(archive, coverPath);

            if (coverEntry is not null
                && coverEntry.Length is > 0 and <= MaxImageBytes)
            {
                coverBytes = ReadEntryBytes(coverEntry);
                coverMediaType = coverItem.MediaType;
            }
        }

        var assets = includeAssets
            ? ReadReferencedImages(archive, chapters)
            : [];

        return new ParsedEpubBook(
            Clean(title, 500) ?? "Untitled book",
            Clean(author, 300),
            Clean(description, 4000),
            Clean(language, 16),
            isbn10,
            isbn13,
            Clean(publisher, 300),
            Clean(publishedDate, 80),
            subjects,
            chapters,
            coverBytes,
            coverMediaType)
        {
            UniqueIdentifier = Clean(uniqueIdentifier, 300),
            SeriesTitle = Clean(seriesTitle, 500),
            SeriesIndex = seriesIndex,
            Assets = assets
        };
    }

    private static ZipArchive OpenArchive(MemoryStream buffered)
    {
        try
        {
            return new ZipArchive(
                buffered,
                ZipArchiveMode.Read,
                leaveOpen: false);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidOperationException(
                $"File is not a valid EPUB (ZIP) archive: {exception.Message}",
                exception);
        }
    }

    /// <summary>
    /// Rejects encrypted EPUBs. Only the standard font obfuscation algorithms
    /// are accepted; AniLingo never removes DRM.
    /// </summary>
    private static void RejectDrm(ZipArchive archive)
    {
        var encryption = FindEntry(archive, "META-INF/encryption.xml");
        if (encryption is null)
        {
            return;
        }

        var document = LoadXml(encryption);
        var protectedContent = document
            .Descendants()
            .Where(x => x.Name.LocalName == "EncryptionMethod")
            .Select(x => x.Attribute("Algorithm")?.Value?.Trim() ?? "")
            .Any(algorithm => !FontObfuscationAlgorithms.Contains(algorithm));

        if (protectedContent)
        {
            throw new InvalidOperationException(
                "EPUB is DRM-protected (encrypted content). AniLingo imports only DRM-free EPUBs and does not remove DRM.");
        }
    }

    private static ImportedBookChapter ParseChapter(
        ZipArchiveEntry entry,
        string entryPath,
        int number,
        ZipArchive archive,
        IReadOnlyDictionary<string, string> mediaTypesByPath)
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

        IReadOnlyList<NovelContentBlock> blocks;

        try
        {
            var document = ParseXhtml(html);

            var body = document
                .Descendants()
                .FirstOrDefault(x => x.Name.LocalName == "body")
                ?? document.Root;

            var builder = new ChapterBlockBuilder(
                GetDirectory(entryPath),
                archive,
                mediaTypesByPath);
            if (body is not null)
            {
                builder.Walk(body);
            }

            blocks = builder.Finish();
        }
        catch (XmlException)
        {
            // Not well-formed XHTML: keep the readable text as plain
            // paragraphs; no markup of a malformed document is trusted.
            var withoutScripts = ScriptStyleRegex()
                .Replace(html, " ");
            var withBreaks = BlockTagRegex()
                .Replace(withoutScripts, "\n\n");
            var stripped = TagRegex()
                .Replace(withBreaks, " ");

            blocks = NormalizeText(WebUtility.HtmlDecode(stripped))
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                .Select(paragraph => NovelChapterDocument.NormalizeTextBlock(
                    NovelContentBlock.ParagraphKind,
                    0,
                    [new NovelInlineRun(paragraph)]))
                .Where(block => block is not null)
                .Cast<NovelContentBlock>()
                .ToArray();
        }

        var text = NovelChapterDocument.ToPlainText(blocks);
        var heading = blocks.FirstOrDefault(
            block => block.Kind == NovelContentBlock.HeadingKind && block.Level <= 3);

        var title = Clean(heading?.PlainText, 500);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = $"Chapter {number}";
        }

        return new ImportedBookChapter(
            number,
            title,
            text)
        {
            SourcePath = entryPath,
            Blocks = blocks
        };
    }

    /// <summary>
    /// Parses chapter XHTML without DTD processing. HTML named entities that
    /// XML does not define are converted to numeric references first.
    /// </summary>
    private static XDocument ParseXhtml(string html)
    {
        var withoutDoctype = DoctypeRegex().Replace(html, "");
        var withEntities = NamedEntityRegex().Replace(
            withoutDoctype,
            match =>
            {
                var name = match.Groups[1].Value;
                if (name is "amp" or "lt" or "gt" or "quot" or "apos")
                {
                    return match.Value;
                }

                var decoded = WebUtility.HtmlDecode(match.Value);
                return decoded == match.Value
                    ? "&amp;" + name + ";"
                    : string.Concat(decoded.EnumerateRunes().Select(
                        rune => "&#" + rune.Value.ToString(CultureInfo.InvariantCulture) + ";"));
            });

        using var reader = XmlReader.Create(
            new StringReader(withEntities),
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true
            });

        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static IReadOnlyList<ParsedEpubAsset> ReadReferencedImages(
        ZipArchive archive,
        IReadOnlyList<ImportedBookChapter> chapters)
    {
        var assets = new List<ParsedEpubAsset>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var block in chapters.SelectMany(chapter => chapter.Blocks))
        {
            if (block.Kind != NovelContentBlock.ImageKind ||
                block.Source is null ||
                !seen.Add(block.Source))
            {
                continue;
            }

            var entry = FindEntry(archive, block.Source);
            if (entry is null || entry.Length is <= 0 or > MaxImageBytes)
            {
                continue;
            }

            var mediaType = MediaTypeFromExtension(block.Source);
            if (mediaType is null)
            {
                continue;
            }

            assets.Add(new ParsedEpubAsset(
                block.Source,
                mediaType,
                ReadEntryBytes(entry)));
        }

        return assets;
    }

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string? MediaTypeFromExtension(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => null
        };

    /// <summary>
    /// Whitelist conversion of chapter XHTML into content blocks. Only text,
    /// block boundaries, headings, emphasis, ruby and internal raster images
    /// are kept; everything else is unwrapped or dropped.
    /// </summary>
    private sealed class ChapterBlockBuilder(
        string chapterDirectory,
        ZipArchive archive,
        IReadOnlyDictionary<string, string> mediaTypesByPath)
    {
        private readonly List<NovelContentBlock> blocks = [];
        private readonly List<NovelInlineRun> runs = [];
        private readonly List<NovelContentBlock> deferredImages = [];
        private string kind = NovelContentBlock.ParagraphKind;
        private int level;
        private int pendingBreaks;

        public void Walk(XElement element) =>
            WalkChildren(element, emphasis: false, strong: false);

        public IReadOnlyList<NovelContentBlock> Finish()
        {
            EndBlock();
            return blocks;
        }

        private void WalkChildren(XElement element, bool emphasis, bool strong)
        {
            foreach (var node in element.Nodes())
            {
                WalkNode(node, emphasis, strong);
            }
        }

        private void WalkNode(XNode node, bool emphasis, bool strong)
        {
            if (node is XText text)
            {
                AppendText(text.Value, emphasis, strong);
                return;
            }

            if (node is not XElement element)
            {
                return;
            }

            var name = element.Name.LocalName;
            if (SkippedElements.Contains(name))
            {
                return;
            }

            if (name.Equals("br", StringComparison.OrdinalIgnoreCase))
            {
                pendingBreaks++;
                return;
            }

            if (name.Equals("img", StringComparison.OrdinalIgnoreCase))
            {
                AddImage(
                    element.Attribute("src")?.Value,
                    element.Attribute("alt")?.Value);
                return;
            }

            if (name.Equals("svg", StringComparison.OrdinalIgnoreCase))
            {
                // Only the raster image an SVG wrapper points at is kept.
                var image = element
                    .Descendants()
                    .FirstOrDefault(x => x.Name.LocalName == "image");
                AddImage(
                    image?.Attributes().FirstOrDefault(
                        x => x.Name.LocalName == "href")?.Value,
                    null);
                return;
            }

            if (name.Equals("ruby", StringComparison.OrdinalIgnoreCase))
            {
                AppendRuby(element, emphasis, strong);
                return;
            }

            var childEmphasis = emphasis
                || EmphasisElements.Contains(name)
                || HasEmphasisClass(element);
            var childStrong = strong || StrongElements.Contains(name);

            if (!BlockElements.Contains(name))
            {
                WalkChildren(element, childEmphasis, childStrong);
                return;
            }

            var headingLevel = name.Length == 2
                && name[0] is 'h' or 'H'
                && name[1] is >= '1' and <= '6'
                    ? name[1] - '0'
                    : 0;

            EndBlock();
            if (headingLevel > 0)
            {
                kind = NovelContentBlock.HeadingKind;
                level = headingLevel;
            }

            WalkChildren(element, childEmphasis, childStrong);
            EndBlock();
        }

        private void AppendText(string value, bool emphasis, bool strong)
        {
            if (value.Length == 0)
            {
                return;
            }

            if (value.Trim().Length > 0)
            {
                FlushBreaks();
            }

            runs.Add(new NovelInlineRun(value, null, emphasis, strong));
        }

        private void AppendRuby(XElement ruby, bool emphasis, bool strong)
        {
            var baseText = new StringBuilder();

            foreach (var node in ruby.Nodes())
            {
                if (node is XText text)
                {
                    baseText.Append(text.Value);
                    continue;
                }

                if (node is not XElement child)
                {
                    continue;
                }

                var name = child.Name.LocalName;
                if (name.Equals("rp", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (name.Equals("rt", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("rtc", StringComparison.OrdinalIgnoreCase))
                {
                    var reading = string.Concat(child
                        .DescendantNodes()
                        .OfType<XText>()
                        .Where(x => x.Parent?.Name.LocalName is not "rp")
                        .Select(x => x.Value));

                    if (baseText.Length > 0)
                    {
                        FlushBreaks();
                        runs.Add(new NovelInlineRun(
                            baseText.ToString(),
                            reading,
                            emphasis,
                            strong));
                        baseText.Clear();
                    }

                    continue;
                }

                // rb and any other inline wrapper contribute base text only.
                baseText.Append(string.Concat(child
                    .DescendantNodes()
                    .OfType<XText>()
                    .Where(x => x.Parent?.Name.LocalName is not ("rt" or "rp" or "rtc"))
                    .Select(x => x.Value)));
            }

            if (baseText.Length > 0)
            {
                AppendText(baseText.ToString(), emphasis, strong);
            }
        }

        private void AddImage(string? source, string? alt)
        {
            var path = ResolveImagePath(source);
            if (path is null)
            {
                return;
            }

            var image = new NovelContentBlock(
                NovelContentBlock.ImageKind,
                Source: path,
                Alt: Clean(alt, 300));

            if (runs.Any(run => run.Text.Trim().Length > 0))
            {
                // An inline image never splits a paragraph; it follows it.
                deferredImages.Add(image);
                return;
            }

            EndBlock();
            blocks.Add(image);
        }

        private string? ResolveImagePath(string? source)
        {
            var value = source?.Trim();
            if (string.IsNullOrEmpty(value) ||
                value.StartsWith("//", StringComparison.Ordinal) ||
                value.StartsWith('/') ||
                SchemeRegex().IsMatch(value))
            {
                // External, absolute and data: resources are never loaded.
                return null;
            }

            if (!TryResolveRelativePath(chapterDirectory, value, out var path) ||
                FindEntry(archive, path) is null)
            {
                return null;
            }

            var mediaType = mediaTypesByPath.TryGetValue(path, out var declared)
                ? declared
                : MediaTypeFromExtension(path);

            return mediaType is not null &&
                RasterImageTypes.ContainsKey(mediaType) &&
                MediaTypeFromExtension(path) is not null
                    ? path
                    : null;
        }

        private void FlushBreaks()
        {
            if (pendingBreaks >= 2)
            {
                // Consecutive line breaks separate paragraphs.
                var currentKind = kind;
                var currentLevel = level;
                EndBlock();
                kind = currentKind;
                level = currentLevel;
            }
            else if (pendingBreaks == 1)
            {
                runs.Add(new NovelInlineRun(" "));
            }

            pendingBreaks = 0;
        }

        private void EndBlock()
        {
            pendingBreaks = 0;

            if (runs.Count > 0)
            {
                var block = NovelChapterDocument.NormalizeTextBlock(
                    kind,
                    kind == NovelContentBlock.HeadingKind ? level : 0,
                    runs);
                if (block is not null)
                {
                    blocks.Add(block);
                }

                runs.Clear();
            }

            blocks.AddRange(deferredImages);
            deferredImages.Clear();
            kind = NovelContentBlock.ParagraphKind;
            level = 0;
        }

        private static bool HasEmphasisClass(XElement element)
        {
            var classes = element.Attribute("class")?.Value;
            if (string.IsNullOrWhiteSpace(classes))
            {
                return false;
            }

            return classes
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(name =>
                    name.Contains("sesame", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("boten", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("em-", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("emphasis", StringComparison.OrdinalIgnoreCase));
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

    private static string? ExtractUniqueIdentifier(
        XElement? package,
        XElement? metadata)
    {
        var uniqueId = package?.Attribute("unique-identifier")?.Value?.Trim();
        var identifiers = metadata?
            .Elements()
            .Where(x => x.Name.LocalName == "identifier")
            .ToArray()
            ?? [];

        var identifier = identifiers.FirstOrDefault(x =>
                !string.IsNullOrEmpty(uniqueId) &&
                string.Equals(x.Attribute("id")?.Value?.Trim(), uniqueId, StringComparison.Ordinal))
            ?? identifiers.FirstOrDefault();

        return identifier is null
            ? null
            : NormalizeWhitespace(identifier.Value);
    }

    private static (string? Title, decimal? Index) ExtractSeries(XElement? metadata)
    {
        if (metadata is null)
        {
            return (null, null);
        }

        var metas = metadata
            .Elements()
            .Where(x => x.Name.LocalName == "meta")
            .ToArray();

        string? CalibreValue(string name) => metas
            .FirstOrDefault(x => string.Equals(
                x.Attribute("name")?.Value?.Trim(),
                name,
                StringComparison.OrdinalIgnoreCase))
            ?.Attribute("content")
            ?.Value;

        var title = CalibreValue("calibre:series");
        var index = ParseSeriesIndex(CalibreValue("calibre:series_index"));

        var collection = metas.FirstOrDefault(x =>
            x.Attribute("property")?.Value?.Trim() == "belongs-to-collection" &&
            IsSeriesCollection(metas, x.Attribute("id")?.Value?.Trim()));

        if (collection is not null)
        {
            title ??= collection.Value;
            var id = collection.Attribute("id")?.Value?.Trim();
            if (index is null && !string.IsNullOrEmpty(id))
            {
                index = ParseSeriesIndex(metas.FirstOrDefault(x =>
                        x.Attribute("refines")?.Value?.Trim() == "#" + id &&
                        x.Attribute("property")?.Value?.Trim() == "group-position")
                    ?.Value);
            }
        }

        return (
            string.IsNullOrWhiteSpace(title) ? null : NormalizeWhitespace(title),
            index);
    }

    private static bool IsSeriesCollection(
        IReadOnlyList<XElement> metas,
        string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return true;
        }

        var type = metas.FirstOrDefault(x =>
                x.Attribute("refines")?.Value?.Trim() == "#" + id &&
                x.Attribute("property")?.Value?.Trim() == "collection-type")
            ?.Value
            ?.Trim();

        // Untyped collections are treated as series; typed ones must say so.
        return type is null ||
            type.Equals("series", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal? ParseSeriesIndex(string? value) =>
        decimal.TryParse(
            value?.Trim(),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var index) && index is > 0 and < 100_000
            ? index
            : null;
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

        try
        {
            using var stream = entry.Open();
            using var reader = XmlReader.Create(
                stream,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Ignore,
                    XmlResolver = null
                });
            return XDocument.Load(
                reader,
                LoadOptions.PreserveWhitespace);
        }
        catch (XmlException exception)
        {
            throw new InvalidOperationException(
                $"EPUB file '{entry.FullName}' is not well-formed XML: {exception.Message}",
                exception);
        }
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

    private static bool TryResolveRelativePath(
        string directory,
        string href,
        out string path)
    {
        try
        {
            path = ResolveRelativePath(directory, href);
            return path.Length > 0;
        }
        catch (InvalidOperationException)
        {
            path = "";
            return false;
        }
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

    private static (string? Isbn10, string? Isbn13) ExtractIsbn(
        XElement? metadata)
    {
        var identifiers = metadata?
            .Descendants()
            .Where(x => x.Name.LocalName.Equals(
                "identifier",
                StringComparison.OrdinalIgnoreCase))
            .Select(x => NormalizeIsbnCandidate(x.Value))
            .Where(x => x is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];

        string? isbn10 = null;
        string? isbn13 = null;

        foreach (var value in identifiers)
        {
            if (value.Length == 10
                && IsValidIsbn10(value))
            {
                isbn10 ??= value;
            }
            else if (value.Length == 13
                && IsValidIsbn13(value))
            {
                isbn13 ??= value;
            }
        }

        return (isbn10, isbn13);
    }

    private static string? NormalizeIsbnCandidate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = Regex.Replace(
                value.ToUpperInvariant(),
                @"[^0-9X]",
                "");

        if (candidate.Length is 10 or 13)
        {
            return candidate;
        }

        var match = Regex.Match(
            value.ToUpperInvariant(),
            @"(?:97[89][\s-]*)?(?:\d[\s-]*){8,11}[\dX]");

        if (!match.Success)
        {
            return null;
        }

        candidate = Regex.Replace(
            match.Value,
            @"[^0-9X]",
            "");

        return candidate.Length is 10 or 13
            ? candidate
            : null;
    }

    private static bool IsValidIsbn10(string value)
    {
        if (value.Length != 10)
        {
            return false;
        }

        var sum = 0;
        for (var index = 0; index < 10; index++)
        {
            var digit = index == 9
                && value[index] == 'X'
                    ? 10
                    : value[index] - '0';

            if (digit is < 0 or > 10)
            {
                return false;
            }

            sum += (10 - index) * digit;
        }

        return sum % 11 == 0;
    }

    private static bool IsValidIsbn13(string value)
    {
        if (value.Length != 13
            || value.Any(x => x is < '0' or > '9'))
        {
            return false;
        }

        var sum = 0;
        for (var index = 0; index < 12; index++)
        {
            var digit = value[index] - '0';
            sum += index % 2 == 0
                ? digit
                : digit * 3;
        }

        var check = (10 - (sum % 10)) % 10;
        return check == value[12] - '0';
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

    [GeneratedRegex(@"(?is)<!DOCTYPE[^>\[]*(\[.*?\])?\s*>")]
    private static partial Regex DoctypeRegex();

    [GeneratedRegex(@"&([A-Za-z][A-Za-z0-9]{1,31});")]
    private static partial Regex NamedEntityRegex();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*:")]
    private static partial Regex SchemeRegex();
}
