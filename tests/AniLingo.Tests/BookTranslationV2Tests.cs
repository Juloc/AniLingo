using AniLingo.Web.Data;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.Novels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AniLingo.Tests;

[TestClass]
public sealed class BookTranslationV2Tests
{
    [TestMethod]
    public async Task TranslationBiblePersistsMergesAndKeepsLockedTerms()
    {
        var root = TempDirectory();

        try
        {
            var store = new BookTranslationMemoryStore(root);
            var workId = Guid.NewGuid();

            var bible = await store.GetOrCreateAsync(
                workId,
                "en",
                "id",
                _ => Task.FromResult(
                    new BookTranslationBibleSeed(
                        "Third-person limited",
                        "Dry fantasy adventure",
                        "Informal literary",
                        "Young adult",
                        ["Fantasy"],
                        [
                            new BookTranslationEntity(
                                "Alice",
                                "Alice",
                                "character",
                                "Primary viewpoint character",
                                "she/her",
                                null,
                                "Dry and understated")
                        ],
                        [
                            new BookTranslationTerm(
                                "Mana Core",
                                "Inti Mana",
                                "magic",
                                "Keep title case",
                                Locked: true)
                        ])),
                CancellationToken.None);

            await store.ApplyChapterDeltaAsync(
                bible,
                Guid.NewGuid(),
                1,
                "Arrival",
                new BookTranslationMemoryDelta(
                    "Alice arrives at the academy.",
                    "She still distrusts the headmaster.",
                    [
                        new BookTranslationEntity(
                            "Headmaster",
                            "Kepala Akademi",
                            "character",
                            "Leader of the academy",
                            "he/him",
                            "Alice distrusts him",
                            "Formal")
                    ],
                    [
                        new BookTranslationTerm(
                            "Mana Core",
                            "Jantung Mana",
                            "magic",
                            "A later model suggestion must not override the locked choice.",
                            Locked: false),
                        new BookTranslationTerm(
                            "First Circle",
                            "Lingkaran Pertama",
                            "rank",
                            null)
                    ]),
                CancellationToken.None);

            var reloaded = await new BookTranslationMemoryStore(root)
                .LoadAsync(
                    workId,
                    "id",
                    CancellationToken.None);

            Assert.IsNotNull(reloaded);
            Assert.AreEqual(2, reloaded.Entities.Count);
            Assert.AreEqual(2, reloaded.Terms.Count);
            Assert.AreEqual(
                "Inti Mana",
                reloaded.Terms.Single(x => x.Source == "Mana Core").Target);
            Assert.IsTrue(
                reloaded.Terms.Single(x => x.Source == "Mana Core").Locked);
            Assert.AreEqual(1, reloaded.Chapters.Count);

            var context = BookTranslationMemoryStore.RenderContext(reloaded);
            StringAssert.Contains(context, "Third-person limited");
            StringAssert.Contains(context, "Alice → Alice");
            StringAssert.Contains(context, "Mana Core → Inti Mana");
            StringAssert.Contains(context, "Alice arrives at the academy.");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task TranslationPipelineRunsTranslatorEditorQaThenMemoryAndCachesFinalText()
    {
        var databasePath = TempDatabasePath();
        var memoryPath = TempDirectory();

        try
        {
            await using var db = await CreateDatabaseAsync(databasePath);
            var (work, chapter) = await SeedBookAsync(db);

            var translator = new RecordingTranslator();
            using var client = new HttpClient
            {
                BaseAddress = new Uri("https://gutendex.com/")
            };
            var service = NewService(
                db,
                client,
                translator,
                memoryPath);

            var first = await service.TranslateChapterAsync(
                chapter.Id,
                "id",
                CancellationToken.None);
            var second = await service.TranslateChapterAsync(
                chapter.Id,
                "id",
                CancellationToken.None);

            Assert.AreEqual(first.Id, second.Id);
            Assert.AreEqual("FINAL-ID", first.Text);
            Assert.AreEqual(
                BookCatalogService.TranslationPromptVersion,
                first.PromptVersion);

            CollectionAssert.AreEqual(
                new[]
                {
                    "analyze",
                    "translate",
                    "edit",
                    "qa",
                    "memory"
                },
                translator.Events);

            var bible = await new BookTranslationMemoryStore(memoryPath)
                .LoadAsync(
                    work.Id,
                    "id",
                    CancellationToken.None);

            Assert.IsNotNull(bible);
            Assert.AreEqual(1, bible.Chapters.Count);
            Assert.AreEqual(
                "The heroine opens the gate.",
                bible.Chapters[0].Summary);
            Assert.AreEqual(
                "Gerbang Perak",
                bible.Terms.Single(x => x.Source == "Silver Gate").Target);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            DeleteDirectory(memoryPath);
        }
    }

    [TestMethod]
    public async Task FailedQaDoesNotPersistPartialNovelTranslation()
    {
        var databasePath = TempDatabasePath();
        var memoryPath = TempDirectory();

        try
        {
            await using var db = await CreateDatabaseAsync(databasePath);
            var (_, chapter) = await SeedBookAsync(db);

            var translator = new InvalidQaTranslator();
            using var client = new HttpClient
            {
                BaseAddress = new Uri("https://gutendex.com/")
            };
            var service = NewService(
                db,
                client,
                translator,
                memoryPath);

            await AssertThrowsAsync<InvalidOperationException>(
                () => service.TranslateChapterAsync(
                    chapter.Id,
                    "id",
                    CancellationToken.None));

            Assert.AreEqual(
                0,
                await db.NovelTranslations.CountAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            DeleteDirectory(memoryPath);
        }
    }

    private static BookCatalogService NewService(
        AppDbContext db,
        HttpClient client,
        IBookTranslator translator,
        string memoryPath)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Books:Translation:MemoryPath"] = memoryPath
                })
            .Build();

        return new BookCatalogService(
            client,
            db,
            translator,
            config);
    }

