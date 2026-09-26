using System.Data.Common;
using System.Text.Json;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Novels;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Infrastructure;
using AniLingo.Web.Pages.Novels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace AniLingo.Tests;

[TestClass]
public sealed class NovelReaderQueryTests
{
    private const string ReaderA = "reader-a";
    private const string ReaderB = "reader-b";

    [TestMethod]
    public async Task ReaderInitialLoadQueryCountDoesNotGrowWithChaptersOrAnnotations()
    {
        using var fixture = await NovelFixture.CreateAsync();

        var small = await fixture.SeedWorkAsync("small", chapterCount: 3);
        var large = await fixture.SeedWorkAsync("large", chapterCount: 1000);
        var smallChapter = small.Chapters[1];
        var largeChapter = large.Chapters[500];

        await fixture.SeedAnnotationsAsync(large, largeChapter, ReaderA, perChapterElsewhere: 400, inCurrent: 5);

        var smallQueries = await fixture.CountQueriesAsync(model =>
            model.OnGetAsync(smallChapter.Id, null, null, null, CancellationToken.None));
        var (largeQueries, largeModel) = await fixture.CountQueriesWithModelAsync(model =>
            model.OnGetAsync(largeChapter.Id, null, null, null, CancellationToken.None));

        Assert.AreEqual(smallQueries, largeQueries);
        Assert.IsTrue(largeQueries <= 10, $"Reader GET used {largeQueries} queries.");

        Assert.AreEqual(5, largeModel.Annotations.Bookmarks.Count);
        Assert.AreEqual(5, largeModel.Annotations.Highlights.Count);
        Assert.IsTrue(largeModel.Annotations.Bookmarks.All(x => x.ChapterId == largeChapter.Id));
        Assert.IsTrue(largeModel.Annotations.Highlights.All(x => x.ChapterId == largeChapter.Id));
        Assert.AreEqual(400, largeModel.Annotations.OtherChapterBookmarkCount);
        Assert.AreEqual(400, largeModel.Annotations.OtherChapterHighlightCount);
        Assert.AreEqual(large.Chapters[499].Id, largeModel.Chapter.PreviousChapterId);
        Assert.AreEqual(large.Chapters[501].Id, largeModel.Chapter.NextChapterId);
    }

    [TestMethod]
    public async Task ChapterWindowIsBoundedAndPagesBySearchAndNumber()
    {
        using var fixture = await NovelFixture.CreateAsync();
        var work = await fixture.SeedWorkAsync("long", chapterCount: 1000);
        var catalog = new NovelCatalogQueries(fixture.Db);

        var window = await catalog.GetChapterWindowAsync(
            work.Id, 500, null, null, null, 100, CancellationToken.None);

        Assert.AreEqual(100, window.Items.Count);
        Assert.IsTrue(window.HasBefore);
        Assert.IsTrue(window.HasAfter);
        Assert.AreEqual(475, window.Items[0].Number);
        Assert.IsTrue(window.Items.Any(x => x.Number == 500));
        CollectionAssert.AreEqual(
            window.Items.Select(x => x.Number).OrderBy(x => x).ToArray(),
            window.Items.Select(x => x.Number).ToArray());

        var next = await catalog.GetChapterWindowAsync(
            work.Id, 500, null, window.Items[^1].Number, null, 100, CancellationToken.None);
        Assert.AreEqual(window.Items[^1].Number + 1, next.Items[0].Number);
        Assert.AreEqual(100, next.Items.Count);

        var previous = await catalog.GetChapterWindowAsync(
            work.Id, 500, null, null, 3, 100, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { 1, 2 }, previous.Items.Select(x => x.Number).ToArray());
        Assert.IsFalse(previous.HasBefore);

        var start = await catalog.GetChapterWindowAsync(
            work.Id, 1, null, null, null, 100, CancellationToken.None);
        Assert.AreEqual(1, start.Items[0].Number);
        Assert.AreEqual(100, start.Items.Count);
        Assert.IsFalse(start.HasBefore);

        var byNumber = await catalog.GetChapterWindowAsync(
            work.Id, 500, "777", null, null, 100, CancellationToken.None);
        Assert.IsTrue(byNumber.Items.Any(x => x.Number == 777));
        Assert.IsTrue(byNumber.Items.All(x => x.Number == 777 || x.Title.Contains("777")));

        var byTitle = await catalog.GetChapterWindowAsync(
            work.Id, 500, "Title 12", null, null, 100, CancellationToken.None);
        Assert.IsTrue(byTitle.Items.Count > 0);
        Assert.IsTrue(byTitle.Items.All(x => x.Title.Contains("Title 12")));

        var wildcard = await catalog.GetChapterWindowAsync(
            work.Id, 500, "%", null, null, 100, CancellationToken.None);
        Assert.AreEqual(0, wildcard.Items.Count);
    }

