using System.Collections.Concurrent;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.Novels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AniLingo.Tests;

[TestClass]
public sealed class BookRecommendationTests
{
    private static readonly DateTime BaseTime =
        new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void SeedsUseProfileProgressFirstThenLibraryRecency()
    {
        var olderRead = Book(
            "Older Read",
            "Author One",
            ["Fantasy"],
            importedDays: 1,
            progress: Progress(chapterIndex: 3, updatedDays: 5));
        var newestRead = Book(
            "Newest Read",
            "Author Two",
            ["Mystery"],
            importedDays: 0,
            progress: Progress(chapterIndex: 1, updatedDays: 9));
        var barelyOpened = Book(
            "Barely Opened",
            "Author Three",
            ["Romance"],
            importedDays: 2,
            progress: Progress(chapterIndex: 0, permille: 10, updatedDays: 20));
        var recentImport = Book(
            "Recent Import",
            "Author Four",
            ["History"],
            importedDays: 8);
        var noSignal = Book(
            "No Signal",
            null,
            ["Fiction", "General"],
            importedDays: 30);

        var seeds = BookRecommendationEngine.SelectSeeds(
            [olderRead, newestRead, barelyOpened, recentImport, noSignal],
            maxSeeds: 3);

        CollectionAssert.AreEqual(
            new[] { "Newest Read", "Older Read", "Recent Import" },
            seeds.Select(x => x.Book.Title).ToArray());
        Assert.AreEqual(BookRecommendationSeedReason.RecentReading, seeds[0].Reason);
        Assert.AreEqual(BookRecommendationSeedReason.RecentReading, seeds[1].Reason);
        Assert.AreEqual(BookRecommendationSeedReason.RecentlyAdded, seeds[2].Reason);
    }

    [TestMethod]
    public void SeedsFallBackToLibraryRecencyWithoutProgress()
    {
        var seeds = BookRecommendationEngine.SelectSeeds(
            [
                Book("First", "A Writer", ["Poetry"], importedDays: 1),
                Book("Second", "B Writer", ["Poetry"], importedDays: 4),
                Book("Third", "C Writer", ["Poetry"], importedDays: 4)
            ],
            maxSeeds: 3);

        CollectionAssert.AreEqual(
            new[] { "Second", "Third", "First" },
            seeds.Select(x => x.Book.Title).ToArray());
        Assert.IsTrue(seeds.All(x =>
            x.Reason == BookRecommendationSeedReason.RecentlyAdded));
    }

    [TestMethod]
    public void ContinueReadingSkipsFinishedBooksAndOrdersByRecentProgress()
    {
        var continueReading = BookRecommendationEngine.SelectContinueReading(
            [
                Book("Finished", "A", [], progress: Progress(chapterIndex: 9, chapterCount: 10, permille: 990, updatedDays: 9)),
                Book("Earlier", "B", [], progress: Progress(chapterIndex: 2, updatedDays: 1)),
                Book("Latest", "C", [], progress: Progress(chapterIndex: 0, permille: 5, updatedDays: 3)),
                Book("Unread", "D", [])
            ],
            maxItems: 12);

        CollectionAssert.AreEqual(
            new[] { "Latest", "Earlier" },
            continueReading.Select(x => x.Title).ToArray());
    }

    [TestMethod]
    public async Task SameAuthorOutranksSubjectOverlapAndRequiresSignal()
    {
        var seed = Book(
            "The Hobbit",
            "J. R. R. Tolkien",
            ["Fantasy fiction", "Dragons"],
            progress: Progress(chapterIndex: 2, updatedDays: 1));
        var search = new FakeSearch(query => query switch
        {
            "Fantasy fiction" =>
            [
                Catalog("ol-1", "Shared Subjects", "Other Writer", "Fantasy", "Dragons"),
                Catalog("ol-2", "The Silmarillion", "Tolkien, J.R.R.", "Mythology"),
                Catalog("ol-3", "Unrelated", "Someone Else", "Cooking")
            ],
            _ => []
        });

        var result = await BookRecommendationEngine.BuildAsync(
            [seed],
            search.SearchAsync,
            NoFallbackOptions(),
            CancellationToken.None);

        var shelf = result.Shelves.Single(x =>
            x.Kind == BookRecommendationShelfKind.BecauseYouRead);
        Assert.AreEqual("Because you read The Hobbit", shelf.Title);
        CollectionAssert.AreEqual(
            new[] { "ol-2", "ol-1" },
            shelf.Items.Select(x => x.Book.Id).ToArray());
        Assert.AreEqual(BookRecommendationEngine.SameAuthorScore, shelf.Items[0].Score);
        Assert.AreEqual("Same author", shelf.Items[0].Reason);
        Assert.AreEqual(2 * BookRecommendationEngine.SubjectOverlapScore, shelf.Items[1].Score);
        Assert.AreEqual("Shares Fantasy fiction, Dragons", shelf.Items[1].Reason);
    }