    private static async Task<(NovelWork Work, NovelChapter Chapter)> SeedBookAsync(
        AppDbContext db)
    {
        var work = new NovelWork
        {
            SourceProvider = BookCatalogService.ImportedBookProvider,
            SourceKey = "translation-v2",
            SourceUrl = "upload://translation-v2.epub",
            Title = "The Silver Gate",
            Author = "Example Author",
            Description = "A fantasy story about a sealed gate.",
            MetadataTitle = "The Silver Gate",
            MetadataDescription = "A fantasy story about a sealed gate.",
            MetadataGenresJson = "[\"Fantasy\",\"Adventure\"]",
            Format = "EPUB:en",
            MetadataStatus = "IMPORTED"
        };

        var chapter = new NovelChapter
        {
            WorkId = work.Id,
            Number = 1,
            Title = "Opening",
            SourceUrl = "book://translation-v2/1",
            OriginalText = "Alice opened the Silver Gate. The old hinges groaned.",
            SourceHash = "translation-v2-hash"
        };

        db.NovelWorks.Add(work);
        db.NovelChapters.Add(chapter);
        await db.SaveChangesAsync();

        return (work, chapter);
    }

    private static async Task<AppDbContext> CreateDatabaseAsync(string path)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path};Foreign Keys=True")
            .Options;

        var db = new AppDbContext(options);
        await DatabaseMigrationBridge.UpgradeAsync(db);
        return db;
    }

    private static async Task AssertThrowsAsync<TException>(
        Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
            Assert.Fail(
                $"Expected {typeof(TException).Name}.");
        }
        catch (TException)
        {
        }
    }

    private static string TempDatabasePath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"anilingo-book-translation-v2-{Guid.NewGuid():N}.db");

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "anilingo-book-translation-v2",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(
                path,
                recursive: true);
        }
    }

    private sealed class RecordingTranslator : IBookTranslator
    {
        public string Id => "recording-v2";
        public List<string> Events { get; } = [];

        public Task<BookTranslationBibleSeed> AnalyzeBookAsync(
            BookTranslationAnalysisRequest request,
            CancellationToken cancellationToken)
        {
            Events.Add("analyze");
            return Task.FromResult(
                new BookTranslationBibleSeed(
                    "Third person",
                    "Atmospheric fantasy",
                    "Literary",
                    null,
                    ["Fantasy"],
                    [
                        new BookTranslationEntity(
                            "Alice",
                            "Alice",
                            "character",
                            null,
                            "she/her",
                            null,
                            "Reserved")
                    ],
                    []));
        }

        public Task<string> TranslateLiteraryAsync(
            string sourceText,
            string sourceLanguage,
            string targetLanguage,
            string context,
            CancellationToken cancellationToken)
        {
            Events.Add("translate");
            StringAssert.Contains(
                context,
                "BOOK TRANSLATION BIBLE");
            StringAssert.Contains(
                context,
                "Alice → Alice");
            return Task.FromResult("DRAFT-ID");
        }

        public Task<string> EditLiteraryAsync(
            BookLiteraryEditRequest request,
            CancellationToken cancellationToken)
        {
            Events.Add("edit");
            Assert.AreEqual("DRAFT-ID", request.DraftTranslation);
            return Task.FromResult("EDITED-ID");
        }

        public Task<BookTranslationQualityReview> ReviewLiteraryAsync(
            BookTranslationQaRequest request,
            CancellationToken cancellationToken)
        {
            Events.Add("qa");
            Assert.AreEqual("EDITED-ID", request.EditedTranslation);
            return Task.FromResult(
                new BookTranslationQualityReview(
                    false,
                    "FINAL-ID",
                    ["Use the established gate terminology."]));
        }

        public Task<BookTranslationMemoryDelta> ExtractTranslationMemoryAsync(
            BookTranslationMemoryRequest request,
            CancellationToken cancellationToken)
        {
            Events.Add("memory");
            Assert.AreEqual("FINAL-ID", request.FinalTranslation);
            return Task.FromResult(
                new BookTranslationMemoryDelta(
                    "The heroine opens the gate.",
                    "The gate is now open.",
                    [],
                    [
                        new BookTranslationTerm(
                            "Silver Gate",
                            "Gerbang Perak",
                            "place",
                            null)
                    ]));
        }
    }

    private sealed class InvalidQaTranslator : IBookTranslator
    {
        public string Id => "invalid-qa";

        public Task<string> TranslateLiteraryAsync(
            string sourceText,
            string sourceLanguage,
            string targetLanguage,
            string context,
            CancellationToken cancellationToken) =>
            Task.FromResult("draft");

        public Task<string> EditLiteraryAsync(
            BookLiteraryEditRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult("edited");

        public Task<BookTranslationQualityReview> ReviewLiteraryAsync(
            BookTranslationQaRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new BookTranslationQualityReview(
                    false,
                    null,
                    ["Missing sentence."]));
    }
}