    [TestMethod]
    public async Task ChapterNotDownloadedGetRendersPreparationWithoutProviderCall()
    {
        using var fixture = await NovelFixture.CreateAsync();
        var work = await fixture.SeedWorkAsync("remote", chapterCount: 2, withContent: false);
        var chapter = work.Chapters[0];

        var model = fixture.CreateReadModel(ReaderA);
        var result = await model.OnGetAsync(chapter.Id, null, null, null, CancellationToken.None);

        Assert.IsInstanceOfType<PageResult>(result);
        Assert.IsFalse(model.Chapter.HasContent);
        Assert.IsNull(model.Preparation);
        Assert.AreEqual(0, fixture.Source.Calls);

        var prepare = await fixture.CreateReadModel(ReaderA)
            .OnPostPrepareChapterAsync(chapter.Id, null, null, CancellationToken.None);
        var redirect = (RedirectToPageResult)prepare;
        var operationId = (Guid)redirect.RouteValues!["prepare"]!;
        Assert.AreEqual(0, fixture.Source.Calls);

        var operation = await new OperationStore(fixture.Db).GetAsync(operationId);
        Assert.IsNotNull(operation);
        Assert.AreEqual(NovelJobs.ChapterDownloadKind, operation.Kind);
        Assert.AreEqual(ReaderA, operation.ProfileId);
        Assert.IsTrue(operation.IsActive);

        var pending = fixture.CreateReadModel(ReaderA);
        await pending.OnGetAsync(chapter.Id, null, null, operationId, CancellationToken.None);
        Assert.IsNotNull(pending.Preparation);
        Assert.AreEqual(0, fixture.Source.Calls);

        // Another profile cannot observe someone else's preparation operation.
        var other = fixture.CreateReadModel(ReaderB);
        await other.OnGetAsync(chapter.Id, null, null, operationId, CancellationToken.None);
        Assert.IsNull(other.Preparation);

        var status = await fixture.CreateReadModel(ReaderA)
            .OnGetChapterStatusAsync(chapter.Id, operationId, CancellationToken.None);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(((JsonResult)status).Value));
        Assert.IsFalse(json.RootElement.GetProperty("ready").GetBoolean());
        Assert.AreEqual("queued", json.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(0, fixture.Source.Calls);
    }

