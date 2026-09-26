using AniLingo.Web.Data;
using AniLingo.Web.Features.Ai;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Learning.Courses;
using AniLingo.Web.Features.Learning.LanguageAssistance;
using AniLingo.Web.Features.Novels;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Features.Vocabulary;
using AniLingo.Web.Infrastructure;
using AniLingo.Web.Pages.Learn;
using AniLingo.Web.Pages.Novels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AniLingo.Tests;

/// <summary>
/// Novel reader learning integration (issue #147, novel-reader part of #233):
/// hosting the shared language inspector, the selection menu's "Look up"
/// action, no duplicate vocabulary across sources, the review round trip and
/// the furigana reader preference.
/// </summary>
[TestClass]
public sealed class NovelLearningTests
{
    private const string Profile = "novel-learner";

    // ---- static wiring: Pages/Novels/Read.cshtml ---------------------------

    [TestMethod]
    public void ReadPageHostsTheSharedInspectorWithoutASecondSelectionSurface()
    {
        var view = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src", "AniLingo.Web", "Pages", "Novels", "Read.cshtml"));

        // The call ends right after the language argument: the host factory's
        // selectionSurface/paragraphAttribute parameters are left at their
        // defaults, so the inspector partial never renders its own floating
        // "Look up" button (config.selection stays null in
        // language-inspector.js) and the reader's own selection menu is the
        // only text-selection UI.
        StringAssert.Contains(
            view,
            ".ForNovelChapter(Model.Chapter.WorkId, Model.Chapter.Id, \"ja\");");
    }

    [TestMethod]
    public void SelectionMenuOffersLookUpAndLearningScriptLoadsBeforeTheBootstrap()
    {
        var view = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src", "AniLingo.Web", "Pages", "Novels", "Read.cshtml"));

        StringAssert.Contains(view, "data-language-lookup-selection");

        var learningScriptIndex = view.IndexOf("~/js/novel-learning.js", StringComparison.Ordinal);
        var bootstrapScriptIndex = view.IndexOf("~/js/novel-reader.js", StringComparison.Ordinal);
        Assert.IsTrue(learningScriptIndex >= 0 && bootstrapScriptIndex >= 0);
        Assert.IsTrue(
            learningScriptIndex < bootstrapScriptIndex,
            "novel-learning.js must register its module before novel-reader.js starts every module.");
    }

    [TestMethod]
    public void NovelReaderBootstrapStartsTheLearningModule()
    {
        var script = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src", "AniLingo.Web", "wwwroot", "js", "novel-reader.js"));

        StringAssert.Contains(script, "modules.learning?.(reader)");
    }

    [TestMethod]
    public void BothViewAlignsJapaneseAndGermanByOneSharedParagraphSegmentNotTwoColumns()
    {
        // #147/#233: the Both view must align by normalized paragraph/segment
        // index rather than two unrelated scroll columns. Each reader segment
        // already carries both languages for the same paragraph index, and
        // novels.css lays the pair out as one CSS grid row per segment.
        var view = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src", "AniLingo.Web", "Pages", "Novels", "Read.cshtml"));
        var css = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src", "AniLingo.Web", "wwwroot", "css", "novels.css"));

        StringAssert.Contains(view, "data-reader-segment=\"@index\"");
        StringAssert.Contains(css, ".novel-reader-shell[data-view=\"both\"] .novel-reader-segment {");
        StringAssert.Contains(css, "display: grid;");
    }

    // ---- resolver gating: LearningModuleResolver for the Novel surface -----

    [TestMethod]
    public async Task NovelSurfaceShowsTheInspectorOnlyWhenReaderToolsResolvesOn()
    {
        await using var fixture = await LanguageInspectorFixture.CreateAsync();
        var context = new LearningScopeContext(
            LearningMediaType.Novel,
            WorkKey: fixture.WorkId.ToString(),
            ContentKey: fixture.ChapterId.ToString());
        var resolver = new LearningModuleResolver(fixture.Db);

        var off = await resolver.ResolveAssistanceAsync(
            LanguageInspectorFixture.Profile,
            context,
            LanguageSourceType.Novel,
            CancellationToken.None);
        Assert.IsFalse(off.ShowInspector, "Learning off must not render the inspector.");

        await fixture.SetProfileModeAsync(LearningMode.LanguageTools);
        var tools = await resolver.ResolveAssistanceAsync(
            LanguageInspectorFixture.Profile,
            context,
            LanguageSourceType.Novel,
            CancellationToken.None);
        Assert.IsTrue(tools.SurfaceTools, "Language Tools enables ReaderTools by default.");
        Assert.IsTrue(tools.ShowInspector);

        await fixture.SetProfileCapabilityAsync(LearningCapability.ReaderTools, false);
        var readerToolsOff = await resolver.ResolveAssistanceAsync(
            LanguageInspectorFixture.Profile,
            context,
            LanguageSourceType.Novel,
            CancellationToken.None);
        Assert.IsTrue(readerToolsOff.Any, "Lookup is still on.");
        Assert.IsFalse(readerToolsOff.SurfaceTools);
        Assert.IsFalse(
            readerToolsOff.ShowInspector,
            "Without ReaderTools the novel reader must not render the inspector, even with lookup on.");
    }

