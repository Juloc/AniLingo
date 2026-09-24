using System.IO.Compression;
using System.Net;
using System.Text;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Books;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AniLingo.Tests;

[TestClass]
public sealed class BookCatalogServiceTests
{
    [TestMethod]
    public void ReadableSampleStripsGutenbergBoilerplateAndSkipsDenseContents()
    {
        var raw = """
            Project Gutenberg header
            *** START OF THE PROJECT GUTENBERG EBOOK TEST ***

            CONTENTS

            CHAPTER I. One
            CHAPTER II. Two
            CHAPTER III. Three

            CHAPTER I. One

            This is the real first chapter. It has enough prose to be readable.
            Another paragraph continues the story and establishes the actual content.
            """ + new string('x', 1200) + """

            CHAPTER II. Two

            Later chapter.
            *** END OF THE PROJECT GUTENBERG EBOOK TEST ***
            trailing license
            """;

        var sample = BookCatalogService.ExtractReadableSample(raw, 1000);

        Assert.IsTrue(sample.StartsWith("CHAPTER I. One", StringComparison.Ordinal));
        Assert.IsTrue(sample.Contains("real first chapter", StringComparison.Ordinal));
        Assert.IsFalse(sample.Contains("Project Gutenberg header", StringComparison.Ordinal));
        Assert.IsFalse(sample.Contains("trailing license", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SearchUsesOpenLibraryForGeneralTitles()
    {
        var path = TempDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(path);
            using var client = new HttpClient(new DelegateHttpMessageHandler(request =>
            {
                Assert.AreEqual("openlibrary.org", request.RequestUri?.Host);

                return JsonResponse("""
                    {
                      "docs": [
                        {
                          "key": "/works/OL27448W",
                          "title": "The Lord of the Rings",
                          "author_name": ["J. R. R. Tolkien"],
                          "cover_i": 14625765,
                          "first_publish_year": 1954,
                          "subject": ["Fantasy fiction", "Middle Earth"]
                        }
                      ]
                    }
                    """);
            }))
            {
                BaseAddress = new Uri("https://gutendex.com/")
            };

            var service = NewService(db, client);
            var books = await service.SearchAsync(
                "Herr der Ringe",
                CancellationToken.None);

            Assert.AreEqual(1, books.Count);
            Assert.AreEqual("ol-OL27448W", books[0].Id);
            Assert.AreEqual("The Lord of the Rings", books[0].Title);
            Assert.AreEqual("J. R. R. Tolkien", books[0].Author);
            Assert.AreEqual(1954, books[0].FirstPublishYear);
            Assert.IsFalse(books[0].CanAcquire);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task ReadableSampleFollowsGutenbergRedirect()
    {
        var path = TempDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(path);
            var requested = new List<string>();

            using var client = new HttpClient(new DelegateHttpMessageHandler(request =>
            {
                var uri = request.RequestUri
                    ?? throw new AssertFailedException("Request URI was missing.");
                requested.Add(uri.ToString());

                if (uri.AbsolutePath == "/ebooks/2701.txt.utf-8")
                {
                    var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                    redirect.Headers.Location = new Uri(
                        "/cache/epub/2701/pg2701.txt",
                        UriKind.Relative);
                    return redirect;
                }

                if (uri.AbsolutePath == "/cache/epub/2701/pg2701.txt")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "*** START OF TEST ***\nCall me Ishmael.\n*** END OF TEST ***",
                            Encoding.UTF8,
                            "text/plain")
                    };
                }

                throw new AssertFailedException($"Unexpected request: {uri}");
            }))
            {
                BaseAddress = new Uri("https://gutendex.com/")
            };

            var service = NewService(db, client);
            var book = new BookCatalogItem(
                "2701",
                "Moby Dick",
                "Herman Melville",
                null,
                null,
                [],
                1851,
                "https://www.gutenberg.org/ebooks/2701.txt.utf-8",
                null,
                "https://www.gutenberg.org/ebooks/2701",
                "Project Gutenberg",
                "Project Gutenberg");

            var sample = await service.GetReadableSampleAsync(
                book,
                CancellationToken.None,
                500);

            Assert.AreEqual("Call me Ishmael.", sample);
            CollectionAssert.AreEqual(
                new[]
                {
                    "https://www.gutenberg.org/ebooks/2701.txt.utf-8",
                    "https://www.gutenberg.org/cache/epub/2701/pg2701.txt"
                },
                requested);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [TestMethod]
    public void EpubParserUsesPackageSpineAndPreservesParagraphs()
    {
        using var epub = BuildTestEpub();

        var book = EpubBookParser.Parse(
            epub,
            "fallback.epub");

        Assert.AreEqual("Test Book", book.Title);
        Assert.AreEqual("Test Author", book.Author);
        Assert.AreEqual("en", book.Language);
        Assert.AreEqual(2, book.Chapters.Count);
        Assert.AreEqual("Chapter One", book.Chapters[0].Title);
        StringAssert.Contains(book.Chapters[0].Text, "Hello world.");
        StringAssert.Contains(book.Chapters[0].Text, "Next paragraph.");
        CollectionAssert.Contains(book.Subjects.ToArray(), "Fantasy");
    }

    [TestMethod]
    public async Task EpubImportIsDeterministicAndCreatesCanonicalReadingChapters()
    {
        var path = TempDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(path);
            using var client = new HttpClient(new DelegateHttpMessageHandler(
                _ => throw new AssertFailedException("Import should not use HTTP.")))
            {
                BaseAddress = new Uri("https://gutendex.com/")
            };

            var service = NewService(db, client);

            await using var first = BuildTestEpub();
            var firstId = await service.ImportUploadedEpubAsync(
                first,
                "test.epub",
                CancellationToken.None);

            await using var second = BuildTestEpub();
            var secondId = await service.ImportUploadedEpubAsync(
                second,
                "test.epub",
                CancellationToken.None);

            Assert.AreEqual(firstId, secondId);
            Assert.AreEqual(1, await db.NovelWorks.CountAsync());
            Assert.AreEqual(2, await db.NovelChapters.CountAsync());

            var work = await db.NovelWorks.SingleAsync();
            Assert.AreEqual(BookCatalogService.ImportedBookProvider, work.SourceProvider);
            Assert.AreEqual("Test Book", work.Title);
            Assert.AreEqual("Test Author", work.Author);
            Assert.AreEqual("EPUB:en", work.Format);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task BookTranslationIsCachedBySourceHashAndTargetLanguage()
    {
        var path = TempDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(path);
            using var client = new HttpClient(new DelegateHttpMessageHandler(
                _ => throw new AssertFailedException("Translation should not use HTTP.")))
            {
                BaseAddress = new Uri("https://gutendex.com/")
            };
            var translator = new FakeBookTranslator();
            var service = NewService(db, client, translator);

            await using var epub = BuildTestEpub();
            var workId = await service.ImportUploadedEpubAsync(
                epub,
                "test.epub",
                CancellationToken.None);

            var chapterId = await db.NovelChapters
                .Where(x => x.WorkId == workId)
                .OrderBy(x => x.Number)
                .Select(x => x.Id)
                .FirstAsync();

            var first = await service.TranslateChapterAsync(
                chapterId,
                "id",
                CancellationToken.None);
            var second = await service.TranslateChapterAsync(
                chapterId,
                "id",
                CancellationToken.None);

            Assert.AreEqual(first.Id, second.Id);
            Assert.AreEqual(1, translator.CallCount);
            Assert.AreEqual("id", first.TargetLanguage);
            StringAssert.StartsWith(first.Text, "[id]");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task IntegrationSettingsPersistWithoutExposingDefaults()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "anilingo-books-settings-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "integrations.json");

        try
        {
            await BookIntegrationSettingsStore.SaveAsync(
                new BookIntegrationSettings(
                    "http://sabnzbd:8080/",
                    "secret-api-key",
                    "",
                    "./book-inbox"),
                CancellationToken.None,
                path);

            var loaded = BookIntegrationSettingsStore.Load(path);

            Assert.AreEqual(
                "http://sabnzbd:8080",
                loaded.SabnzbdBaseUrl);
            Assert.AreEqual(
                "secret-api-key",
                loaded.SabnzbdApiKey);
            Assert.IsNull(loaded.SabnzbdCategory);
            Assert.AreEqual(
                Path.GetFullPath("./book-inbox"),
                loaded.InboxPath);
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
    public void RemoteEpubUrlRejectsUnsafeTargets()
    {
        Assert.ThrowsException<InvalidOperationException>(() =>
            BookCatalogService.ValidateExternalEpubUriSyntax(
                new Uri("http://example.com/book.epub")));

        Assert.ThrowsException<InvalidOperationException>(() =>
            BookCatalogService.ValidateExternalEpubUriSyntax(
                new Uri("https://127.0.0.1/book.epub")));

        Assert.ThrowsException<InvalidOperationException>(() =>
            BookCatalogService.ValidateExternalEpubUriSyntax(
                new Uri("https://[::1]/book.epub")));

        BookCatalogService.ValidateExternalEpubUriSyntax(
            new Uri("https://example.com/book.epub"));
    }

    [TestMethod]
    public async Task SabSubmissionKeepsApiKeyOutOfRequestUrl()
    {
        var path = TempDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(path);
            var observedMethod = HttpMethod.Get;
            var observedUri = "";
            var observedBody = "";

            using var client = new HttpClient(new DelegateHttpMessageHandler(request =>
            {
                observedMethod = request.Method;
                observedUri = request.RequestUri?.ToString() ?? "";
                observedBody = request.Content?
                    .ReadAsStringAsync()
                    .GetAwaiter()
                    .GetResult()
                    ?? "";

                return JsonResponse(
                    """{"status":true}""");
            }))
            {
                BaseAddress = new Uri("https://gutendex.com/")
            };

            var service = NewService(
                db,
                client,
                configuration: new Dictionary<string, string?>
                {
                    ["Books:SABnzbd:BaseUrl"] = "http://sabnzbd:8080/",
                    ["Books:SABnzbd:ApiKey"] = "top-secret",
                    ["Books:SABnzbd:Category"] = "books"
                });

            var result = await service.QueueSabnzbdUrlAsync(
                "https://downloads.example/book.nzb",
                "Test Book",
                CancellationToken.None);

            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(HttpMethod.Post, observedMethod);
            Assert.IsFalse(
                observedUri.Contains(
                    "top-secret",
                    StringComparison.Ordinal));
            StringAssert.Contains(
                observedBody,
                "apikey=top-secret");
            StringAssert.Contains(
                observedBody,
                "mode=addurl");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static BookCatalogService NewService(
        AppDbContext db,
        HttpClient client,
        IBookTranslator? translator = null,
        IReadOnlyDictionary<string, string?>? configuration = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configuration)
            .Build();

        return new BookCatalogService(
            client,
            db,
            translator ?? new FakeBookTranslator(),
            config);
    }

    private static MemoryStream BuildTestEpub()
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
                <container xmlns="urn:oasis:names:tc:opendocument:xmlns:container" version="1.0">
                  <rootfiles>
                    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml" />
                  </rootfiles>
                </container>
                """);

            AddEntry(
                archive,
                "OEBPS/content.opf",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://www.idpf.org/2007/opf"
                         xmlns:dc="http://purl.org/dc/elements/1.1/"
                         version="3.0">
                  <metadata>
                    <dc:title>Test Book</dc:title>
                    <dc:creator>Test Author</dc:creator>
                    <dc:language>en</dc:language>
                    <dc:description>A test story.</dc:description>
                    <dc:subject>Fantasy</dc:subject>
                  </metadata>
                  <manifest>
                    <item id="c1" href="chapter1.xhtml" media-type="application/xhtml+xml" />
                    <item id="c2" href="chapter2.xhtml" media-type="application/xhtml+xml" />
                  </manifest>
                  <spine>
                    <itemref idref="c1" />
                    <itemref idref="c2" />
                  </spine>
                </package>
                """);

            AddEntry(
                archive,
                "OEBPS/chapter1.xhtml",
                """
                <html xmlns="http://www.w3.org/1999/xhtml">
                  <body>
                    <h1>Chapter One</h1>
                    <p>Hello <em>world</em>.</p>
                    <p>Next paragraph.</p>
                  </body>
                </html>
                """);

            AddEntry(
                archive,
                "OEBPS/chapter2.xhtml",
                """
                <html xmlns="http://www.w3.org/1999/xhtml">
                  <body>
                    <h1>Chapter Two</h1>
                    <p>The story continues here.</p>
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
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content.Trim());
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        };

    private static string TempDatabasePath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"anilingo-books-{Guid.NewGuid():N}.db");

    private static async Task<AppDbContext> CreateDatabaseAsync(string path)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path};Foreign Keys=True")
            .Options;

        var db = new AppDbContext(options);
        await DatabaseMigrationBridge.UpgradeAsync(db);
        return db;
    }

    private sealed class FakeBookTranslator : IBookTranslator
    {
        public string Id => "fake-books";
        public int CallCount { get; private set; }

        public Task<string> TranslateLiteraryAsync(
            string sourceText,
            string sourceLanguage,
            string targetLanguage,
            string context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(
                $"[{targetLanguage}] {sourceText}");
        }
    }

    private sealed class DelegateHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
