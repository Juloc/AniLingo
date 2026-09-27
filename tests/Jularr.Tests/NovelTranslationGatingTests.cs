using System.Text.Json;
using Jularr.Web.Data;
using Jularr.Web.Features.Learning;
using Jularr.Web.Features.Learning.LanguageAssistance;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.Operations;
using Jularr.Web.Features.Vocabulary;
using Jularr.Web.Infrastructure;
using Jularr.Web.Pages.Novels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jularr.Tests;

/// <summary>
/// #230/#369: the novel reader's whole-chapter German AI *translation
/// generation* must resolve the Translation capability through the canonical
/// hierarchy (profile → Novel media type → work → chapter). Reading an
/// already cached German translation, however, is core reader behaviour and
/// must always work regardless of that capability (#369 fixed a regression
/// where #230 withheld cached text while Learning was off). Follows the
/// pattern in NovelLearningTests/HomePageLearningGatingTests.
/// </summary>
[TestClass]
public sealed class NovelTranslationGatingTests
{
    private const string Profile = "novel-translation-learner";

    [TestMethod]
    public async Task OffStillExposesCachedGermanTextAndTheStatusHandlerReturnsReady()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedCachedGermanTranslationAsync();

        var reader = fixture.CreateReadModel(Profile);
        await reader.OnGetAsync(
            fixture.ChapterId, null, null, null, null, null, CancellationToken.None);

        Assert.IsFalse(
            reader.TranslationEnabled,
            "Off must not offer *generating* a new chapter translation.");
        Assert.AreEqual(
            1,
            reader.GermanParagraphs.Count,
            "#369: reading an already cached German translation is core reader " +
            "behaviour and must not depend on the Learning Translation capability.");

        var status = await fixture.CreateReadModel(Profile)
            .OnGetTranslationStatusAsync(fixture.ChapterId, CancellationToken.None);
        var json = JsonDocument.Parse(JsonSerializer.Serialize(((JsonResult)status).Value));
        Assert.AreEqual("ready", json.RootElement.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task OffStillExposesCachedTranslateGemmaTextWithoutConfiguredGenerator()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedCachedGermanTranslationAsync(
            NovelTranslationProviders.TranslateGemmaPrefix + "cached:model");

        var reader = fixture.CreateReadModel(Profile);
        await reader.OnGetAsync(
            fixture.ChapterId, null, null, null, null, null, CancellationToken.None);

        Assert.IsFalse(reader.TranslationEnabled);
        Assert.IsFalse(reader.TranslateGemmaConfigured);
        Assert.IsTrue(reader.TranslateGemmaAvailable, "Cached local text keeps its controls.");
        Assert.AreEqual(0, reader.GermanParagraphs.Count);
        Assert.AreEqual(1, reader.TranslateGemmaParagraphs.Count);

        var status = await fixture.CreateReadModel(Profile)
            .OnGetTranslateGemmaStatusAsync(fixture.ChapterId, CancellationToken.None);
        var json = JsonDocument.Parse(JsonSerializer.Serialize(((JsonResult)status).Value));
        Assert.AreEqual("ready", json.RootElement.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task UnconfiguredTranslateGemmaWithoutCachedTextRendersNoLocalControls()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedCachedGermanTranslationAsync();

        var reader = fixture.CreateReadModel(Profile);
        await reader.OnGetAsync(
            fixture.ChapterId, null, null, null, null, null, CancellationToken.None);
        Assert.IsFalse(reader.TranslateGemmaAvailable);

        var root = RepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "src", "Jularr.Web", "Pages", "Novels", "Read.cshtml"));
        StringAssert.Contains(
            view,
            "data-translate-gemma-available=\"@Model.TranslateGemmaAvailable.ToString().ToLowerInvariant()\"");