    [TestMethod]
    public async Task SubjectOverlapRanksByMatchedSubjectsWithDeterministicTieBreak()
    {
        var seed = Book(
            "Seed",
            "Seed Author",
            ["Science", "Space exploration", "Robots"],
            progress: Progress(chapterIndex: 1, updatedDays: 1));
        var search = new FakeSearch(query => query == "Science"
            ? [
                Catalog("gb-b", "Beta", "Writer B", "Robots"),
                Catalog("gb-c", "Alpha", "Writer C", "Robots"),
                Catalog("gb-a", "Gamma", "Writer A", "Science", "Space Exploration", "Robots -- Fiction"),
                Catalog("gb-d", "Delta", "Writer D", "Fiction", "General")
            ]
            : []);

        var result = await BookRecommendationEngine.BuildAsync(
            [seed],
            search.SearchAsync,
            NoFallbackOptions(),
            CancellationToken.None);

        var shelf = result.Shelves.Single(x =>
            x.Kind == BookRecommendationShelfKind.BecauseYouRead);
        CollectionAssert.AreEqual(
            new[] { "gb-a", "gb-b", "gb-c" },
            shelf.Items.Select(x => x.Book.Id).ToArray());
        Assert.AreEqual(3 * BookRecommendationEngine.SubjectOverlapScore, shelf.Items[0].Score);
    }

    [TestMethod]
    public async Task DuplicateTitleAuthorCandidatesAreSuppressed()
    {
        var seed = Book(
            "Emma",
            "Jane Austen",
            ["Romance"],
            progress: Progress(chapterIndex: 1, updatedDays: 1));
        var search = new FakeSearch(query => query == "Romance"
            ? [
                Catalog("gb-1", "Pride and Prejudice", "Jane Austen", "Romance"),
                Catalog("pg-2", "Pride and Prejudice: A Novel", "Austen, Jane", "Romance"),
                Catalog("ol-3", "The Pride and Prejudice", null, "Romance"),
                Catalog("ol-4", "Pride and Prejudice", "Different Person", "Romance")
            ]
            : []);

        var result = await BookRecommendationEngine.BuildAsync(
            [seed],
            search.SearchAsync,
            NoFallbackOptions(),
            CancellationToken.None);

        var ids = result.Shelves
            .SelectMany(x => x.Items)
            .Select(x => x.Book.Id)
            .ToArray();
        CollectionAssert.AreEqual(new[] { "gb-1", "ol-4" }, ids);
    }

    [TestMethod]
    public async Task BooksAlreadyInLibraryAreExcluded()
    {
        var owned = Book(
            "Persuasion",
            "Jane Austen",
            ["Romance"],
            catalogId: "pg-105",
            progress: Progress(chapterIndex: 1, updatedDays: 1));
        var alsoOwned = Book(
            "Sense and Sensibility",
            "Austen, Jane",
            [],
            importedDays: 3);
        var search = new FakeSearch(_ =>
        [
            Catalog("pg-105", "Persuasion (Illustrated)", "Jane Austen", "Romance"),
            Catalog("gb-9", "Different Title Same Record", "Jane Austen", "Romance"),
            Catalog("gb-10", "Sense and Sensibility", "Jane Austen", "Romance"),
            Catalog("gb-11", "Northanger Abbey", "Jane Austen", "Romance")
        ]);

        var result = await BookRecommendationEngine.BuildAsync(
            [owned, alsoOwned with { CatalogId = "gb-9" }],
            search.SearchAsync,
            NoFallbackOptions(),
            CancellationToken.None);

        var ids = result.Shelves
            .SelectMany(x => x.Items)
            .Select(x => x.Book.Id)
            .ToArray();
        CollectionAssert.AreEqual(new[] { "gb-11" }, ids);
    }

