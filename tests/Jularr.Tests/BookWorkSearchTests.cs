using System.Net;
using System.Text;
using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.Novels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Jularr.Tests;

/// <summary>
/// #405: Books search presents canonical works. Provider records of one book merge (ISBN,
/// main title + author), works rank by intent (exact title and author first, companion
/// products below), the best cover is chosen deterministically, and library/request state
/// holds across provider identities.
/// </summary>
[TestClass]
public sealed class BookWorkSearchTests
{
    [TestMethod]
    public void ExactWorkOutranksWorkbooksJournalsAndSummaries()
    {
        var openLibrary = new[]
        {
            Book("ol-OL1W", "Atomic Habits Workbook", "Brian Moore", editions: 3),
            Book("ol-OL2W", "Summary of Atomic Habits", "Readtrepreneur", editions: 2),
            Book("ol-OL3W", "Atomic Habits", "James Clear", editions: 60, year: 2018, isbns: ["9780735211292"], cover: "https://covers.openlibrary.org/b/id/1-L.jpg?default=false"),
            Book("ol-OL4W", "Atomic Habits Journal", "James Clear", editions: 1)
        };
        var google = new[]
        {
            Book("gb-ah", "Atomic Habits: An Easy & Proven Way to Build Good Habits & Break Bad Ones", "James Clear", year: 2018, isbns: ["0735211299"], summary: "No matter your goals, Atomic Habits offers a proven framework.")
        };

        var ranked = BookWorkSearch.Rank("atomic habits", openLibrary, google);

        Assert.AreEqual(4, ranked.Count, "The Google record is the same work and merges into it.");
        Assert.AreEqual("Atomic Habits", ranked[0].Title);
        Assert.AreEqual("ol-OL3W", ranked[0].Id);
        CollectionAssert.AreEquivalent(new[] { "ol-OL3W", "gb-ah" }, ranked[0].Identities.ToArray());
        Assert.AreEqual("No matter your goals, Atomic Habits offers a proven framework.", ranked[0].Summary, "Metadata comes from every merged record.");
        CollectionAssert.AreEquivalent(
            new[] { "Atomic Habits Workbook", "Summary of Atomic Habits", "Atomic Habits Journal" },
            ranked.Skip(1).Select(book => book.Title).ToArray(),
            "Companion products stay discoverable below the work.");

        Assert.AreEqual("Atomic Habits Workbook", BookWorkSearch.Rank("atomic habits workbook", openLibrary, google)[0].Title,
            "Asking for the workbook finds the workbook.");
        Assert.AreEqual("Atomic Habits", BookWorkSearch.Rank("atomic habits james clear", openLibrary, google)[0].Title,
            "Author words in the query match the author, not the title.");
    }

    [TestMethod]
    public void IsbnQueryFindsTheEditionRegardlessOfTitle()
    {
        var ranked = BookWorkSearch.Rank(
            "978-3-442-17858-2",
            [Book("ol-A", "Die 1%-Methode", "James Clear", isbns: ["3442178584"]), Book("ol-B", "Atomic Habits", "James Clear", isbns: ["9780735211292"])]);

        Assert.AreEqual("ol-A", ranked[0].Id, "An ISBN-10 and its ISBN-13 are the same book.");
        Assert.AreEqual("9783442178582", BookWorkSearch.NormalizeIsbn("3-442-17858-4"));
        Assert.IsNull(BookWorkSearch.NormalizeIsbn("atomic habits"));
    }

    [TestMethod]
    public void OnlyIdentityEvidenceMergesRecords()
    {
        var ranked = BookWorkSearch.Rank(
            "dune",
            [
                Book("ol-D1", "Dune", "Frank Herbert", year: 1965),
                Book("ol-D2", "Dune Messiah", "Frank Herbert", year: 1969),
                Book("ol-D3", "Dune", "Someone Else")
            ],
            [
                Book("gb-d1", "Dune", "Frank Herbert", year: 1990),
                Book("gb-d4", "Dune: Deluxe Edition", "Frank Herbert", year: 2019),
                Book("gb-d5", "Dune", null)
            ]);

        var herbert = ranked.Single(book => book.Id == "ol-D1");
        CollectionAssert.AreEquivalent(new[] { "ol-D1", "gb-d1", "gb-d4", "gb-d5" }, herbert.Identities.ToArray(), "Same main title and author (or the full title without author): the same work.");
        Assert.AreEqual(1965, herbert.FirstPublishYear, "The work shows its original year, not a later edition's.");
        Assert.IsTrue(ranked.Any(book => book.Id == "ol-D2"), "A similar title is another work.");
        Assert.IsTrue(ranked.Any(book => book.Id == "ol-D3"), "The same title by another author is another work.");
        Assert.AreEqual(3, ranked.Count);
        Assert.AreEqual("ol-D1", ranked[0].Id, "More providers and a known year put the canonical Dune first.");
    }