        var script = File.ReadAllText(Path.Combine(root, "src", "Jularr.Web", "wwwroot", "js", "novel-translation.js"));
        StringAssert.Contains(script, "if (!gemmaAvailable) return null;");
        StringAssert.Contains(script, "if (gemmaAvailable) {");
        foreach (var hardCoded in new[] { "Übersetzung", "Beide", "Startet", "Läuft", "nicht eingerichtet" })
        {
            Assert.IsFalse(
                script.Contains(hardCoded, StringComparison.Ordinal),
                $"'{hardCoded}' must come from UiTranslationResources.");
        }
    }

    [TestMethod]
    public async Task LocalTextFromAnOlderModelStaysReadableButIsNotCurrent()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedCachedGermanTranslationAsync(
            NovelTranslationProviders.TranslateGemmaPrefix + "translategemma-4b-it:0123456789ab");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TranslateGemma:Endpoint"] = "http://translategemma:11434/v1/chat/completions"
            })
            .Build();

        var status = await fixture.CreateReadModel(Profile, configuration)
            .OnGetTranslateGemmaStatusAsync(fixture.ChapterId, CancellationToken.None);
        var json = JsonDocument.Parse(JsonSerializer.Serialize(((JsonResult)status).Value));

        Assert.AreEqual("ready", json.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(1, json.RootElement.GetProperty("paragraphs").GetArrayLength());
        Assert.IsFalse(json.RootElement.GetProperty("current").GetBoolean());
        Assert.IsFalse(
            json.RootElement.GetProperty("canRegenerate").GetBoolean(),
            "Only the owner may queue a new local translation, and Learning is off here.");

        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Jularr.Web", "wwwroot", "js", "novel-translation.js"));
        StringAssert.Contains(script, "result.canRegenerate");
        StringAssert.Contains(script, "localRetranslateButton");
    }

    [TestMethod]
    public async Task MalformedTranslateGemmaEndpointIsTreatedAsNotConfigured()
    {
        await using var fixture = await Fixture.CreateAsync();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TranslateGemma:Endpoint"] = "localhost:11434/v1/chat/completions"
            })
            .Build();

        var reader = fixture.CreateReadModel(Profile, configuration);
        await reader.OnGetAsync(
            fixture.ChapterId, null, null, null, null, null, CancellationToken.None);
        Assert.IsFalse(reader.TranslateGemmaConfigured);
        Assert.IsFalse(reader.TranslateGemmaAvailable);

        var status = await fixture.CreateReadModel(Profile, configuration)
            .OnGetTranslateGemmaStatusAsync(fixture.ChapterId, CancellationToken.None);
        var json = JsonDocument.Parse(JsonSerializer.Serialize(((JsonResult)status).Value));
        Assert.AreEqual("unavailable", json.RootElement.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task OffWithoutACachedTranslationStillRefusesTheStatusHandler()
    {
        await using var fixture = await Fixture.CreateAsync();

        var status = await fixture.CreateReadModel(Profile)
            .OnGetTranslationStatusAsync(fixture.ChapterId, CancellationToken.None);
        Assert.IsInstanceOfType(
            status,
            typeof(ForbidResult),
            "Without a cached translation, checking status implies generation, which stays gated.");
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

    [TestMethod]
    public async Task OffStillSupportsOriginalGermanAndBothWhereTranslatedContentExists()
    {
        // Acceptance criteria (#369): "Novel reader still supports Original /
        // German / Both where translated content exists" even with Learning
        // Off. JapaneseParagraphs/GermanParagraphs are what the reader view
        // renders both languages from, and the view's language switch only
        // disables German/Both when there is no cached text (see the source
        // assertion below), not on the resolved capability.
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedCachedGermanTranslationAsync();

        var reader = fixture.CreateReadModel(Profile);
        await reader.OnGetAsync(
            fixture.ChapterId, null, null, null, null, null, CancellationToken.None);

        Assert.IsFalse(reader.TranslationEnabled);
        Assert.IsTrue(reader.JapaneseParagraphs.Count > 0, "Original must always be readable.");
        Assert.IsTrue(reader.GermanParagraphs.Count > 0, "German must be readable once cached, even with Learning off.");
    }

    [TestMethod]
    public void ReaderViewDisablesTheLanguageSwitchOnCachedContentNotOnTranslationEnabled()
    {
        var view = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Jularr.Web", "Pages", "Novels", "Read.cshtml"));

        StringAssert.Contains(
            view,
            "var hasTranslation = Model.GermanParagraphs.Count > 0;",
            "#369: the language switch (Original/German/Both) must be driven by the cache, not the capability.");

        // The reader frame's language menu: the Original/German/Both choices come
        // before the translate slot, whose generate form is gated separately.
        var switchIndex = view.IndexOf("data-reader-menu=\"language\"", StringComparison.Ordinal);
        Assert.IsTrue(switchIndex > 0, "Language menu markup not found.");
        var switchMarkupEnd = view.IndexOf("data-translation-slot", switchIndex, StringComparison.Ordinal);
        Assert.IsTrue(switchMarkupEnd > switchIndex, "Translate slot not found inside the language menu.");
        var switchMarkup = view[switchIndex..switchMarkupEnd];
        StringAssert.Contains(switchMarkup, "data-reader-view=\"de\" hidden=\"@(!hasTranslation)\"");
        Assert.IsFalse(
            switchMarkup.Contains("TranslationEnabled", StringComparison.Ordinal),
            "The language switch itself must not reference the resolved Learning capability.");
    }

    // ---- Novels/Work (the per-chapter "DE" badge). #369: unlike the
    // reader's *generation* gate, the badge only reflects whether a chapter
    // already has a cached translation - it is core reader status, not a
    // Learning affordance, so the Work page no longer resolves the
    // Translation capability at all.

    [TestMethod]
    public void WorkViewShowsTheTranslatedBadgeFromTheCacheAlone()
    {
        var view = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Jularr.Web", "Pages", "Novels", "Work.cshtml"));

        StringAssert.Contains(view, "@if (chapter.HasTranslation)");
        StringAssert.Contains(
            view,
            "chapter.HasTranslation",
            "The badge must not resolve the Learning Translation capability (#369).");
        Assert.IsFalse(
            view.Contains("Model.TranslationEnabled", StringComparison.Ordinal),
            "The Work page must not gate the cached-translation badge behind Learning (#369).");
    }

    [TestMethod]
    public void WorkPageDoesNotResolveTheTranslationCapability()
    {
        // The Work page model has no TranslationEnabled property to resolve;
        // this documents the intent alongside the reader/library gating tests.
        var properties = typeof(WorkModel).GetProperties();
        Assert.IsFalse(
            properties.Any(p => p.Name == "TranslationEnabled"),
            "#369: the Work page's chapter badge must not depend on a resolved Learning capability.");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Jularr.sln")))
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
            var directory = Path.Combine(Path.GetTempPath(), $"jularr-novel-translation-gating-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "jmdict-ger.tsv"), "");
            await File.WriteAllTextAsync(Path.Combine(directory, "jmdict-eng-common.tsv"), "");
            var connectionString = $"Data Source={Path.Combine(directory, "jularr.db")};Foreign Keys=True";

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

        /// <summary>Seeds a cached German translation directly so the reader has content to read.</summary>
        public async Task SeedCachedGermanTranslationAsync(string providerId = "unused")
        {
            var chapter = await Db.NovelChapters.SingleAsync(x => x.Id == ChapterId);
            Db.NovelTranslations.Add(new NovelTranslation
            {
                ChapterId = ChapterId,
                TargetLanguage = NovelTranslationProviders.IsTranslateGemma(providerId)
                    ? NovelReadingLanguage.GermanTranslateGemma
                    : NovelReadingLanguage.German,
                ProviderId = providerId,
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

        public ReadModel CreateReadModel(string profileId, IConfiguration? configuration = null)
        {
            var imports = new NovelImportService(Db, []);
            return AttachPageContext(new ReadModel(
                new NovelCatalogQueries(Db),
                new NovelAnnotationService(Db),
                new NovelProgressService(Db),
                imports,
                new NovelTranslationService(Db, imports, new UnusedTranslator(), configuration: configuration),
                new NovelMappingService(Db, new UnusedMappingSuggester()),
                new NovelJobs(new BackgroundJobQueue(services.GetRequiredService<IServiceScopeFactory>())),
                new LanguageTextAnalyzer(
                    new LanguageInspectorFixture.FakeMorphology(),
                    new JapaneseDictionary(directory)),
                Db,
                TestAccounts.Context(profileId),
                new OperationRunner(Db, services)));
        }

        /// <summary>
        /// The Ui bundle (localization issue #185) is resolved from
        /// <see cref="PageModel.HttpContext"/>, so a page model built for a
        /// direct handler call (not through the MVC pipeline) needs a bare
        /// PageContext for that property to be non-null.
        /// </summary>
        private static TPage AttachPageContext<TPage>(TPage page)
            where TPage : PageModel
        {
            var httpContext = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection()
                    .AddSingleton<IModelMetadataProvider, EmptyModelMetadataProvider>()
                    .BuildServiceProvider()
            };
            page.PageContext = new PageContext
            {
                HttpContext = httpContext,
                ViewData = new ViewDataDictionary<TPage>(
                    new EmptyModelMetadataProvider(),
                    new ModelStateDictionary())
            };
            page.TempData = new TempDataDictionary(httpContext, new NoTempDataProvider());
            return page;
        }

        private sealed class NoTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) =>
                new Dictionary<string, object>();

            public void SaveTempData(HttpContext context, IDictionary<string, object> values)
            {
            }
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
