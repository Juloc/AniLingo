using System.Data.Common;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.Manga;
using AniLingo.Web.Features.Novels;
using AniLingo.Web.Features.Reading;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace AniLingo.Tests;

/// <summary>
/// Pins the #338 Continue Reading read model: newest first across Novels,
/// Books and Manga, deterministic tie-break, profile isolation, the shared
/// "finished" rule, exact resume URLs and a constant number of queries.
/// </summary>
[TestClass]
public sealed class ContinueReadingQueryTests
{
    private const string ReaderA = "reader-a";
    private const string ReaderB = "reader-b";
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task OrdersAcrossMediaNewestFirstAndAppliesLimit()
    {
        await using var fixture = await ContinueReadingFixture.CreateAsync();
        var oldNovel = await fixture.SeedNovelAsync("Old novel", chapters: 3);
        var novel = await fixture.SeedNovelAsync("Novel", chapters: 3);
        var book = await fixture.SeedBookAsync("Book", chapters: 3);
        var manga = await fixture.SeedMangaAsync("Manga", (1, 20), (2, 20));

        await fixture.SetNovelProgressAsync(ReaderA, oldNovel, 0, 200, Now.AddHours(-4));
        await fixture.SetNovelProgressAsync(ReaderA, novel, 1, 300, Now.AddHours(-3));
        await fixture.SetNovelProgressAsync(ReaderA, book, 0, 400, Now.AddHours(-1));
        await fixture.SetMangaProgressAsync(ReaderA, manga, 0, 4, Now.AddHours(-2));

        var all = await fixture.Query().GetAsync(ReaderA);
        CollectionAssert.AreEqual(
            new[] { book.WorkId, manga.SeriesId, novel.WorkId, oldNovel.WorkId },
            all.Select(x => x.WorkId).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                ContinueReadingKind.Book,
                ContinueReadingKind.Manga,
                ContinueReadingKind.Novel,
                ContinueReadingKind.Novel
            },
            all.Select(x => x.Kind).ToArray());
        Assert.AreEqual(Now.AddHours(-1), all[0].LastReadAt);
        Assert.AreEqual(DateTimeKind.Utc, all[0].LastReadAt.Kind);
        Assert.AreEqual(Now.AddHours(-2), all[1].LastReadAt);

        var limited = await fixture.Query().GetAsync(ReaderA, limit: 3);
        CollectionAssert.AreEqual(
            new[] { book.WorkId, manga.SeriesId, novel.WorkId },
            limited.Select(x => x.WorkId).ToArray());

