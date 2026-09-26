using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Learning.LanguageAssistance;
using AniLingo.Web.Features.Novels;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Features.Vocabulary;
using AniLingo.Web.Infrastructure;
using AniLingo.Web.Pages.Novels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AniLingo.Tests;

/// <summary>
/// #230: the novel reader's whole-chapter German AI translation must resolve
/// the Translation capability through the canonical hierarchy (profile → Novel
/// media type → work → chapter) instead of always being available once cached.
/// Follows the pattern in NovelLearningTests/HomePageLearningGatingTests.
/// </summary>
[TestClass]
public sealed class NovelTranslationGatingTests
{
    private const string Profile = "novel-translation-learner";

    [TestMethod]
    public async Task OffWithholdsCachedGermanTextAndRefusesTheStatusHandler()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedCachedGermanTranslationAsync();

        var reader = fixture.CreateReadModel(Profile);
        await reader.OnGetAsync(
            fixture.ChapterId, null, null, null, null, null, CancellationToken.None);

        Assert.IsFalse(reader.TranslationEnabled, "Off must not offer chapter translation.");
        Assert.AreEqual(0, reader.GermanParagraphs.Count, "Cached German text must not be exposed while off.");

        var status = await fixture.CreateReadModel(Profile)
            .OnGetTranslationStatusAsync(fixture.ChapterId, CancellationToken.None);
        Assert.IsInstanceOfType(status, typeof(ForbidResult));
    }

    [TestMethod]
    public async Task LanguageToolsAndStudyEnableTranslationByDefault()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedCachedGermanTranslationAsync();

        await fixture.SetModeAsync(Profile, LearningMode.LanguageTools);
        var languageTools = fixture.CreateReadModel(Profile);
        await languageTools.OnGetAsync(
            fixture.ChapterId, null, null, null, null, null, CancellationToken.None);
        Assert.IsTrue(languageTools.TranslationEnabled);
        Assert.AreEqual(1, languageTools.GermanParagraphs.Count);

        await fixture.SetModeAsync(Profile, LearningMode.Study);
        var study = fixture.CreateReadModel(Profile);
        await study.OnGetAsync(
            fixture.ChapterId, null, null, null, null, null, CancellationToken.None);
        Assert.IsTrue(study.TranslationEnabled);
    }

    [TestMethod]
    public async Task CustomRespectsTheTranslationCapability()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetModeAsync(Profile, LearningMode.Custom);

        var withoutOverride = fixture.CreateReadModel(Profile);
        await withoutOverride.OnGetAsync(
            fixture.ChapterId, null, null, null, null, null, CancellationToken.None);
        Assert.IsFalse(withoutOverride.TranslationEnabled);

        await new LearningConfigurationStore(fixture.Db).SetCapabilityOverrideAsync(
            Profile,
            LearningScopeRef.Profile,
            LearningCapability.Translation,
            true,
            CancellationToken.None);

        var withOverride = fixture.CreateReadModel(Profile);
        await withOverride.OnGetAsync(
            fixture.ChapterId, null, null, null, null, null, CancellationToken.None);
        Assert.IsTrue(withOverride.TranslationEnabled);
    }

    [TestMethod]
    public async Task WorkLevelOverrideEnablesTranslationOnlyForThatNovel()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var otherFixture = await Fixture.CreateAsync();

        await new LearningConfigurationStore(fixture.Db).SetModeAsync(
            Profile,
            LearningScopeRef.ForWork(LearningMediaType.Novel, fixture.WorkId.ToString()),
            LearningMode.Study,
            CancellationToken.None);

        var overridden = fixture.CreateReadModel(Profile);
        await overridden.OnGetAsync(
            fixture.ChapterId, null, null, null, null, null, CancellationToken.None);
        Assert.IsTrue(overridden.TranslationEnabled, "The overridden work must resolve Study.");

        var unrelated = otherFixture.CreateReadModel(Profile);
        await unrelated.OnGetAsync(
            otherFixture.ChapterId, null, null, null, null, null, CancellationToken.None);
        Assert.IsFalse(
            unrelated.TranslationEnabled,
            "A different novel on the same (globally Off) profile must not inherit the override.");
    }

    // ---- Novels/Work (the per-chapter "DE" badge) shares the same
    // Translation gate as the reader, through the exact same
    // LearningModuleResolver.ResolveTranslationEnabledAsync call the Work
    // page model makes at Novel/work scope (no content key: the badge is a
    // whole-work affordance, not per-chapter).

    [TestMethod]
    public async Task WorkScopeResolvesTranslationTheSameWayTheWorkPageDoes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var resolver = new LearningModuleResolver(fixture.Db);

        Assert.IsFalse(
            await resolver.ResolveTranslationEnabledAsync(
                Profile, LearningMediaType.Novel, fixture.WorkId.ToString(), null, CancellationToken.None),
            "Off must not offer the whole-work translation badge.");

        await fixture.SetModeAsync(Profile, LearningMode.Study);
        Assert.IsTrue(
            await resolver.ResolveTranslationEnabledAsync(
                Profile, LearningMediaType.Novel, fixture.WorkId.ToString(), null, CancellationToken.None));

        // A work-level override (global Off, one novel set to Study) enables
        // the badge only for that novel, the same override case the Work
        // page's chapter badges must respect.
        await new LearningConfigurationStore(fixture.Db).SetModeAsync(
            Profile, LearningScopeRef.Profile, LearningMode.Off, CancellationToken.None);
        await new LearningConfigurationStore(fixture.Db).SetModeAsync(
            Profile,
            LearningScopeRef.ForWork(LearningMediaType.Novel, fixture.WorkId.ToString()),
            LearningMode.Study,
            CancellationToken.None);

        await using var otherFixture = await Fixture.CreateAsync();
        Assert.IsTrue(
            await resolver.ResolveTranslationEnabledAsync(
                Profile, LearningMediaType.Novel, fixture.WorkId.ToString(), null, CancellationToken.None));
        Assert.IsFalse(
            await new LearningModuleResolver(otherFixture.Db).ResolveTranslationEnabledAsync(
                Profile, LearningMediaType.Novel, otherFixture.WorkId.ToString(), null, CancellationToken.None),
            "A different novel on the same (globally Off) profile must not inherit the override.");
    }

    [TestMethod]
    public void WorkViewGatesTheTranslatedBadgeOnTranslationEnabled()
    {
        var view = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "AniLingo.Web", "Pages", "Novels", "Work.cshtml"));

        StringAssert.Contains(view, "@if (Model.TranslationEnabled && chapter.HasTranslation)");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AniLingo.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
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
            var directory = Path.Combine(Path.GetTempPath(), $"anilingo-novel-translation-gating-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "jmdict-ger.tsv"), "");
            await File.WriteAllTextAsync(Path.Combine(directory, "jmdict-eng-common.tsv"), "");
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
                SourceProvider = "test",
                SourceKey = Guid.NewGuid().ToString("N"),
                SourceUrl = "https://example.invalid/novel-translation-gating",
                Title = "Translation Gating Novel"
            };
            var volume = new NovelVolume { WorkId = work.Id, Number = 1, SourceKey = "web" };
            var chapter = new NovelChapter
            {
                WorkId = work.Id,
                VolumeId = volume.Id,
                Number = 1,
                SourceUrl = "https://example.invalid/novel-translation-gating/1",
                Title = "Chapter One",
                OriginalText = "本の猫。",
                SourceHash = "hash"
            };

            db.AddRange(work, volume, chapter);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            return new Fixture(directory, services, db, work.Id, chapter.Id);
        }

        /// <summary>Seeds a cached German translation directly so the reader has content to withhold.</summary>
        public async Task SeedCachedGermanTranslationAsync()
        {
            var chapter = await Db.NovelChapters.SingleAsync(x => x.Id == ChapterId);
            Db.NovelTranslations.Add(new NovelTranslation
            {
                ChapterId = ChapterId,
                TargetLanguage = NovelReadingLanguage.German,
                ProviderId = "unused",
                PromptVersion = NovelTranslationService.PromptVersion,
                SourceHash = chapter.SourceHash,
                Text = "Die Katze im Buch."
            });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public Task SetModeAsync(string profileId, LearningMode mode) =>
            new LearningConfigurationStore(Db).SetModeAsync(
                profileId,
                LearningScopeRef.Profile,
                mode,
                CancellationToken.None);

        public ReadModel CreateReadModel(string profileId)
        {
            var imports = new NovelImportService(Db, []);
            return new ReadModel(
                new NovelCatalogQueries(Db),
                new NovelAnnotationService(Db),
                new NovelProgressService(Db),
                imports,
                new NovelTranslationService(Db, imports, new UnusedTranslator()),
                new NovelMappingService(Db, new UnusedMappingSuggester()),
                new NovelJobs(new BackgroundJobQueue(services.GetRequiredService<IServiceScopeFactory>())),
                new LanguageTextAnalyzer(
                    new LanguageInspectorFixture.FakeMorphology(),
                    new JapaneseDictionary(directory)),
                Db,
                TestAccounts.Context(profileId),
                new OperationRunner(Db, services));
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

        private sealed class UnusedTranslator : INovelTranslator
        {
            public string Id => "unused";

            public Task<string> TranslateAsync(string japaneseText, string targetLanguage, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("Translation is not expected in these gating tests.");
        }

        private sealed class UnusedMappingSuggester : INovelMappingSuggester
        {
            public string Id => "unused";

            public Task<IReadOnlyList<NovelMappingSuggestion>> SuggestMappingsAsync(
                NovelMappingSuggestionRequest request,
                CancellationToken cancellationToken) =>
                throw new InvalidOperationException("Mapping suggestions are not expected in these gating tests.");
        }
    }
}