    [TestMethod]
    public async Task ResumeAnchorsUseCurrentLanguageLayoutAndJumpTargets()
    {
        using var fixture = await NovelFixture.CreateAsync();
        var work = await fixture.SeedWorkAsync("anchors", chapterCount: 2);
        var chapter = work.Chapters[0];
        await fixture.AddTranslationAsync(chapter, "Erster Absatz.\n\nZweiter deutscher Absatz.");

        var progress = new NovelProgressService(fixture.Db);
        await progress.SaveProgressAsync(ReaderA, chapter.Id, 600, "de", 1, 7, CancellationToken.None);

        var saved = await progress.GetProgressAsync(ReaderA, work.Id, CancellationToken.None);
        Assert.IsNotNull(saved);
        Assert.AreEqual("de", saved.AnchorLanguage);
        Assert.AreEqual(1, saved.AnchorParagraphIndex);
        Assert.AreEqual(7, saved.AnchorOffset);
        Assert.AreEqual("Zweiter deutscher Absatz.", saved.AnchorText);

        // A stale paragraph index keeps the position but drops the paragraph anchor.
        await progress.SaveProgressAsync(ReaderB, chapter.Id, 300, "ja", 99, 3, CancellationToken.None);
        var stale = await progress.GetProgressAsync(ReaderB, work.Id, CancellationToken.None);
        Assert.IsNotNull(stale);
        Assert.IsNull(stale.AnchorParagraphIndex);
        Assert.IsNull(stale.AnchorText);
        Assert.AreEqual(300, stale.PositionPermille);

        var resume = fixture.CreateReadModel(ReaderA);
        await resume.OnGetAsync(chapter.Id, null, null, null, CancellationToken.None);
        Assert.AreEqual(new NovelReaderAnchor("de", 1, 7, 600, "Zweiter deutscher Absatz.", false), resume.InitialAnchor);

        var annotations = new NovelAnnotationService(fixture.Db);
        var bookmark = await annotations.AddBookmarkAsync(
            ReaderA, chapter.Id, 150, "ja", 0, 2, null, null, null, CancellationToken.None);
        var highlight = await annotations.AddHighlightAsync(
            ReaderA, chapter.Id, "ja", 1, 3, 6, null, CancellationToken.None);

        var bookmarkJump = fixture.CreateReadModel(ReaderA);
        await bookmarkJump.OnGetAsync(chapter.Id, bookmark.Id, null, null, CancellationToken.None);
        Assert.AreEqual(new NovelReaderAnchor("ja", 0, 2, 150, bookmark.AnchorText, true), bookmarkJump.InitialAnchor);

        var highlightJump = fixture.CreateReadModel(ReaderA);
        await highlightJump.OnGetAsync(chapter.Id, null, highlight.Id, null, CancellationToken.None);
        Assert.AreEqual("ja", highlightJump.InitialAnchor.Language);
        Assert.AreEqual(1, highlightJump.InitialAnchor.ParagraphIndex);
        Assert.AreEqual(3, highlightJump.InitialAnchor.Offset);
        Assert.IsTrue(highlightJump.InitialAnchor.Forced);

        // Another profile's bookmark id is not a valid jump target.
        var foreign = fixture.CreateReadModel(ReaderB);
        await foreign.OnGetAsync(chapter.Id, bookmark.Id, null, null, CancellationToken.None);
        Assert.IsFalse(foreign.InitialAnchor.Forced);
        Assert.AreEqual(300, foreign.InitialAnchor.PositionPermille);
    }

    [TestMethod]
    public async Task HighlightValidationAllowsOverlapAndRejectsInvalidRanges()
    {
        using var fixture = await NovelFixture.CreateAsync();
        var work = await fixture.SeedWorkAsync("highlights", chapterCount: 1);
        var chapter = work.Chapters[0];
        var service = new NovelAnnotationService(fixture.Db);

        var first = await service.AddHighlightAsync(ReaderA, chapter.Id, "ja", 0, 0, 5, null, CancellationToken.None);
        var overlapping = await service.AddHighlightAsync(ReaderA, chapter.Id, "ja", 0, 3, 8, null, CancellationToken.None);
        var nested = await service.AddHighlightAsync(ReaderA, chapter.Id, "ja", 0, 1, 2, "inner", CancellationToken.None);
        var duplicate = await service.AddHighlightAsync(ReaderA, chapter.Id, "ja", 0, 0, 5, "later note", CancellationToken.None);

        Assert.AreNotEqual(first.Id, overlapping.Id);
        Assert.AreNotEqual(first.Id, nested.Id);
        Assert.AreEqual(first.Id, duplicate.Id);
        Assert.AreEqual("later note", duplicate.Note);
        Assert.AreEqual(3, await fixture.Db.NovelHighlights.CountAsync());

        var paragraph = NovelTextLayout.SplitParagraphs(chapter.OriginalText)[0];
        Assert.AreEqual(paragraph[3..8], overlapping.Text);

        // Offsets are clamped to the paragraph; the same range for another profile is separate.
        var clamped = await service.AddHighlightAsync(ReaderB, chapter.Id, "ja", 0, -4, 5, null, CancellationToken.None);
        Assert.AreEqual(0, clamped.StartOffset);
        Assert.AreNotEqual(first.Id, clamped.Id);

        await AssertRejectedAsync(() => service.AddHighlightAsync(ReaderA, chapter.Id, "ja", 7, 0, 3, null, CancellationToken.None));
        await AssertRejectedAsync(() => service.AddHighlightAsync(ReaderA, chapter.Id, "ja", 0, 4, 4, null, CancellationToken.None));
        await AssertRejectedAsync(() => service.AddHighlightAsync(ReaderA, chapter.Id, "ja", 0, 6, 2, null, CancellationToken.None));
        // German has no current translation yet, so no German paragraph can be highlighted.
        await AssertRejectedAsync(() => service.AddHighlightAsync(ReaderA, chapter.Id, "de", 0, 0, 3, null, CancellationToken.None));
        await AssertRejectedAsync(() => service.AddHighlightAsync(ReaderA, Guid.NewGuid(), "ja", 0, 0, 3, null, CancellationToken.None));

        var longWork = await fixture.SeedWorkAsync("long-paragraph", chapterCount: 1, text: new string('あ', 2500));
        await AssertRejectedAsync(() => service.AddHighlightAsync(
            ReaderA, longWork.Chapters[0].Id, "ja", 0, 0, 2001, null, CancellationToken.None));
        var maximal = await service.AddHighlightAsync(
            ReaderA, longWork.Chapters[0].Id, "ja", 0, 0, 2000, null, CancellationToken.None);
        Assert.AreEqual(2000, maximal.Text.Length);
    }