    // ---- one canonical vocabulary path: no duplicate term across sources --

    [TestMethod]
    public async Task SavingAWordFromTheNovelReaderReusesTheSameCardAnAnimeSaveWould()
    {
        await using var fixture = await LanguageInspectorFixture.CreateAsync();
        await fixture.SetProfileModeAsync(LearningMode.Study);

        var novelContext = new LanguageInspectContext(
            "ja",
            "novel",
            fixture.ChapterId.ToString(),
            Sentence: "本の猫。",
            Paragraph: 0);

        var saved = await fixture.Inspector().SetStateAsync(
            new LanguageWordStateRequest("猫", "saved", novelContext),
            CancellationToken.None);
        Assert.AreEqual("saved", saved.State);
        Assert.IsTrue(saved.ContextRecorded);

        var animeContext = fixture.AnimeContext(0, "猫がいる。");
        var savedAgain = await fixture.Inspector().SetStateAsync(
            new LanguageWordStateRequest("猫", "known", animeContext),
            CancellationToken.None);
        Assert.AreEqual("known", savedAgain.State);

        // One catalog Term, one Recognition card: subtitles, readers and
        // Vocabulary all see the same state, regardless of where it was saved.
        Assert.AreEqual(
            1,
            await fixture.Db.Terms.CountAsync(x => x.Language == "ja" && x.Canonical == "猫"));
        var card = await fixture.WordCardAsync("猫");
        Assert.AreEqual(UserTermState.Known, card.State);
    }

    [TestMethod]
    public async Task SavingFromTheNovelReaderRecordsAChapterAndParagraphAnchor()
    {
        await using var fixture = await LanguageInspectorFixture.CreateAsync();
        await fixture.SetProfileModeAsync(LearningMode.Study);

        var novelContext = new LanguageInspectContext(
            "ja",
            "novel",
            fixture.ChapterId.ToString(),
            Sentence: "本の猫。",
            Paragraph: 0);

        await fixture.Inspector().SetStateAsync(
            new LanguageWordStateRequest("猫", "saved", novelContext),
            CancellationToken.None);

        var recorded = await fixture.Db.LearningContexts
            .SingleAsync(x => x.ProfileId == LanguageInspectorFixture.Profile && x.SourceType == "novel");
        Assert.AreEqual($"chapter:{fixture.ChapterId:D}", recorded.SourceKey);
        Assert.AreEqual("paragraph:0", recorded.PositionKey);
        Assert.AreEqual("本の猫。", recorded.Text);
    }

    // ---- review round trip: LearningContext -> Learn/Review -> Novels/Read -

    [TestMethod]
    public async Task ReviewPageLinksBackToTheExactChapterAndParagraphAndTheReaderJumpsThere()
    {
        await using var fixture = await NovelLearningFixture.CreateAsync();
        await fixture.SetModeAsync(Profile, LearningMode.Study);

        var term = new Term { Language = "ja", Canonical = "猫" };
        fixture.Db.Terms.Add(term);
        await fixture.Db.SaveChangesAsync();

        await LearningTestData.SeedTermCardAsync(
            fixture.Db,
            Profile,
            term,
            UserTermState.Learning,
            nextReviewAt: DateTime.UtcNow.AddDays(-1));

        var unitId = await fixture.Db.LearningUnits
            .Where(x => x.TermId == term.Id)
            .Select(x => x.Id)
            .SingleAsync();

        fixture.Db.LearningContexts.Add(new LearningContext
        {
            ProfileId = Profile,
            UnitId = unitId,
            SourceType = "novel",
            SourceKey = $"chapter:{fixture.ChapterId:D}",
            PositionKey = "paragraph:1",
            LanguageTag = "ja",
            Text = "二番目の段落。"
        });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var review = fixture.CreateReviewModel(Profile);
        await review.OnGetAsync(CancellationToken.None);

        Assert.IsNotNull(review.Current);
        Assert.IsNull(review.Current!.Context, "This card has no anime context.");
        Assert.IsNotNull(review.SourceLink, "The novel source link must be resolved instead.");
        var expectedUrl = $"/Novels/Read/{fixture.ChapterId}?paragraph=1&lang=ja";
        Assert.AreEqual(expectedUrl, review.SourceLink!.Url);
        Assert.AreEqual("二番目の段落。", review.SourceLink.Sentence);

        // Following the link back must land on the same paragraph.
        var reader = fixture.CreateReadModel(Profile);
        await reader.OnGetAsync(
            fixture.ChapterId,
            bookmark: null,
            highlight: null,
            paragraph: 1,
            lang: "ja",
            prepare: null,
            CancellationToken.None);

        Assert.IsTrue(reader.InitialAnchor.Forced);
        Assert.AreEqual(1, reader.InitialAnchor.ParagraphIndex);
        Assert.AreEqual("ja", reader.InitialAnchor.Language);
    }