        Assert.AreEqual("Book", all[0].Title);
        Assert.AreEqual("Manga", all[1].Title);
        Assert.AreEqual(40, all[0].ProgressPercent);
        Assert.AreEqual(25, all[1].ProgressPercent, "Manga progress is the page share of the saved chapter.");
    }

    [TestMethod]
    public async Task EqualTimestampsBreakTiesByWorkIdAcrossMedia()
    {
        await using var fixture = await ContinueReadingFixture.CreateAsync();
        var novel = await fixture.SeedNovelAsync(
            "Novel",
            chapters: 2,
            id: Guid.Parse("cccccccc-0000-0000-0000-000000000000"));
        var book = await fixture.SeedBookAsync(
            "Book",
            chapters: 2,
            id: Guid.Parse("22222222-0000-0000-0000-000000000000"));
        var manga = await fixture.SeedMangaAsync(
            "Manga",
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000000"),
            (1, 10));

        await fixture.SetNovelProgressAsync(ReaderA, novel, 0, 100, Now);
        await fixture.SetNovelProgressAsync(ReaderA, book, 0, 100, Now);
        await fixture.SetMangaProgressAsync(ReaderA, manga, 0, 2, Now);

        var all = await fixture.Query().GetAsync(ReaderA);
        CollectionAssert.AreEqual(
            new[] { book.WorkId, manga.SeriesId, novel.WorkId },
            all.Select(x => x.WorkId).ToArray());

        var boundary = await fixture.Query().GetAsync(ReaderA, limit: 2);
        CollectionAssert.AreEqual(
            new[] { book.WorkId, manga.SeriesId },
            boundary.Select(x => x.WorkId).ToArray(),
            "Ties at the limit boundary resolve by the same work-id order.");
    }

    [TestMethod]
    public async Task ReturnsOnlyTheRequestingProfilesProgress()
    {
        await using var fixture = await ContinueReadingFixture.CreateAsync();
        var novel = await fixture.SeedNovelAsync("Shared novel", chapters: 2);
        var manga = await fixture.SeedMangaAsync("Shared manga", (1, 10));

        await fixture.SetNovelProgressAsync(ReaderA, novel, 0, 100, Now);
        await fixture.SetNovelProgressAsync(ReaderB, novel, 1, 500, Now.AddMinutes(5));
        await fixture.SetMangaProgressAsync(ReaderB, manga, 0, 3, Now);

        var a = await fixture.Query().GetAsync(ReaderA);
        var b = await fixture.Query().GetAsync(ReaderB);
        var none = await fixture.Query().GetAsync("reader-without-progress");

        Assert.AreEqual(1, a.Count);
        Assert.AreEqual(novel.ChapterIds[0], a[0].ChapterId);
        Assert.AreEqual(10, a[0].ProgressPercent);
        Assert.AreEqual(2, b.Count);
        Assert.AreEqual(novel.ChapterIds[1], b[0].ChapterId);
        Assert.AreEqual(0, none.Count);
    }

    [TestMethod]
    public async Task ExcludesFinishedWorksWithoutConsumingLimitSlots()
    {
        await using var fixture = await ContinueReadingFixture.CreateAsync();

        var novelFinished = await fixture.SeedNovelAsync("Novel finished", chapters: 2);
        var novelNearlyDone = await fixture.SeedNovelAsync("Novel nearly done", chapters: 2);
        var novelEarlierChapterDone = await fixture.SeedNovelAsync("Novel earlier chapter", chapters: 2);
        var bookFinished = await fixture.SeedBookAsync("Book finished", chapters: 3);
        var bookReading = await fixture.SeedBookAsync("Book reading", chapters: 3);
        var mangaFinished = await fixture.SeedMangaAsync("Manga finished", (1, 10), (2, 12));
        var mangaLastChapter = await fixture.SeedMangaAsync("Manga last chapter", (1, 10), (2, 12));
        var mangaEarlierChapterDone = await fixture.SeedMangaAsync("Manga earlier chapter", (1, 10), (2, 12));

        // Finished items are the newest ones; they must not crowd out the rest.
        await fixture.SetNovelProgressAsync(ReaderA, novelFinished, 1, 950, Now);
        await fixture.SetNovelProgressAsync(ReaderA, bookFinished, 2, 1000, Now.AddMinutes(-1));
        await fixture.SetMangaProgressAsync(ReaderA, mangaFinished, 1, 11, Now.AddMinutes(-2));

        await fixture.SetNovelProgressAsync(ReaderA, novelNearlyDone, 1, 949, Now.AddMinutes(-10));
        await fixture.SetNovelProgressAsync(ReaderA, novelEarlierChapterDone, 0, 1000, Now.AddMinutes(-11));
        await fixture.SetNovelProgressAsync(ReaderA, bookReading, 1, 990, Now.AddMinutes(-12));
        await fixture.SetMangaProgressAsync(ReaderA, mangaLastChapter, 1, 10, Now.AddMinutes(-13));
        await fixture.SetMangaProgressAsync(ReaderA, mangaEarlierChapterDone, 0, 9, Now.AddMinutes(-14));

        var items = await fixture.Query().GetAsync(ReaderA);
        CollectionAssert.AreEqual(
            new[]
            {
                novelNearlyDone.WorkId,
                novelEarlierChapterDone.WorkId,
                bookReading.WorkId,
                mangaLastChapter.SeriesId,
                mangaEarlierChapterDone.SeriesId
            },
            items.Select(x => x.WorkId).ToArray());

        var first = await fixture.Query().GetAsync(ReaderA, limit: 1);
        Assert.AreEqual(novelNearlyDone.WorkId, first.Single().WorkId);

        // The Novel/Book rule is the Books recommendation "finished" rule on
        // the shared NovelProgress row.
        Assert.IsTrue(new BookRecommendationProgress(2, 3, 1000, Now).IsFinished);
        Assert.IsFalse(new BookRecommendationProgress(1, 3, 990, Now).IsFinished);
        Assert.IsFalse(new BookRecommendationProgress(1, 2, 949, Now).IsFinished);
        Assert.IsTrue(new BookRecommendationProgress(1, 2, 950, Now).IsFinished);

        // A newly imported later chapter makes the finished work resumable again.
        await fixture.AddMangaChapterAsync(mangaFinished, 3, 8);
        var afterImport = await fixture.Query().GetAsync(ReaderA);
        Assert.AreEqual(mangaFinished.SeriesId, afterImport[0].WorkId);
    }

    [TestMethod]
    public async Task ResumeUrlsTargetTheSavedReaderPosition()
    {
        await using var fixture = await ContinueReadingFixture.CreateAsync();
        var novel = await fixture.SeedNovelAsync("Novel", chapters: 3);
        var book = await fixture.SeedBookAsync("Book", chapters: 3);
        var manga = await fixture.SeedMangaAsync("Manga", (1, 10), (2.5, 20));

        await fixture.SetNovelProgressAsync(ReaderA, novel, 2, 250, Now);
        await fixture.SetNovelProgressAsync(ReaderA, book, 1, 500, Now.AddMinutes(-1), anchorLanguage: "de");
        await fixture.SetMangaProgressAsync(ReaderA, manga, 1, 4, Now.AddMinutes(-2));

        var items = (await fixture.Query().GetAsync(ReaderA)).ToDictionary(x => x.Kind);

        Assert.AreEqual($"/Novels/Read/{novel.ChapterIds[2]}", items[ContinueReadingKind.Novel].ResumeUrl);
        Assert.AreEqual("3", items[ContinueReadingKind.Novel].ChapterLabel);
        Assert.IsNull(items[ContinueReadingKind.Novel].PageNumber);

        Assert.AreEqual($"/Books/Read/{book.ChapterIds[1]}?lang=de", items[ContinueReadingKind.Book].ResumeUrl);

        var mangaItem = items[ContinueReadingKind.Manga];
        Assert.AreEqual($"/Manga/Read/{manga.ChapterIds[1]}?page=4", mangaItem.ResumeUrl);
        Assert.AreEqual(5, mangaItem.PageNumber);
        Assert.AreEqual(20, mangaItem.PageCount);
        Assert.AreEqual("2.5", mangaItem.ChapterLabel);
        Assert.AreEqual(
            $"/Manga/Read/{manga.ChapterIds[0]}?handler=Page&page=0",
            mangaItem.CoverImageUrl,
            "Without a cover the first page of the first chapter is used, like the Manga library.");
    }

    [TestMethod]
    public async Task CanonicalWritersAndLibraryListsAgreeWithTheReadModel()
    {
        await using var fixture = await ContinueReadingFixture.CreateAsync();
        var novel = await fixture.SeedNovelAsync("Novel", chapters: 3);
        var book = await fixture.SeedBookAsync("Book", chapters: 3);
        var manga = await fixture.SeedMangaAsync("Manga", (1, 10), (2, 20));

        await new NovelProgressService(fixture.Db).SaveProgressAsync(
            ReaderA,
            novel.ChapterIds[1],
            420,
            "ja",
            0,
            0,
            CancellationToken.None);
        await fixture.Books().SaveProgressAsync(
            ReaderA,
            book.WorkId,
            book.ChapterIds[2],
            300,
            "en",
            CancellationToken.None);
        var mangaRepository = new MangaRepository(fixture.Db);
        var chapter = await mangaRepository.GetChapterAsync(manga.ChapterIds[1], CancellationToken.None);
        await mangaRepository.SaveProgressAsync(ReaderA, chapter!, 7, CancellationToken.None);

        var items = (await fixture.Query().GetAsync(ReaderA)).ToDictionary(x => x.WorkId);
        Assert.AreEqual(3, items.Count);

        var novelLibrary = await new NovelCatalogQueries(fixture.Db).GetLibraryAsync(ReaderA, CancellationToken.None);
        foreach (var work in novelLibrary.Where(x => x.HasProgress))
        {
            var item = items[work.Id];
            Assert.AreEqual(work.CurrentChapterId, item.ChapterId);
            Assert.AreEqual(work.ProgressPermille / 10, item.ProgressPercent);
            Assert.AreEqual(DateTime.SpecifyKind(work.LastReadAt!.Value, DateTimeKind.Utc), item.LastReadAt);
        }

        Assert.AreEqual($"/Novels/Read/{novel.ChapterIds[1]}", items[novel.WorkId].ResumeUrl);
        Assert.AreEqual(ContinueReadingKind.Book, items[book.WorkId].Kind);
        Assert.AreEqual($"/Books/Read/{book.ChapterIds[2]}?lang=en", items[book.WorkId].ResumeUrl);

        var mangaLibrary = await mangaRepository.GetLibraryAsync(ReaderA, CancellationToken.None);
        var series = mangaLibrary.Single(x => x.HasProgress);
        var mangaItem = items[series.Id];
        Assert.AreEqual(
            $"/Manga/Read/{series.CurrentChapterId}?page={series.CurrentPageIndex}",
            mangaItem.ResumeUrl,
            "Resume URL equals the Manga library's continue link.");
        Assert.AreEqual(series.ProgressPercent, mangaItem.ProgressPercent);
        Assert.AreEqual(series.LastReadAt, mangaItem.LastReadAt);
    }

    [TestMethod]
    public async Task UsesAConstantNumberOfQueries()
    {
        await using var fixture = await ContinueReadingFixture.CreateAsync();

        Assert.AreEqual(2, await fixture.CountQueriesAsync(ReaderA));

        for (var i = 0; i < 20; i++)
        {
            var novel = await fixture.SeedNovelAsync($"Novel {i}", chapters: 2);
            await fixture.SetNovelProgressAsync(ReaderA, novel, 0, 100, Now.AddMinutes(-i));
            var manga = await fixture.SeedMangaAsync($"Manga {i}", (1, 10), (2, 10));
            await fixture.SetMangaProgressAsync(ReaderA, manga, 0, 1, Now.AddMinutes(-i).AddSeconds(-30));
        }

        Assert.AreEqual(2, await fixture.CountQueriesAsync(ReaderA));
        Assert.AreEqual(
            ContinueReadingQuery.DefaultLimit,
            (await fixture.Query().GetAsync(ReaderA)).Count);
    }

    internal sealed record SeededWork(Guid WorkId, IReadOnlyList<Guid> ChapterIds);

    internal sealed record SeededManga(Guid SeriesId, IReadOnlyList<Guid> ChapterIds, IReadOnlyList<int> PageCounts);

    internal sealed class ContinueReadingFixture : IAsyncDisposable
    {
        private readonly string path;

        private ContinueReadingFixture(string path, AppDbContext db)
        {
            this.path = path;
            Db = db;
        }

        public AppDbContext Db { get; }

        public static async Task<ContinueReadingFixture> CreateAsync()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                $"anilingo-continue-reading-{Guid.NewGuid():N}.db");
            var db = new AppDbContext(Options(path));
            await DatabaseMigrationBridge.UpgradeAsync(db);
            return new ContinueReadingFixture(path, db);
        }

        public ContinueReadingQuery Query() => new(Db);

        public async Task<int> CountQueriesAsync(string profileId)
        {
            var counter = new QueryCounter();
            await using var db = new AppDbContext(Options(path, counter));
            await new ContinueReadingQuery(db).GetAsync(profileId);
            return counter.Count;
        }

        public Task<SeededWork> SeedNovelAsync(string title, int chapters, Guid? id = null) =>
            SeedWorkAsync(title, "syosetu", chapters, id);

        public Task<SeededWork> SeedBookAsync(string title, int chapters, Guid? id = null) =>
            SeedWorkAsync(title, BookCatalogService.ImportedBookProvider, chapters, id);

        public async Task SetNovelProgressAsync(
            string profileId,
            SeededWork work,
            int chapterIndex,
            int permille,
            DateTime updatedAt,
            string anchorLanguage = "ja")
        {
            Db.NovelProgress.Add(new NovelProgress
            {
                ProfileId = profileId,
                WorkId = work.WorkId,
                ChapterId = work.ChapterIds[chapterIndex],
                PositionPermille = permille,
                AnchorLanguage = anchorLanguage,
                UpdatedAt = updatedAt
            });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public Task<SeededManga> SeedMangaAsync(string title, params (double Number, int Pages)[] chapters) =>
            SeedMangaAsync(title, Guid.NewGuid(), chapters);

        public async Task<SeededManga> SeedMangaAsync(
            string title,
            Guid seriesId,
            params (double Number, int Pages)[] chapters)
        {
            var repository = new MangaRepository(Db);
            await repository.UpsertSeriesAsync(seriesId, title, $"/manga/{seriesId:N}", CancellationToken.None);
            var seeded = new SeededManga(seriesId, [], []);
            foreach (var (number, pages) in chapters)
            {
                seeded = await AddMangaChapterAsync(seeded, number, pages);
            }

            return seeded;
        }

        public async Task<SeededManga> AddMangaChapterAsync(SeededManga series, double number, int pages)
        {
            var chapterId = Guid.NewGuid();
            await new MangaRepository(Db).UpsertChapterAsync(
                new MangaChapterItem(
                    chapterId,
                    series.SeriesId,
                    number,
                    null,
                    $"Chapter {number}",
                    pages,
                    "folder",
                    Now),
                $"/manga/{series.SeriesId:N}/{number}",
                CancellationToken.None);
            return series with
            {
                ChapterIds = [.. series.ChapterIds, chapterId],
                PageCounts = [.. series.PageCounts, pages]
            };
        }

        /// <summary>
        /// Writes through <see cref="MangaRepository.SaveProgressAsync"/> and
        /// then pins the timestamp for deterministic ordering.
        /// </summary>
        public async Task SetMangaProgressAsync(
            string profileId,
            SeededManga series,
            int chapterIndex,
            int pageIndex,
            DateTime updatedAt)
        {
            var repository = new MangaRepository(Db);
            var chapter = await repository.GetChapterAsync(series.ChapterIds[chapterIndex], CancellationToken.None)
                ?? throw new InvalidOperationException("Seeded manga chapter missing.");
            await repository.SaveProgressAsync(profileId, chapter, pageIndex, CancellationToken.None);
            await Db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "MangaProgress" SET "UpdatedAt" = {updatedAt.ToString("O")}
                WHERE "ProfileId" = {profileId} AND "SeriesId" = {series.SeriesId.ToString()};
                """);
        }

        public BookCatalogService Books() =>
            new(
                new HttpClient(new NoNetworkHandler()),
                Db,
                new NoopBookTranslator(),
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Books:Translation:MemoryPath"] = Path.Combine(
                            Path.GetTempPath(),
                            "anilingo-continue-reading-tests",
                            Guid.NewGuid().ToString("N"))
                    })
                    .Build());

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }

        private async Task<SeededWork> SeedWorkAsync(string title, string provider, int chapters, Guid? id)
        {
            var work = new NovelWork
            {
                Id = id ?? Guid.NewGuid(),
                SourceProvider = provider,
                SourceKey = Guid.NewGuid().ToString("N"),
                SourceUrl = "https://example.invalid/work",
                Title = title
            };
            var chapterRows = Enumerable.Range(1, chapters)
                .Select(number => new NovelChapter
                {
                    WorkId = work.Id,
                    Number = number,
                    Title = $"Chapter {number}",
                    SourceUrl = $"https://example.invalid/work/{number}",
                    OriginalText = $"第{number}章の本文です。",
                    SourceHash = $"hash-{number}"
                })
                .ToArray();

            Db.NovelWorks.Add(work);
            Db.NovelChapters.AddRange(chapterRows);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return new SeededWork(work.Id, chapterRows.Select(x => x.Id).ToArray());
        }

        private static DbContextOptions<AppDbContext> Options(string path, QueryCounter? counter = null)
        {
            var builder = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={path};Foreign Keys=True");
            if (counter is not null)
            {
                builder.AddInterceptors(counter);
            }

            return builder.Options;
        }
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Count++;
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result)
        {
            Count++;
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class NoopBookTranslator : IBookTranslator
    {
        public string Id => "noop-continue-reading";

        public Task<string> TranslateLiteraryAsync(
            string sourceText,
            string sourceLanguage,
            string targetLanguage,
            string context,
            CancellationToken cancellationToken) =>
            Task.FromResult(sourceText);
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("Continue Reading must not call external services.");
    }
}