    [TestMethod]
    public void BestCoverIsChosenDeterministicallyAndKeptAcrossSearches()
    {
        var thumbnail = "https://books.google.com/books/content?id=cv&printsec=frontcover&img=1&zoom=1&source=gbs_api";
        var smallThumbnail = "https://books.google.com/books/content?id=cv&printsec=frontcover&img=1&zoom=5&source=gbs_api";
        var large = "https://covers.openlibrary.org/b/id/99-L.jpg?default=false";
        var medium = "https://covers.openlibrary.org/b/id/99-M.jpg?default=false";
        var google = Book("gb-cv", "Cover Test Novel", "Ada Writer", cover: thumbnail, covers: [thumbnail, smallThumbnail]);
        var openLibrary = Book("ol-CV", "Cover Test Novel", "Ada Writer", cover: large, covers: [large, medium]);

        var first = BookWorkSearch.Rank("cover test novel", [google], [openLibrary]).Single();
        var reversed = BookWorkSearch.Rank("cover test novel", [openLibrary], [google]).Single();

        Assert.AreEqual(thumbnail, first.CoverImageUrl, "Google Books shows the current edition, whatever the provider order (#371).");
        Assert.AreEqual(thumbnail, reversed.CoverImageUrl);
        CollectionAssert.AreEqual(new[] { thumbnail, large, medium, smallThumbnail }, first.CoverCandidates.ToArray(),
            "Open Library artwork is the fallback; tiny thumbnails come last.");

        // A work first seen with Open Library artwork only keeps it while it is still offered.
        var onlyOpenLibrary = Book("ol-KP", "Kept Cover Tale", "Ben Author", cover: large);
        Assert.AreEqual(large, BookWorkSearch.Rank("kept cover tale", [onlyOpenLibrary]).Single().CoverImageUrl);
        Assert.AreEqual(
            large,
            BookWorkSearch.Rank("kept cover tale", [onlyOpenLibrary], [Book("gb-kp", "Kept Cover Tale", "Ben Author", cover: thumbnail)]).Single().CoverImageUrl,
            "Cards do not change pictures between searches.");
    }

    [TestMethod]
    public async Task SearchStillAnswersWhenAProviderFails()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"jularr-work-search-{Guid.NewGuid():N}")).FullName;
        try
        {
            await using var db = await Database(directory);
            using var client = new HttpClient(new Handler(request => request.RequestUri!.Host switch
            {
                "openlibrary.org" => Json("""
                    { "docs": [
                      { "key": "/works/OL3W", "title": "Atomic Habits", "author_name": ["James Clear"], "cover_i": 5, "first_publish_year": 2018, "edition_count": 60, "isbn": ["9780735211292"] },
                      { "key": "/works/OL1W", "title": "Atomic Habits Workbook", "author_name": ["Brian Moore"], "edition_count": 2 }
                    ] }
                    """),
                "www.googleapis.com" => throw new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests),
                "id.wikisource.org" => Json("""{ "query": { "search": [] } }"""),
                _ => throw new AssertFailedException($"Unexpected request: {request.RequestUri}")
            }))
            {
                BaseAddress = new Uri("https://gutendex.com/")
            };
            var service = new BookCatalogService(client, db, new NoopTranslator(), new ConfigurationBuilder().Build());

            var books = await service.SearchAsync("Atomic Habits", CancellationToken.None);

            CollectionAssert.AreEqual(new[] { "Atomic Habits", "Atomic Habits Workbook" }, books.Select(book => book.Title).ToArray());
            Assert.AreEqual("https://covers.openlibrary.org/b/id/5-L.jpg?default=false", books[0].CoverImageUrl);
            CollectionAssert.AreEqual(new[] { "9780735211292" }, books[0].Isbns.ToArray());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task LibraryAndRequestStateHoldAcrossProviderIdentities()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"jularr-work-state-{Guid.NewGuid():N}")).FullName;
        try
        {
            await using var db = await Database(directory);
            var store = new AcquisitionAccessStore(db);
            await store.CreateAsync(
                new AcquisitionRequestDraft(MediaAcquisitionKind.Book, BookCatalogService.CatalogRequestProvider, "gb-ah", "Atomic Habits", "James Clear", null),
                "alice",
                AcquisitionRequestStatus.Pending,
                null,
                CancellationToken.None);
            var imported = new NovelWork
            {
                SourceProvider = BookCatalogService.ImportedBookProvider,
                SourceKey = "dune",
                Title = "Dune: The Graphic Novel",
                MetadataProvider = BookCatalogService.CatalogRequestProvider,
                MetadataExternalId = "gb-dune"
            };
            db.NovelWorks.Add(imported);
            await db.SaveChangesAsync();

            var states = await new BookAddStateQuery(db, store).GetAsync(
                [
                    new BookAddLookup("ol-OL3W", "Atomic Habits", ["ol-OL3W", "gb-ah"]),
                    new BookAddLookup("ol-DUNE", "Dune", ["ol-DUNE", "gb-dune"]),
                    new BookAddLookup("ol-EMMA", "Emma", ["ol-EMMA"])
                ],
                CancellationToken.None);

            Assert.AreEqual("pending", states["ol-OL3W"].RequestStatus, "A request made through the Google record binds the merged work.");
            Assert.AreEqual("gb-ah", states["ol-OL3W"].RequestCatalogId);
            Assert.AreEqual(imported.Id, states["ol-DUNE"].LibraryWorkId, "The library link of any identity counts.");
            Assert.IsNull(states["ol-EMMA"].LibraryWorkId);
            Assert.IsNull(states["ol-EMMA"].RequestStatus);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static BookCatalogItem Book(
        string id,
        string title,
        string? author,
        int? editions = null,
        int? year = null,
        string[]? isbns = null,
        string? cover = null,
        string[]? covers = null,
        string? summary = null) =>
        new(id, title, author, summary, cover, [], year, null, null, "https://example.org/" + id, id.StartsWith("gb-", StringComparison.Ordinal) ? "Google Books" : "Open Library", null)
        {
            EditionCount = editions,
            Isbns = isbns ?? [],
            CoverCandidates = covers ?? []
        };

    private static async Task<AppDbContext> Database(string directory)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(directory, "jularr.db")};Foreign Keys=True")
            .Options);
        await DatabaseMigrationBridge.UpgradeAsync(db);
        return db;
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class NoopTranslator : IBookTranslator
    {
        public string Id => "noop-books";

        public Task<string> TranslateLiteraryAsync(string sourceText, string sourceLanguage, string targetLanguage, string context, CancellationToken cancellationToken) =>
            Task.FromResult(sourceText);
    }
}