    // ---- furigana: off by default, gated by toolkit, computed on demand ----

    [TestMethod]
    public async Task FuriganaDefaultsOffAndComputesReadingsOnlyForKanjiRunsWithoutNativeRuby()
    {
        await using var fixture = await NovelLearningFixture.CreateAsync();

        var reader = fixture.CreateReadModel(Profile);
        await reader.OnGetAsync(
            fixture.ChapterId, null, null, null, null, null, CancellationToken.None);

        // Off by default: the reader's own bounded GET never computes it, and
        // the toggle button only appears when the toolkit supports readings.
        Assert.IsFalse(reader.ReaderSettings.FuriganaEnabled, "Furigana must default to off.");
        Assert.IsTrue(reader.FuriganaSupported, "Japanese toolkit supports readings.");

        var segments = reader.BuildFuriganaSegments([new NovelInlineRun("本の猫。")]);
        Assert.AreEqual(4, segments.Count);
        Assert.AreEqual("本", segments[0].Text);
        Assert.AreEqual("ほん", segments[0].Reading);
        Assert.AreEqual("の", segments[1].Text);
        Assert.IsNull(segments[1].Reading, "Kana carries no computed reading.");
        Assert.AreEqual("猫", segments[2].Text);
        Assert.AreEqual("ねこ", segments[2].Reading);
        Assert.AreEqual("。", segments[3].Text);
        Assert.IsNull(segments[3].Reading);

        // Native EPUB ruby already shows its own reading and must not be
        // double-annotated.
        var alreadyAnnotated = reader.BuildFuriganaSegments([new NovelInlineRun("本", "もと")]).Single();
        Assert.AreEqual("本", alreadyAnnotated.Text);
        Assert.IsNull(alreadyAnnotated.Reading);

        // The endpoint behind the client-side toggle (never called during the
        // reader's own GET) returns segments per paragraph on demand.
        var furiganaResult = await reader.OnGetFuriganaAsync(fixture.ChapterId, CancellationToken.None) as JsonResult;
        Assert.IsNotNull(furiganaResult);
        var paragraphs = (IReadOnlyDictionary<int, IReadOnlyList<FuriganaSegment>>)furiganaResult.Value!
            .GetType().GetProperty("paragraphs")!.GetValue(furiganaResult.Value)!;
        Assert.AreEqual("ねこ", paragraphs[0].Single(x => x.Text == "猫").Reading);
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

    /// <summary>Small self-contained Novel + Learning fixture for the furigana and review round-trip tests.</summary>
    private sealed class NovelLearningFixture : IAsyncDisposable
    {
        private readonly string directory;
        private readonly ServiceProvider services;

        private NovelLearningFixture(string directory, ServiceProvider services, AppDbContext db, Guid workId, Guid chapterId)
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

        public static async Task<NovelLearningFixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"anilingo-novel-learning-{Guid.NewGuid():N}");
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
                SourceUrl = "https://example.invalid/novel-learning",
                Title = "Furigana Novel"
            };
            var volume = new NovelVolume { WorkId = work.Id, Number = 1, SourceKey = "web" };
            var chapter = new NovelChapter
            {
                WorkId = work.Id,
                VolumeId = volume.Id,
                Number = 1,
                SourceUrl = "https://example.invalid/novel-learning/1",
                Title = "Chapter One",
                OriginalText = "本の猫。\n\n二番目の段落。",
                SourceHash = "hash"
            };

            db.AddRange(work, volume, chapter);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            return new NovelLearningFixture(directory, services, db, work.Id, chapter.Id);
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

        public ReviewModel CreateReviewModel(string profileId) =>
            new(
                Db,
                LearningTestData.Service(Db, profileId),
                new AiSentenceExplanationService(Db, new UnusedExplainer()),
                new NovelCatalogQueries(Db),
                TestAccounts.Context(profileId));

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
                throw new InvalidOperationException("Translation is not expected in these tests.");
        }

        private sealed class UnusedMappingSuggester : INovelMappingSuggester
        {
            public string Id => "unused";

            public Task<IReadOnlyList<NovelMappingSuggestion>> SuggestMappingsAsync(
                NovelMappingSuggestionRequest request,
                CancellationToken cancellationToken) =>
                throw new InvalidOperationException("Mapping suggestions are not expected in these tests.");
        }

        private sealed class UnusedExplainer : IAiSentenceExplainer
        {
            public string Id => "unused";

            public Task<AiSentenceExplanation> ExplainSentenceAsync(
                AiSentenceExplainRequest request,
                CancellationToken cancellationToken) =>
                throw new InvalidOperationException("AI explanation is not expected in these tests.");
        }
    }
}