    [TestMethod]
    public async Task WorkNotesArePagedProfileScopedAndExcludeCurrentChapter()
    {
        using var fixture = await NovelFixture.CreateAsync();
        var work = await fixture.SeedWorkAsync("notes", chapterCount: 10);
        var current = work.Chapters[4];
        await fixture.SeedAnnotationsAsync(work, current, ReaderA, perChapterElsewhere: 45, inCurrent: 3);
        var service = new NovelAnnotationService(fixture.Db);

        var first = await service.GetWorkNotesAsync(
            ReaderA, work.Id, current.Id, NovelNoteKind.Bookmark, 0, CancellationToken.None);
        Assert.AreEqual(NovelAnnotationService.WorkNotesPageSize, first.Items.Count);
        Assert.IsTrue(first.HasMore);
        Assert.IsTrue(first.Items.All(x => x.ChapterId != current.Id));
        Assert.IsTrue(first.Items.All(x => x.ChapterNumber > 0));

        var second = await service.GetWorkNotesAsync(
            ReaderA, work.Id, current.Id, NovelNoteKind.Bookmark, first.NextOffset, CancellationToken.None);
        Assert.AreEqual(5, second.Items.Count);
        Assert.IsFalse(second.HasMore);
        Assert.AreEqual(0, first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)).Count());

        var highlights = await service.GetWorkNotesAsync(
            ReaderA, work.Id, current.Id, NovelNoteKind.Highlight, 0, CancellationToken.None);
        Assert.AreEqual(NovelAnnotationService.WorkNotesPageSize, highlights.Items.Count);
        Assert.IsTrue(highlights.Items.All(x => !string.IsNullOrEmpty(x.Title)));

        var otherProfile = await service.GetWorkNotesAsync(
            ReaderB, work.Id, current.Id, NovelNoteKind.Bookmark, 0, CancellationToken.None);
        Assert.AreEqual(0, otherProfile.Items.Count);

        var otherAnnotations = await service.GetChapterAnnotationsAsync(
            ReaderB, work.Id, current.Id, CancellationToken.None);
        Assert.AreEqual(0, otherAnnotations.Bookmarks.Count);
        Assert.AreEqual(0, otherAnnotations.OtherChapterBookmarkCount);

        // Removing another profile's annotation is a no-op.
        var victim = first.Items[0].Id;
        await service.RemoveBookmarkAsync(ReaderB, victim, CancellationToken.None);
        Assert.IsTrue(await fixture.Db.NovelBookmarks.AnyAsync(x => x.Id == victim));
        Assert.IsNull(await service.UpdateBookmarkAppearanceAsync(ReaderB, victim, "paper", "#123456", CancellationToken.None));
    }

    [TestMethod]
    public async Task LibraryCountsComeFromBoundedDatabaseAggregates()
    {
        using var fixture = await NovelFixture.CreateAsync();
        var work = await fixture.SeedWorkAsync("counts", chapterCount: 3);

        var chapters = work.Chapters;
        await fixture.AddTranslationAsync(chapters[0], "Aktuelle Übersetzung.");
        await fixture.AddTranslationAsync(chapters[1], "Veraltet.", sourceHash: "OUTDATED");
        await fixture.AddTranslationAsync(chapters[2], "Buch-Prompt.", promptVersion: 5);

        var remote = await fixture.Db.NovelChapters.SingleAsync(x => x.Id == chapters[2].Id);
        remote.OriginalText = "";
        await fixture.Db.SaveChangesAsync();

        for (var index = 0; index < 4; index++)
        {
            await fixture.SeedWorkAsync($"filler-{index}", chapterCount: 20);
        }

        await new NovelProgressService(fixture.Db).SaveProgressAsync(
            ReaderA, chapters[1].Id, 250, "ja", 0, 0, CancellationToken.None);

        var queries = await fixture.CountAsync(db =>
            new NovelCatalogQueries(db).GetLibraryAsync(ReaderA, CancellationToken.None));
        Assert.AreEqual(2, queries);

        var library = await new NovelCatalogQueries(fixture.Db).GetLibraryAsync(ReaderA, CancellationToken.None);
        var item = library.Single(x => x.Id == work.Id);
        Assert.AreEqual(3, item.ChapterCount);
        Assert.AreEqual(2, item.LoadedChapterCount);
        Assert.AreEqual(1, item.TranslatedChapterCount);
        Assert.AreEqual(chapters[1].Id, item.CurrentChapterId);
        Assert.AreEqual(250, item.ProgressPermille);

        var otherProfile = await new NovelCatalogQueries(fixture.Db).GetLibraryAsync(ReaderB, CancellationToken.None);
        Assert.IsFalse(otherProfile.Single(x => x.Id == work.Id).HasProgress);
    }

    private static async Task AssertRejectedAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        Assert.Fail("Expected the annotation to be rejected.");
    }

    private sealed record SeededWork(Guid Id, IReadOnlyList<NovelChapter> Chapters);

    private sealed class NovelFixture : IDisposable
    {
        private readonly string path;
        private readonly ServiceProvider services;

        private NovelFixture(string path, ServiceProvider services, AppDbContext db)
        {
            this.path = path;
            this.services = services;
            Db = db;
        }

        public AppDbContext Db { get; }
        public CountingSourceProvider Source { get; } = new();

        public static async Task<NovelFixture> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"anilingo-novel-reader-{Guid.NewGuid():N}.db");
            var connectionString = $"Data Source={path};Foreign Keys=True";
            var collection = new ServiceCollection();
            collection.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
            var services = collection.BuildServiceProvider();

            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connectionString)
                .Options);
            await DatabaseMigrationBridge.UpgradeAsync(db);
            return new NovelFixture(path, services, db);
        }

        public async Task<SeededWork> SeedWorkAsync(
            string key,
            int chapterCount,
            bool withContent = true,
            string? text = null)
        {
            var work = new NovelWork
            {
                SourceProvider = CountingSourceProvider.ProviderKey,
                SourceKey = key,
                SourceUrl = $"https://example.invalid/{key}",
                Title = key
            };
            var volume = new NovelVolume { WorkId = work.Id, Number = 1, SourceKey = "web" };
            var chapters = Enumerable.Range(1, chapterCount)
                .Select(number =>
                {
                    var body = text ?? $"第{number}章の最初の段落です。\n\n二番目の段落はここにあります。";
                    return new NovelChapter
                    {
                        WorkId = work.Id,
                        VolumeId = volume.Id,
                        Number = number,
                        SourceUrl = $"https://example.invalid/{key}/{number}",
                        Title = $"Title {number}",
                        OriginalText = withContent ? body : "",
                        SourceHash = withContent ? $"HASH-{key}-{number}" : ""
                    };
                })
                .ToList();

            Db.Add(work);
            Db.Add(volume);
            Db.AddRange(chapters);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return new SeededWork(work.Id, chapters);
        }

        public async Task SeedAnnotationsAsync(
            SeededWork work,
            NovelChapter current,
            string profileId,
            int perChapterElsewhere,
            int inCurrent)
        {
            var others = work.Chapters.Where(x => x.Id != current.Id).ToArray();
            var start = DateTime.UtcNow.AddDays(-1);
            for (var index = 0; index < perChapterElsewhere; index++)
            {
                var chapter = others[index % others.Length];
                Db.Add(new NovelBookmark
                {
                    ProfileId = profileId,
                    WorkId = work.Id,
                    ChapterId = chapter.Id,
                    PositionPermille = index % 1000,
                    CreatedAt = start.AddSeconds(index)
                });
                Db.Add(new NovelHighlight
                {
                    ProfileId = profileId,
                    WorkId = work.Id,
                    ChapterId = chapter.Id,
                    ParagraphIndex = 0,
                    StartOffset = 0,
                    EndOffset = 2,
                    Text = "第1",
                    CreatedAt = start.AddSeconds(index)
                });
            }

            for (var index = 0; index < inCurrent; index++)
            {
                Db.Add(new NovelBookmark
                {
                    ProfileId = profileId,
                    WorkId = work.Id,
                    ChapterId = current.Id,
                    PositionPermille = 100 * index
                });
                Db.Add(new NovelHighlight
                {
                    ProfileId = profileId,
                    WorkId = work.Id,
                    ChapterId = current.Id,
                    ParagraphIndex = 0,
                    StartOffset = index,
                    EndOffset = index + 2,
                    Text = "x"
                });
            }

            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task AddTranslationAsync(
            NovelChapter chapter,
            string text,
            string? sourceHash = null,
            int promptVersion = NovelTranslationService.PromptVersion)
        {
            Db.Add(new NovelTranslation
            {
                ChapterId = chapter.Id,
                TargetLanguage = "de",
                ProviderId = $"fake-{promptVersion}-{sourceHash}",
                PromptVersion = promptVersion,
                SourceHash = sourceHash ?? chapter.SourceHash,
                Text = text
            });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public ReadModel CreateReadModel(string profileId) =>
            CreateReadModel(Db, profileId);

        private ReadModel CreateReadModel(AppDbContext db, string profileId)
        {
            var imports = new NovelImportService(db, [Source]);
            return new ReadModel(
                new NovelCatalogQueries(db),
                new NovelAnnotationService(db),
                new NovelProgressService(db),
                imports,
                new NovelTranslationService(db, imports, new UnusedTranslator()),
                new NovelMappingService(db, new UnusedMappingSuggester()),
                new NovelJobs(new BackgroundJobQueue(services.GetRequiredService<IServiceScopeFactory>())),
                db,
                EpisodeFlowFixture.Account(profileId),
                new OperationRunner(db, services));
        }

        public async Task<int> CountQueriesAsync(Func<ReadModel, Task<IActionResult>> action) =>
            (await CountQueriesWithModelAsync(action)).Queries;

        public async Task<(int Queries, ReadModel Model)> CountQueriesWithModelAsync(
            Func<ReadModel, Task<IActionResult>> action)
        {
            var counter = new QueryCounter();
            await using var db = CreateCountingContext(counter);
            var model = CreateReadModel(db, ReaderA);
            await action(model);
            return (counter.Count, model);
        }

        public async Task<int> CountAsync(Func<AppDbContext, Task> action)
        {
            var counter = new QueryCounter();
            await using var db = CreateCountingContext(counter);
            await action(db);
            return counter.Count;
        }

        private AppDbContext CreateCountingContext(QueryCounter counter) =>
            new(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={path};Foreign Keys=True")
                .AddInterceptors(counter)
                .Options);

        public void Dispose()
        {
            Db.Dispose();
            services.Dispose();
            SqliteConnection.ClearAllPools();
            File.Delete(path);
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

    private sealed class CountingSourceProvider : INovelSourceProvider
    {
        public const string ProviderKey = "counting";

        public int Calls { get; private set; }

        public string Key => ProviderKey;

        public bool CanHandle(Uri sourceUri) => true;

        public Task<NovelSourceWorkSnapshot> GetWorkAsync(
            Uri sourceUri,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("The source provider must not be called.");
        }

        public Task<NovelSourceChapterSnapshot> GetChapterAsync(
            Uri sourceUri,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("The source provider must not be called.");
        }
    }

    private sealed class UnusedTranslator : INovelTranslator
    {
        public string Id => "unused";

        public Task<string> TranslateAsync(
            string japaneseText,
            string targetLanguage,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Translation is not expected in reader tests.");
    }

    private sealed class UnusedMappingSuggester : INovelMappingSuggester
    {
        public string Id => "unused";

        public Task<IReadOnlyList<NovelMappingSuggestion>> SuggestMappingsAsync(
            NovelMappingSuggestionRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Mapping suggestions are not expected in reader tests.");
    }
}