    [TestMethod]
    public async Task CandidatesAreDeduplicatedAcrossShelves()
    {
        var seed = Book(
            "Dune",
            "Frank Herbert",
            ["Deserts", "Ecology"],
            progress: Progress(chapterIndex: 4, updatedDays: 1));
        var shared = Catalog("ol-shared", "Dune Messiah", "Frank Herbert", "Deserts", "Ecology");
        var search = new FakeSearch(query => query switch
        {
            "Deserts" => [shared],
            "Frank Herbert" =>
            [
                shared with { Id = "gb-shared" },
                Catalog("ol-other", "The Dosadi Experiment", "Frank Herbert")
            ],
            "Ecology" =>
            [
                shared,
                Catalog("ol-eco", "Silent Spring", "Rachel Carson", "Ecology")
            ],
            _ => []
        });

        var result = await BookRecommendationEngine.BuildAsync(
            [seed],
            search.SearchAsync,
            NoFallbackOptions(),
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[]
            {
                BookRecommendationShelfKind.BecauseYouRead,
                BookRecommendationShelfKind.MoreByAuthor,
                BookRecommendationShelfKind.SimilarSubjects
            },
            result.Shelves.Select(x => x.Kind).ToArray());
        CollectionAssert.AreEqual(
            new[] { "ol-shared" },
            result.Shelves[0].Items.Select(x => x.Book.Id).ToArray());
        Assert.AreEqual("More by Frank Herbert", result.Shelves[1].Title);
        CollectionAssert.AreEqual(
            new[] { "ol-other" },
            result.Shelves[1].Items.Select(x => x.Book.Id).ToArray());
        Assert.AreEqual("Similar themes: Ecology", result.Shelves[2].Title);
        CollectionAssert.AreEqual(
            new[] { "ol-eco" },
            result.Shelves[2].Items.Select(x => x.Book.Id).ToArray());
    }

