using System.IO.Compression;
using System.Security;
using System.Text;

namespace AniLingo.Tests;

/// <summary>Builds small EPUB files for parser and import tests.</summary>
internal sealed class EpubTestBuilder
{
    // 1×1 transparent PNG.
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    // Fixed entry timestamps keep identical builds byte-identical.
    private static readonly DateTimeOffset FixedTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly List<(string Path, string Xhtml)> chapters = [];
    private readonly List<(string Path, byte[] Bytes, string MediaType)> images = [];

    public string Title { get; set; } = "Test Volume";
    public string? Author { get; set; } = "Test Author";
    public string Language { get; set; } = "ja";
    public string? Identifier { get; set; } = "urn:uuid:test-volume";
    public string? CalibreSeries { get; set; }
    public string? CalibreSeriesIndex { get; set; }
    public string? CollectionSeries { get; set; }
    public string? CollectionPosition { get; set; }
    public string? CoverPath { get; set; }
    public string? EncryptionAlgorithm { get; set; }

    public EpubTestBuilder Chapter(string path, string title, params string[] paragraphs) =>
        RawChapter(
            path,
            $"<h1>{SecurityElement.Escape(title)}</h1>" + string.Concat(
                paragraphs.Select(x => $"<p>{SecurityElement.Escape(x)}</p>")));

    public EpubTestBuilder RawChapter(string path, string body, string? head = null)
    {
        chapters.Add((path, head ?? $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops"><head><title>t</title></head><body>{{body}}</body></html>
            """));
        return this;
    }

    public EpubTestBuilder RawDocument(string path, string document)
    {
        chapters.Add((path, document));
        return this;
    }

    public EpubTestBuilder Image(string path, byte[]? bytes = null, string mediaType = "image/png")
    {
        images.Add((path, bytes ?? Png, mediaType));
        return this;
    }

    public MemoryStream Build()
    {
        var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "mimetype", "application/epub+zip");
            Write(archive, "META-INF/container.xml", """
                <?xml version="1.0"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
                </container>
                """);

            if (EncryptionAlgorithm is not null)
            {
                Write(archive, "META-INF/encryption.xml", $"""
                    <?xml version="1.0"?>
                    <encryption xmlns="urn:oasis:names:tc:opendocument:xmlns:container" xmlns:enc="http://www.w3.org/2001/04/xmlenc#">
                      <enc:EncryptedData><enc:EncryptionMethod Algorithm="{EncryptionAlgorithm}"/>
                      <enc:CipherData><enc:CipherReference URI="OEBPS/{chapters[0].Path}"/></enc:CipherData></enc:EncryptedData>
                    </encryption>
                    """);
            }

            Write(archive, "OEBPS/content.opf", Package());

            foreach (var (path, xhtml) in chapters)
            {
                Write(archive, "OEBPS/" + path, xhtml);
            }

            foreach (var (path, bytes, _) in images)
            {
                var entry = archive.CreateEntry("OEBPS/" + path);
                entry.LastWriteTime = FixedTime;
                using var stream = entry.Open();
                stream.Write(bytes);
            }
        }

        output.Position = 0;
        return output;
    }

    public byte[] BuildBytes()
    {
        using var stream = Build();
        return stream.ToArray();
    }

    private string Package()
    {
        var metadata = new StringBuilder();
        metadata.Append($"<dc:title>{SecurityElement.Escape(Title)}</dc:title>");
        if (Author is not null)
        {
            metadata.Append($"<dc:creator>{SecurityElement.Escape(Author)}</dc:creator>");
        }

        metadata.Append($"<dc:language>{Language}</dc:language>");
        if (Identifier is not null)
        {
            metadata.Append($"<dc:identifier id=\"BookId\">{SecurityElement.Escape(Identifier)}</dc:identifier>");
        }

        if (CalibreSeries is not null)
        {
            metadata.Append($"<meta name=\"calibre:series\" content=\"{SecurityElement.Escape(CalibreSeries)}\"/>");
        }

        if (CalibreSeriesIndex is not null)
        {
            metadata.Append($"<meta name=\"calibre:series_index\" content=\"{CalibreSeriesIndex}\"/>");
        }

        if (CollectionSeries is not null)
        {
            metadata.Append($"<meta property=\"belongs-to-collection\" id=\"c01\">{SecurityElement.Escape(CollectionSeries)}</meta>");
            metadata.Append("<meta refines=\"#c01\" property=\"collection-type\">series</meta>");
            if (CollectionPosition is not null)
            {
                metadata.Append($"<meta refines=\"#c01\" property=\"group-position\">{CollectionPosition}</meta>");
            }
        }

        var manifest = new StringBuilder();
        var spine = new StringBuilder();
        for (var index = 0; index < chapters.Count; index++)
        {
            manifest.Append($"<item id=\"c{index}\" href=\"{chapters[index].Path}\" media-type=\"application/xhtml+xml\"/>");
            spine.Append($"<itemref idref=\"c{index}\"/>");
        }

        for (var index = 0; index < images.Count; index++)
        {
            var (path, _, mediaType) = images[index];
            var properties = path == CoverPath ? " properties=\"cover-image\"" : "";
            manifest.Append($"<item id=\"i{index}\" href=\"{path}\" media-type=\"{mediaType}\"{properties}/>");
        }

        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="BookId">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">{metadata}</metadata>
              <manifest>{manifest}</manifest>
              <spine>{spine}</spine>
            </package>
            """;
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        entry.LastWriteTime = FixedTime;
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
