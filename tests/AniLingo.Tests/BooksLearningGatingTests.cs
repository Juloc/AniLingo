using AniLingo.Web.Data;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Novels;
using AniLingo.Web.Infrastructure;
using AniLingo.Web.Pages.Books;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AniLingo.Tests;

/// <summary>
/// #230: the Books reader's whole-chapter AI translation (the DE/EN "Translate
/// this chapter" flow) must resolve the Translation capability through the
/// canonical hierarchy instead of always being available. Follows the pattern
/// in NovelLearningTests/HomePageLearningGatingTests.
/// </summary>
[TestClass]
public sealed class BooksLearningGatingTests
{
    private const string Profile = "books-learner";

    [TestMethod]
    public async Task OffResolvesTranslationDisabledAndRefusesTheHandlers()
    {
        await using var fixture = await Fixture.CreateAsync();

        var reader = fixture.CreateReadModel(Profile);
        await reader.OnGetAsync(fixture.ChapterId, "de", null, null, CancellationToken.None);
        Assert.IsFalse(reader.TranslationEnabled, "Off must not offer chapter translation.");

        var postResult = await fixture.CreateReadModel(Profile).OnPostTranslateAsync(
            fixture.ChapterId,
            "de",
            CancellationToken.None);
        Assert.IsInstanceOfType(postResult, typeof(ForbidResult));

        var statusResult = await fixture.CreateReadModel(Profile).OnGetTranslationStatusAsync(
            fixture.ChapterId,
            "de",
            CancellationToken.None);
        Assert.IsInstanceOfType(statusResult, typeof(ForbidResult));
    }

    [TestMethod]
    public async Task LanguageToolsAndStudyEnableTranslationByDefault()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.SetModeAsync(Profile, LearningMode.LanguageTools);
        var languageTools = fixture.CreateReadModel(Profile);
        await languageTools.OnGetAsync(fixture.ChapterId, "de", null, null, CancellationToken.None);
        Assert.IsTrue(languageTools.TranslationEnabled, "Language Tools offers translation by default.");

        await fixture.SetModeAsync(Profile, LearningMode.Study);
        var study = fixture.CreateReadModel(Profile);
        await study.OnGetAsync(fixture.ChapterId, "de", null, null, CancellationToken.None);
        Assert.IsTrue(study.TranslationEnabled, "Study offers translation by default.");
    }

    [TestMethod]
    public async Task CustomRespectsTheTranslationCapability()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetModeAsync(Profile, LearningMode.Custom);

        var withoutOverride = fixture.CreateReadModel(Profile);
        await withoutOverride.OnGetAsync(fixture.ChapterId, "de", null, null, CancellationToken.None);
        Assert.IsFalse(
            withoutOverride.TranslationEnabled,
            "Custom starts with every capability off until the profile opts in.");

        await fixture.SetCapabilityAsync(Profile, LearningCapability.Translation, true);
        var withOverride = fixture.CreateReadModel(Profile);
        await withOverride.OnGetAsync(fixture.ChapterId, "de", null, null, CancellationToken.None);
        Assert.IsTrue(withOverride.TranslationEnabled);
    }

    [TestMethod]
    public async Task WorkLevelOverrideEnablesTranslationOnlyForThatBook()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var otherFixture = await Fixture.CreateAsync();

        // Global Off, but this one book is switched to Study.
        await new LearningConfigurationStore(fixture.Db).SetModeAsync(
            Profile,
            LearningScopeRef.ForWork(LearningMediaType.Book, fixture.WorkId.ToString()),
            LearningMode.Study,
            CancellationToken.None);

        var overridden = fixture.CreateReadModel(Profile);
        await overridden.OnGetAsync(fixture.ChapterId, "de", null, null, CancellationToken.None);
        Assert.IsTrue(overridden.TranslationEnabled, "The overridden work must resolve Study.");

        var unrelated = otherFixture.CreateReadModel(Profile);
        await unrelated.OnGetAsync(otherFixture.ChapterId, "de", null, null, CancellationToken.None);
        Assert.IsFalse(
            unrelated.TranslationEnabled,
            "A different book on the same (globally Off) profile must not inherit the override.");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string directory;
        private readonly ServiceProvider services;

        private Fixture(string directory, ServiceProvider services, AppDbContext db, Guid workId, Guid chapterId)
        {
            this.directory = directory;
            this.services = services;
            Db = db;
            WorkId = workId;
            ChapterId = chapterId;
        }

        public AppDbContext Db { get; }
        public Guid WorkId { get; }
        public Guid ChapterId { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"anilingo-books-gating-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var connectionString = $"Data Source={Path.Combine(directory, "anilingo.db")};Foreign Keys=True";

            var collection = new ServiceCollection();
            collection.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
            var services = collection.BuildServiceProvider();

            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connectionString)
                .Options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var work = new NovelWork
            {
                SourceProvider = BookCatalogService.ImportedBookProvider,
                SourceKey = Guid.NewGuid().ToString("N"),
                SourceUrl = "upload://books-gating-test.epub",
                Title = "Gating Test Book",
                Format = "EPUB:en"
            };
            var volume = new NovelVolume
            {
                WorkId = work.Id,
                Number = 1,
                Kind = NovelVolumeKinds.Book,
                SourceKey = "book"
            };
            var chapter = new NovelChapter
            {
                WorkId = work.Id,
                VolumeId = volume.Id,
                Number = 1,
                Title = "Chapter One",
                SourceUrl = "book://books-gating-test/1",
                OriginalText = "Hello world.",
                SourceHash = "hash-1"
            };

            db.AddRange(work, volume, chapter);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            return new Fixture(directory, services, db, work.Id, chapter.Id);
        }

        public Task SetModeAsync(string profileId, LearningMode mode) =>
            new LearningConfigurationStore(Db).SetModeAsync(
                profileId,
                LearningScopeRef.Profile,
                mode,
                CancellationToken.None);

        public Task SetCapabilityAsync(string profileId, LearningCapability capability, bool enabled) =>
            new LearningConfigurationStore(Db).SetCapabilityOverrideAsync(
                profileId,
                LearningScopeRef.Profile,
                capability,
                enabled,
                CancellationToken.None);

        public ReadModel CreateReadModel(string profileId) =>
            new(
                NewBookCatalogService(),
                TestAccounts.Context(profileId),
                new BackgroundJobQueue(services.GetRequiredService<IServiceScopeFactory>()),
                Db);

        private BookCatalogService NewBookCatalogService()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Books:Translation:MemoryPath"] = Path.Combine(directory, "translation-memory")
                })
                .Build();

            return new BookCatalogService(new HttpClient(), Db, new UnusedTranslator(), config);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await services.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private sealed class UnusedTranslator : IBookTranslator
        {
            public string Id => "unused";

            public Task<string> TranslateLiteraryAsync(
                string sourceText,
                string sourceLanguage,
                string targetLanguage,
                string context,
                CancellationToken cancellationToken) =>
                throw new InvalidOperationException("AI translation is not expected in these gating tests.");
        }
    }
}