    [TestMethod]
    public async Task ProviderSearchesAndSeedsAreBounded()
    {
        var library = Enumerable.Range(0, 12)
            .Select(i => Book(
                $"Book {i:00}",
                $"Author{i:00} Surname{i:00}",
                [$"Subject{i:00}a", $"Subject{i:00}b", "Shared theme"],
                progress: Progress(chapterIndex: 1, updatedDays: i)))
            .ToArray();
        var search = new FakeSearch(
            query => [Catalog("id-" + query, "Result " + query, "Anyone", "Shared theme")],
            delay: TimeSpan.FromMilliseconds(20));

        var result = await BookRecommendationEngine.BuildAsync(
            library,
            search.SearchAsync,
            BookRecommendationOptions.Default,
            CancellationToken.None);

        Assert.AreEqual(BookRecommendationOptions.Default.MaxSeeds, result.Seeds.Count);
        Assert.IsLessThanOrEqualTo(
            BookRecommendationOptions.Default.MaxProviderSearches,
            search.Queries.Count);
        Assert.AreEqual(search.Queries.Count, result.ProviderSearchCount);
        Assert.AreEqual(
            search.Queries.Count,
            search.Queries.Distinct().Count());
        Assert.IsLessThanOrEqualTo(
            BookRecommendationOptions.Default.MaxConcurrentSearches,
            search.MaxConcurrency);
        Assert.IsFalse(search.Queries.Any(x => x is not null && x.StartsWith("Subject03", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ProviderFailuresDegradeToRemainingShelvesAndFallback()
    {
        var seed = Book(
            "Seed",
            "Failing Author",
            ["Working subject"],
            progress: Progress(chapterIndex: 1, updatedDays: 1));
        var search = new FakeSearch(query => query switch
        {
            "Failing Author" => throw new HttpRequestException("down"),
            "Working subject" =>
            [
                Catalog("ol-ok", "Still Here", "Other", "Working subject")
            ],
            null =>
            [
                Catalog("pg-1", "Popular One", "Classic Writer")
            ],
            _ => []
        });

        var result = await BookRecommendationEngine.BuildAsync(
            [seed],
            search.SearchAsync,
            BookRecommendationOptions.Default,
            CancellationToken.None);

        Assert.AreEqual(1, result.FailedProviderSearchCount);
        Assert.IsTrue(result.HasProviderFailures);
        CollectionAssert.AreEqual(
            new[]
            {
                BookRecommendationShelfKind.BecauseYouRead,
                BookRecommendationShelfKind.Popular
            },
            result.Shelves.Select(x => x.Kind).ToArray());
        Assert.AreEqual("Popular free book", result.Shelves[1].Items[0].Reason);
    }

    [TestMethod]
    public async Task HangingProviderIsCutOffBySearchBudget()
    {
        var seed = Book(
            "Seed",
            "Slow Author",
            ["Fast subject"],
            progress: Progress(chapterIndex: 1, updatedDays: 1));
        var search = new FakeSearch(async (query, token) =>
        {
            if (query == "Slow Author")
            {
                await Task.Delay(Timeout.Infinite, token);
            }

            return query == "Fast subject"
                ? [Catalog("ol-fast", "Fast Book", "Other", "Fast subject")]
                : [];
        });

        var started = DateTime.UtcNow;
        var result = await BookRecommendationEngine.BuildAsync(
            [seed],
            search.SearchAsync,
            new BookRecommendationOptions
            {
                SearchBudget = TimeSpan.FromMilliseconds(200),
                MinimumRecommendationsBeforeFallback = 0
            },
            CancellationToken.None);

        Assert.IsLessThan(10, (DateTime.UtcNow - started).TotalSeconds);
        Assert.AreEqual(1, result.FailedProviderSearchCount);
        Assert.AreEqual(
            "ol-fast",
            result.Shelves.Single().Items.Single().Book.Id);
    }

    [TestMethod]
    public async Task EmptyHistoryUsesOnlyThePopularFallbackSearch()
    {
        var search = new FakeSearch(query => query is null
            ? [Catalog("pg-1342", "Pride and Prejudice", "Austen, Jane")]
            : throw new AssertFailedException("Unexpected personalised search."));

        var result = await BookRecommendationEngine.BuildAsync(
            [],
            search.SearchAsync,
            BookRecommendationOptions.Default,
            CancellationToken.None);

        CollectionAssert.AreEqual(new string?[] { null }, search.Queries.ToArray());
        var shelf = result.Shelves.Single();
        Assert.AreEqual(BookRecommendationShelfKind.Popular, shelf.Kind);
        Assert.AreEqual("Popular free books", shelf.Title);
        Assert.AreEqual(0, result.ContinueReading.Count);
    }

    [TestMethod]
    public async Task RecommendationsUseOnlyTheCurrentProfilesProgress()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-book-recommendations-{Guid.NewGuid():N}.db");

        try
        {
            await using var db = await CreateDatabaseAsync(path);
            var fantasy = await AddWorkAsync(db, "Fantasy Book", "Fantasy Writer", """["Fantasy"]""", chapters: 3, importedDays: 1);
            var mystery = await AddWorkAsync(db, "Mystery Book", "Mystery Writer", """["Mystery"]""", chapters: 2, importedDays: 5);

            db.NovelProgress.AddRange(
                new NovelProgress
                {
                    ProfileId = "alice",
                    WorkId = fantasy.WorkId,
                    ChapterId = fantasy.ChapterIds[1],
                    PositionPermille = 400,
                    UpdatedAt = BaseTime
                },
                new NovelProgress
                {
                    ProfileId = "bob",
                    WorkId = mystery.WorkId,
                    ChapterId = mystery.ChapterIds[1],
                    PositionPermille = 990,
                    UpdatedAt = BaseTime.AddDays(1)
                });
            await db.SaveChangesAsync();

            var requests = 0;
            using var client = new HttpClient(new CountingHandler(() => Interlocked.Increment(ref requests)))
            {
                BaseAddress = new Uri("https://gutendex.com/")
            };
            var service = new BookRecommendationService(
                NewCatalogService(db, client),
                db);

            var alice = await service.LoadLibraryAsync("alice", "id", CancellationToken.None);
            var carol = await service.LoadLibraryAsync("carol", "id", CancellationToken.None);

            Assert.AreEqual(0, requests, "Loading the library must not call external catalogs.");

            var aliceFantasy = alice.Single(x => x.WorkId == fantasy.WorkId);
            Assert.IsNotNull(aliceFantasy.Progress);
            Assert.AreEqual(1, aliceFantasy.Progress.ChapterIndex);
            Assert.AreEqual(3, aliceFantasy.Progress.ChapterCount);
            Assert.IsNull(alice.Single(x => x.WorkId == mystery.WorkId).Progress);
            Assert.IsTrue(carol.All(x => x.Progress is null));

            var aliceSeeds = BookRecommendationEngine.SelectSeeds(alice, 1);
            Assert.AreEqual("Fantasy Book", aliceSeeds.Single().Book.Title);
            Assert.AreEqual(BookRecommendationSeedReason.RecentReading, aliceSeeds.Single().Reason);

            var carolSeeds = BookRecommendationEngine.SelectSeeds(carol, 1);
            Assert.AreEqual("Mystery Book", carolSeeds.Single().Book.Title);
            Assert.AreEqual(BookRecommendationSeedReason.RecentlyAdded, carolSeeds.Single().Reason);

            Assert.AreEqual(
                "Fantasy Book",
                BookRecommendationEngine.SelectContinueReading(alice, 12).Single().Title);
            Assert.AreEqual(
                0,
                BookRecommendationEngine.SelectContinueReading(carol, 12).Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static BookRecommendationOptions NoFallbackOptions() =>
        new()
        {
            MinimumRecommendationsBeforeFallback = 0
        };

    private static BookRecommendationLibraryBook Book(
        string title,
        string? author,
        string[] subjects,
        int importedDays = 0,
        string? catalogId = null,
        BookRecommendationProgress? progress = null) =>
        new(
            Guid.NewGuid(),
            title,
            author,
            null,
            subjects,
            catalogId,
            BaseTime.AddDays(importedDays),
            progress);

    private static BookRecommendationProgress Progress(
        int chapterIndex,
        int chapterCount = 10,
        int permille = 300,
        int updatedDays = 0) =>
        new(
            chapterIndex,
            chapterCount,
            permille,
            BaseTime.AddDays(updatedDays));

    private static BookCatalogItem Catalog(
        string id,
        string title,
        string? author,
        params string[] subjects) =>
        new(
            id,
            title,
            author,
            null,
            null,
            subjects,
            null,
            null,
            null,
            "https://example.test/" + id,
            "Test Catalog",
            null);

    private static async Task<AppDbContext> CreateDatabaseAsync(string path)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path};Foreign Keys=True")
            .Options;

        var db = new AppDbContext(options);
        await DatabaseMigrationBridge.UpgradeAsync(db);
        return db;
    }

    private static async Task<(Guid WorkId, Guid[] ChapterIds)> AddWorkAsync(
        AppDbContext db,
        string title,
        string author,
        string genresJson,
        int chapters,
        int importedDays)
    {
        var work = new NovelWork
        {
            SourceProvider = BookCatalogService.ImportedBookProvider,
            SourceKey = Guid.NewGuid().ToString("N"),
            SourceUrl = "upload:" + title,
            Title = title,
            Author = author,
            MetadataGenresJson = genresJson,
            ImportedAt = BaseTime.AddDays(importedDays)
        };
        db.NovelWorks.Add(work);
        var volume = new NovelVolume
        {
            WorkId = work.Id,
            Number = 1,
            Kind = NovelVolumeKinds.Book,
            SourceKey = "book"
        };
        db.NovelVolumes.Add(volume);

        var chapterIds = new Guid[chapters];
        for (var i = 0; i < chapters; i++)
        {
            var chapter = new NovelChapter
            {
                WorkId = work.Id,
                VolumeId = volume.Id,
                Number = i + 1,
                SourceUrl = $"upload:{title}:{i}",
                Title = $"Chapter {i + 1}",
                OriginalText = "Text",
                SourceHash = $"hash-{i}"
            };
            chapterIds[i] = chapter.Id;
            db.NovelChapters.Add(chapter);
        }

        await db.SaveChangesAsync();
        return (work.Id, chapterIds);
    }

    private static BookCatalogService NewCatalogService(
        AppDbContext db,
        HttpClient client)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Books:Translation:MemoryPath"] = Path.Combine(
                    Path.GetTempPath(),
                    "anilingo-book-recommendation-tests",
                    Guid.NewGuid().ToString("N"))
            })
            .Build();

        return new BookCatalogService(
            client,
            db,
            new NoopBookTranslator(),
            config);
    }

