using System.IO.Compression;
using System.Text;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.Operations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AniLingo.Tests;

[TestClass]
public sealed class BookInboxImportTests
{
    [TestMethod]
    public async Task CompletedBookDownloadImportsInboxOnceAsRecordedOperation()
    {
        var root = TempDirectory();
        var inbox = Directory.CreateDirectory(Path.Combine(root, "inbox")).FullName;

        try
        {
            await using (var file = File.Create(Path.Combine(inbox, "first.epub")))
            {
                BuildTestEpub().CopyTo(file);
            }

            await using var services = await CreateServicesAsync(root, inbox);
            var db = services.GetRequiredService<AppDbContext>();

            var imported = await BookInboxImport.ImportAfterDownloadsAsync(
                services,
                [
                    Snapshot(BookInboxImport.SabnzbdDownloadKind, "owner"),
                    Snapshot(BookInboxImport.SabnzbdDownloadKind, "owner")
                ],
                CancellationToken.None);

            Assert.IsTrue(imported);
            Assert.AreEqual(1, await db.NovelWorks.CountAsync());

            var operations = await new OperationStore(db).ListAsync(
                new OperationListFilter(Search: "Import Books inbox"),
                CancellationToken.None);
            var operation = operations.Single();
            Assert.AreEqual(BookInboxImport.OperationKind, operation.Kind);
            Assert.AreEqual(OperationStatus.Succeeded, operation.Status);
            Assert.AreEqual("owner", operation.ProfileId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task OtherCompletedOperationsDoNotScanTheInbox()
    {
        var root = TempDirectory();
        var inbox = Directory.CreateDirectory(Path.Combine(root, "inbox")).FullName;

        try
        {
            await using (var file = File.Create(Path.Combine(inbox, "first.epub")))
            {
                BuildTestEpub().CopyTo(file);
            }

            await using var services = await CreateServicesAsync(root, inbox);
            var db = services.GetRequiredService<AppDbContext>();

            var imported = await BookInboxImport.ImportAfterDownloadsAsync(
                services,
                [Snapshot("anime-grab", "owner")],
                CancellationToken.None);

            Assert.IsFalse(imported);
            Assert.AreEqual(0, await db.NovelWorks.CountAsync());
            Assert.AreEqual(0, (await new OperationStore(db).ListAsync(
                new OperationListFilter(),
                CancellationToken.None)).Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task UnconfiguredInboxSkipsImport()
    {
        var root = TempDirectory();

        try
        {
            await using var services = await CreateServicesAsync(root, inboxPath: null);

            var imported = await BookInboxImport.ImportAfterDownloadsAsync(
                services,
                [Snapshot(BookInboxImport.SabnzbdDownloadKind, "owner")],
                CancellationToken.None);

            Assert.IsFalse(imported);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<ServiceProvider> CreateServicesAsync(
        string root,
        string? inboxPath)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(root, "anilingo.db")};Foreign Keys=True")
            .Options;
        var db = new AppDbContext(options);
        await DatabaseMigrationBridge.UpgradeAsync(db);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Books:InboxPath"] = inboxPath,
                ["Books:Translation:MemoryPath"] = Path.Combine(root, "translation-memory")
            })
            .Build();

        return new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<IBookTranslator, NoopBookTranslator>()
            .AddSingleton(provider => new BookCatalogService(
                new HttpClient(),
                db,
                provider.GetRequiredService<IBookTranslator>(),
                configuration))
            .AddSingleton(provider => new OperationRunner(db, provider))
            .BuildServiceProvider();
    }

    private static OperationSnapshot Snapshot(string kind, string profileId) =>
        new(
            Guid.NewGuid(),
            kind,
            "External downloads",
            OperationLane.Normal,
            OperationStatus.Succeeded,
            profileId,
            "SABnzbd download",
            Subject: null,
            ProgressPercent: 100,
            Message: null,
            Error: null,
            IsDownload: true,
            BytesTotal: null,
            BytesCompleted: null,
            BytesPerSecond: null,
            EtaUtc: null,
            Attempt: 1,
            Retryable: false,
            ExternalProvider: SabnzbdOperationsClient.ProviderId,
            ExternalId: "SABnzbd_nzo_test",
            CreatedAtUtc: DateTime.UtcNow,
            StartedAtUtc: DateTime.UtcNow,
            FinishedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: DateTime.UtcNow);

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "anilingo-book-inbox-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class NoopBookTranslator : IBookTranslator
    {
        public string Id => "noop-books";

        public Task<string> TranslateLiteraryAsync(
            string sourceText,
            string sourceLanguage,
            string targetLanguage,
            string context,
            CancellationToken cancellationToken) =>
            Task.FromResult(sourceText);
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
                    <dc:identifier>urn:isbn:978-0-306-40615-7</dc:identifier>
                    <dc:publisher>Test Publisher</dc:publisher>
                    <dc:date>2026-09-25</dc:date>
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
}
