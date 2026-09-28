using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Import;
using Jularr.Web.Features.Artwork;
using Jularr.Web.Features.Books;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Jularr.Tests;

// Issue #406: Books covers move beside the EPUB/PDF on the configured Books NAS library root
// (#389/#545) instead of only living under /data/books/covers, through the one artwork service
// (BesideMediaArtworkStore) #570's future local cache will sit in front of.
[TestClass]
public sealed class BookCoverArtworkTests
{
    [TestMethod]
    public async Task EmbeddedCoverIsPersistedBesideTheBookOnTheConfiguredNasRootAndRecorded()
    {
        await using var fixture = await Fixture.CreateAsync(withLibraryRoot: true);
        var epubPath = Path.Combine(fixture.LibraryRoot!, "Dune", "Dune.epub");
        Directory.CreateDirectory(Path.GetDirectoryName(epubPath)!);
        await File.WriteAllBytesAsync(epubPath, BuildEpubWithCover());

        var workId = (await fixture.Service.ImportBooksFromPathAsync(
            epubPath, "download", hint: null, singleBook: true, CancellationToken.None, preserveSourceFiles: true)).Single();

        var coverPath = Path.Combine(Path.GetDirectoryName(epubPath)!, "cover.png");
        Assert.IsTrue(File.Exists(coverPath), "The embedded cover is written beside the EPUB.");
        CollectionAssert.AreEqual(EpubTestBuilder.Png, await File.ReadAllBytesAsync(coverPath));

        var resolved = await fixture.Service.GetLocalCoverPathAsync(workId, null, CancellationToken.None);
        Assert.AreEqual(coverPath, resolved, "Library/detail rendering reads the same beside-media file.");

        var assets = new MediaArtworkAssetStore(fixture.Db);
        var rows = await assets.ListAsync(MediaArtworkScopes.Book, workId, CancellationToken.None);
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("cover.png", rows[0].FileName);

        var dataCovers = fixture.CoversPath;
        Assert.IsFalse(
            Directory.Exists(dataCovers) && Directory.EnumerateFiles(dataCovers, workId.ToString("N") + ".*").Any(),
            "No leftover /data copy once the NAS copy is canonical.");
    }

    [TestMethod]
    public async Task UserPlacedCoverBesideTheBookIsNeverReplaced()
    {
        await using var fixture = await Fixture.CreateAsync(withLibraryRoot: true);
        var folder = Path.Combine(fixture.LibraryRoot!, "Dune");
        Directory.CreateDirectory(folder);
        var epubPath = Path.Combine(folder, "Dune.epub");
        await File.WriteAllBytesAsync(epubPath, BuildEpubWithCover());

        var custom = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 9, 9, 9, 9, 0xFF, 0xD9 };
        await File.WriteAllBytesAsync(Path.Combine(folder, "cover.jpg"), custom);

        var workId = (await fixture.Service.ImportBooksFromPathAsync(
            epubPath, "download", hint: null, singleBook: true, CancellationToken.None, preserveSourceFiles: true)).Single();

        CollectionAssert.AreEqual(custom, await File.ReadAllBytesAsync(Path.Combine(folder, "cover.jpg")));
        Assert.IsFalse(File.Exists(Path.Combine(folder, "cover.png")), "No provider copy is added next to the user's cover.");
        Assert.AreEqual($"/Books/Cover/{workId}", (await fixture.Db.NovelWorks.AsNoTracking().SingleAsync(x => x.Id == workId)).CoverImageUrl);
    }

    [TestMethod]
    public async Task NoLibraryRootConfiguredKeepsUsingDataCovers()
    {
        await using var fixture = await Fixture.CreateAsync(withLibraryRoot: false);
        await using var epub = EpubWithCoverStream();

        var workId = await fixture.Service.ImportUploadedEpubAsync(epub, "dune.epub", CancellationToken.None);

        var resolved = await fixture.Service.GetLocalCoverPathAsync(workId, null, CancellationToken.None);
        Assert.IsNotNull(resolved);
        StringAssert.StartsWith(resolved, fixture.CoversPath, "No NAS root configured: unchanged /data behavior.");
    }

    private static byte[] BuildEpubWithCover() =>
        new EpubTestBuilder { Title = "Dune", Author = "Frank Herbert", Language = "en", Identifier = "urn:uuid:dune-nas", CoverPath = "cover.png" }
            .Image("cover.png")
            .Chapter("c1.xhtml", "Book One", "A beginning is the time for taking the most delicate care.")
            .BuildBytes();

    private static MemoryStream EpubWithCoverStream() =>
        new(new EpubTestBuilder { Title = "Dune", Author = "Frank Herbert", Language = "en", Identifier = "urn:uuid:dune-plain-data", CoverPath = "cover.png" }
            .Image("cover.png")
            .Chapter("c1.xhtml", "Book One", "Arrakis, the desert planet.")
            .BuildBytes());

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string tempRoot, AppDbContext db, BookCatalogService service, string? libraryRoot, string coversPath)
        {
            TempRoot = tempRoot;
            Db = db;
            Service = service;
            LibraryRoot = libraryRoot;
            CoversPath = coversPath;
        }

        public string TempRoot { get; }
        public AppDbContext Db { get; }
        public BookCatalogService Service { get; }
        public string? LibraryRoot { get; }
        public string CoversPath { get; }

        public static async Task<Fixture> CreateAsync(bool withLibraryRoot)
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), $"jularr-book-artwork-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempRoot);
            var coversPath = Path.Combine(tempRoot, "data-covers");

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(tempRoot, "jularr.db")};Pooling=False")
                .Options;
            var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var importSettings = new AnimeImportSettingsStore(tempRoot);
            string? libraryRoot = null;
            if (withLibraryRoot)
            {
                libraryRoot = Path.Combine(tempRoot, "media-books");
                Directory.CreateDirectory(libraryRoot);
                await importSettings.UpdateAsync(state => state with
                {
                    MediaLibraries = new Dictionary<MediaAcquisitionKind, MediaLibraryTarget>(state.MediaLibraries)
                    {
                        [MediaAcquisitionKind.Book] = new MediaLibraryTarget(LibraryRoot: libraryRoot)
                    }
                });
            }

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Books:CoversPath"] = coversPath,
                    ["Books:FilesPath"] = Path.Combine(tempRoot, "data-files"),
                    ["Books:Translation:MemoryPath"] = Path.Combine(tempRoot, "translation-memory")
                })
                .Build();

            var service = new BookCatalogService(
                new HttpClient(),
                db,
                new NoopBookTranslator(),
                config,
                dataProtectionProvider: null,
                discoverySettingsDirectory: null,
                importSettings: importSettings);

            return new Fixture(tempRoot, db, service, libraryRoot, coversPath);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            try
            {
                Directory.Delete(TempRoot, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class NoopBookTranslator : IBookTranslator
    {
        public string Id => "noop";

        public Task<string> TranslateLiteraryAsync(
            string sourceText,
            string sourceLanguage,
            string targetLanguage,
            string context,
            CancellationToken cancellationToken) =>
            Task.FromResult(sourceText);
    }
}