    private sealed class FakeSearch
    {
        private readonly Func<string?, CancellationToken, Task<IReadOnlyList<BookCatalogItem>>> handler;
        private readonly ConcurrentQueue<string?> queries = new();
        private int active;
        private int maxConcurrency;

        public FakeSearch(
            Func<string?, IReadOnlyList<BookCatalogItem>> results,
            TimeSpan? delay = null)
        {
            handler = async (query, token) =>
            {
                if (delay is TimeSpan wait)
                {
                    await Task.Delay(wait, token);
                }
                else
                {
                    await Task.Yield();
                }

                return results(query);
            };
        }

        public FakeSearch(
            Func<string?, CancellationToken, Task<IReadOnlyList<BookCatalogItem>>> handler)
        {
            this.handler = handler;
        }

        public IReadOnlyList<string?> Queries => queries.ToArray();
        public int MaxConcurrency => Volatile.Read(ref maxConcurrency);

        public async Task<IReadOnlyList<BookCatalogItem>> SearchAsync(
            string? query,
            CancellationToken cancellationToken)
        {
            queries.Enqueue(query);
            var now = Interlocked.Increment(ref active);
            int seen;
            do
            {
                seen = Volatile.Read(ref maxConcurrency);
            }
            while (now > seen
                && Interlocked.CompareExchange(ref maxConcurrency, now, seen) != seen);

            try
            {
                return await handler(query, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }
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

    private sealed class CountingHandler(Action onRequest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            onRequest();
            throw new HttpRequestException("External catalogs are disabled in this test.");
        }
    }
}
