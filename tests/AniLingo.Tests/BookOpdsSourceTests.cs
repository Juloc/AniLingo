using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Books;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AniLingo.Tests;

[TestClass]
public sealed class BookOpdsSourceTests
{
    [TestMethod]
    public async Task OpdsSettingsPreserveSecretWhenPasswordIsLeftBlank()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "anilingo-opds-"
            + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(
            directory,
            "sources.json");

        try
        {
            var created = await BookOpdsSettingsStore.UpsertAsync(
                null,
                "Private catalog",
                "https://catalog.example/opds",
                "reader@example.com",
                "secret",
                true,
                false,
                CancellationToken.None,
                path);

            await BookOpdsSettingsStore.UpsertAsync(
                created.Id,
                "Private catalog",
                "https://catalog.example/opds",
                "reader@example.com",
                null,
                true,
                true,
                CancellationToken.None,
                path);

            var loaded = BookOpdsSettingsStore.Load(path);

            Assert.AreEqual(1, loaded.Count);
            Assert.AreEqual(
                "secret",
                loaded[0].Password);
            Assert.IsTrue(
                loaded[0].HasCredentials);

            await BookOpdsSettingsStore.RemoveAsync(
                created.Id,
                CancellationToken.None,
                path);

            Assert.AreEqual(
                0,
                BookOpdsSettingsStore.Load(path).Count);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(
                    directory,
                    recursive: true);
            }
        }
    }

    [TestMethod]
    public void OpdsCredentialsAreLimitedToConfiguredOrigin()
    {
        Assert.IsTrue(
            BookCatalogService.IsOpdsCredentialTargetAllowed(
                "https://catalog.example/opds",
                "https://catalog.example/books/a.epub"));

        Assert.IsFalse(
            BookCatalogService.IsOpdsCredentialTargetAllowed(
                "https://catalog.example/opds",
                "https://cdn.example/books/a.epub"));

        Assert.IsFalse(
            BookCatalogService.IsOpdsCredentialTargetAllowed(
                "https://catalog.example/opds",
                "http://catalog.example/books/a.epub"));

        Assert.IsFalse(
            BookCatalogService.IsOpdsCredentialTargetAllowed(
                "https://catalog.example:8443/opds",
                "https://catalog.example/books/a.epub"));
    }

    [TestMethod]
    public async Task Opds2SearchAndImportUseConfiguredBasicAuth()
    {
        var dbPath = TempDatabasePath();
        var settingsDirectory = Path.Combine(
            Path.GetTempPath(),
            "anilingo-opds-settings-"
            + Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(
            settingsDirectory,
            "sources.json");

        try
        {
            await using var db =
                await CreateDatabaseAsync(dbPath);

            var authTargets = new List<string>();

            using var client = new HttpClient(
                new DelegateHttpMessageHandler(request =>
                {
                    var uri = request.RequestUri
                        ?? throw new AssertFailedException(
                            "Request URI was missing.");

                    if (request.Headers.Authorization is not null)
                    {
                        authTargets.Add(uri.AbsoluteUri);
                        Assert.AreEqual(
                            "Basic",
                            request.Headers.Authorization.Scheme);
                        Assert.AreEqual(
                            Convert.ToBase64String(
                                Encoding.UTF8.GetBytes(
                                    "reader:secret")),
                            request.Headers.Authorization.Parameter);
                    }

                    if (uri.AbsolutePath == "/opds")
                    {
                        return JsonResponse(
                            """
                            {
                              "metadata": {
                                "title": "Test OPDS"
                              },
                              "publications": [
                                {
                                  "metadata": {
                                    "title": "Alice in Wonderland",
                                    "author": [{"name": "Lewis Carroll"}],
                                    "description": "A curious adventure.",
                                    "language": "en"
                                  },
                                  "links": [
                                    {
                                      "rel": "http://opds-spec.org/acquisition/open-access",
                                      "type": "application/epub+zip",
                                      "href": "/books/alice.epub"
                                    }
                                  ]
                                }
                              ]
                            }
                            """);
                    }

                    if (uri.AbsolutePath
                        == "/books/alice.epub")
                    {
                        using var epub = BuildTestEpub(
                            "Alice in Wonderland",
                            "Lewis Carroll",
                            "en");

                        return EpubResponse(
                            epub.ToArray());
                    }

                    throw new AssertFailedException(
                        $"Unexpected request: {uri}");
                }))
            {
                BaseAddress =
                    new Uri("https://gutendex.com/")
            };

            var service = NewService(
                db,
                client,
                settingsPath);

            var source =
                await service.SaveOpdsSourceAsync(
                    null,
                    "Test OPDS",
                    "https://catalog.example/opds",
                    "reader",
                    "secret",
                    true,
                    false,
                    CancellationToken.None);

            var results = await service.SearchOpdsAsync(
                source.Id,
                "Alice",
                CancellationToken.None);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(
                "Alice in Wonderland",
                results[0].Title);
            Assert.AreEqual(
                "Lewis Carroll",
                results[0].Author);

            var workId = await service.ImportOpdsBookAsync(
                source.Id,
                "Alice",
                results[0].Key,
                CancellationToken.None);

            var work = await db.NovelWorks
                .AsNoTracking()
                .SingleAsync(x => x.Id == workId);

            Assert.AreEqual(
                "Alice in Wonderland",
                work.Title);
            StringAssert.StartsWith(
                work.MetadataProvider ?? "",
                "opds-");

            Assert.IsTrue(
                authTargets.Any(x =>
                    x.EndsWith(
                        "/opds",
                        StringComparison.Ordinal)));
            Assert.IsTrue(
                authTargets.Any(x =>
                    x.EndsWith(
                        "/books/alice.epub",
                        StringComparison.Ordinal)));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(dbPath);

            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(
                    settingsDirectory,
                    recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task Opds1OpenSearchTemplateIsDiscovered()
    {
        var dbPath = TempDatabasePath();
        var settingsDirectory = Path.Combine(
            Path.GetTempPath(),
            "anilingo-opds-atom-"
            + Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(
            settingsDirectory,
            "sources.json");

        try
        {
            await using var db =
                await CreateDatabaseAsync(dbPath);

            using var client = new HttpClient(
                new DelegateHttpMessageHandler(request =>
                {
                    var uri = request.RequestUri
                        ?? throw new AssertFailedException(
                            "Request URI was missing.");

                    if (uri.AbsolutePath == "/opds")
                    {
                        return XmlResponse(
                            """
                            <?xml version="1.0" encoding="utf-8"?>
                            <feed xmlns="http://www.w3.org/2005/Atom">
                              <title>Atom OPDS</title>
                              <link rel="search"
                                    type="application/opensearchdescription+xml"
                                    href="/opensearch.xml" />
                            </feed>
                            """);
                    }

                    if (uri.AbsolutePath
                        == "/opensearch.xml")
                    {
                        return XmlResponse(
                            """
                            <?xml version="1.0" encoding="utf-8"?>
                            <OpenSearchDescription xmlns="http://a9.com/-/spec/opensearch/1.1/">
                              <Url type="application/atom+xml;profile=opds-catalog;kind=acquisition"
                                   template="https://atom.example/find?q={searchTerms}" />
                            </OpenSearchDescription>
                            """);
                    }

                    if (uri.AbsolutePath == "/find")
                    {
                        Assert.AreEqual(
                            "?q=Treasure",
                            uri.Query);

                        return XmlResponse(
                            """
                            <?xml version="1.0" encoding="utf-8"?>
                            <feed xmlns="http://www.w3.org/2005/Atom"
                                  xmlns:dcterms="http://purl.org/dc/terms/">
                              <title>Search</title>
                              <entry>
                                <title>Treasure Island</title>
                                <author><name>Robert Louis Stevenson</name></author>
                                <summary>Pirates and treasure.</summary>
                                <dcterms:language>en</dcterms:language>
                                <link rel="http://opds-spec.org/acquisition/open-access"
                                      type="application/epub+zip"
                                      href="/books/treasure.epub" />
                              </entry>
                            </feed>
                            """);
                    }

                    throw new AssertFailedException(
                        $"Unexpected request: {uri}");
                }))
            {
                BaseAddress =
                    new Uri("https://gutendex.com/")
            };

            var service = NewService(
                db,
                client,
                settingsPath);

            var source =
                await service.SaveOpdsSourceAsync(
                    null,
                    "Atom OPDS",
                    "https://atom.example/opds",
                    null,
                    null,
                    true,
                    false,
                    CancellationToken.None);

            var results = await service.SearchOpdsAsync(
                source.Id,
                "Treasure",
                CancellationToken.None);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(
                "Treasure Island",
                results[0].Title);
            Assert.AreEqual(
                "Robert Louis Stevenson",
                results[0].Author);
            Assert.AreEqual(
                "en",
                results[0].Language);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(dbPath);

            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(
                    settingsDirectory,
                    recursive: true);
            }
        }
    }

    private static BookCatalogService NewService(
        AppDbContext db,
        HttpClient client,
        string settingsPath)
    {
        var configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Books:Opds:SettingsPath"] =
                            settingsPath
                    })
                .Build();

        return new BookCatalogService(
            client,
            db,
            new FakeBookTranslator(),
            configuration);
    }

    private static MemoryStream BuildTestEpub(
        string title,
        string author,
        string language)
    {
        var stream = new MemoryStream();

        using (var archive = new ZipArchive(
                   stream,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            AddEntry(
                archive,
                "META-INF/container.xml",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <container xmlns="urn:oasis:names:tc:opendocument:xmlns:container"
                           version="1.0">
                  <rootfiles>
                    <rootfile full-path="OEBPS/content.opf"
                              media-type="application/oebps-package+xml" />
                  </rootfiles>
                </container>
                """);

            AddEntry(
                archive,
                "OEBPS/content.opf",
                $"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://www.idpf.org/2007/opf"
                         xmlns:dc="http://purl.org/dc/elements/1.1/"
                         version="3.0">
                  <metadata>
                    <dc:title>{title}</dc:title>
                    <dc:creator>{author}</dc:creator>
                    <dc:language>{language}</dc:language>
                  </metadata>
                  <manifest>
                    <item id="c1"
                          href="chapter.xhtml"
                          media-type="application/xhtml+xml" />
                  </manifest>
                  <spine>
                    <itemref idref="c1" />
                  </spine>
                </package>
                """);

            AddEntry(
                archive,
                "OEBPS/chapter.xhtml",
                """
                <html xmlns="http://www.w3.org/1999/xhtml">
                  <body>
                    <h1>Chapter One</h1>
                    <p>Down the rabbit hole.</p>
                  </body>
                </html>
                """);
        }

        stream.Position = 0;
        return stream;
    }

    private static void AddEntry(
        ZipArchive archive,
        string path,
        string content)
    {
        var entry = archive.CreateEntry(path);

        using var writer = new StreamWriter(
            entry.Open(),
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false));

        writer.Write(content.Trim());
    }

    private static HttpResponseMessage JsonResponse(
        string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/opds+json")
        };

    private static HttpResponseMessage XmlResponse(
        string xml) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                xml.Trim(),
                Encoding.UTF8,
                "application/xml")
        };

    private static HttpResponseMessage EpubResponse(
        byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType =
            new MediaTypeHeaderValue(
                "application/epub+zip");

        return new HttpResponseMessage(
            HttpStatusCode.OK)
        {
            Content = content
        };
    }

    private static string TempDatabasePath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"anilingo-opds-{Guid.NewGuid():N}.db");

    private static async Task<AppDbContext> CreateDatabaseAsync(
        string path)
    {
        var options =
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(
                    $"Data Source={path};Foreign Keys=True")
                .Options;

        var db = new AppDbContext(options);
        await DatabaseMigrationBridge.UpgradeAsync(db);
        return db;
    }

    private sealed class FakeBookTranslator
        : IBookTranslator
    {
        public string Id => "fake-opds";

        public Task<string> TranslateLiteraryAsync(
            string sourceText,
            string sourceLanguage,
            string targetLanguage,
            string context,
            CancellationToken cancellationToken) =>
            Task.FromResult(sourceText);
    }

    private sealed class DelegateHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
