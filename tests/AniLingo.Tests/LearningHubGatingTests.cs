using System.Security.Claims;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Ai;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Statistics;
using AniLingo.Web.Features.Vocabulary;
using AniLingo.Web.Pages.Learn;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using KanaIndexModel = AniLingo.Web.Pages.Kana.IndexModel;

namespace AniLingo.Tests;

/// <summary>
/// Verifies #230/#232: Learning hub modules and module pages follow the
/// canonical resolver only. Off shows nothing, Language Tools shows no
/// cards/SRS, Study shows the modules, historic rows never re-open a module.
/// </summary>
[TestClass]
public sealed class LearningHubGatingTests
{
    private const string Profile = "hub-user";

    [TestMethod]
    public async Task OffHidesEveryModuleEvenWithDueReviews()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddDueLearningTermAsync();

        var hub = fixture.Hub();
        await hub.OnGetAsync(CancellationToken.None);

        Assert.IsFalse(hub.LearningEnabled);
        Assert.AreEqual(LearningMode.Off, hub.Mode);
        Assert.IsFalse(hub.ShowAnyModule);
        Assert.IsFalse(hub.ShowMetrics);
        Assert.IsFalse(hub.LanguageToolsOnly);
        Assert.AreEqual(0, hub.DueReviews, "Off must not count due reviews.");
        Assert.AreEqual(0, hub.LearningTerms);
    }

    [TestMethod]
    public async Task LanguageToolsShowsNoCardsOrSrsModules()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetModeAsync(LearningMode.LanguageTools);
        await fixture.AddDueLearningTermAsync();

        var hub = fixture.Hub();
        await hub.OnGetAsync(CancellationToken.None);

        Assert.IsTrue(hub.LearningEnabled);
        Assert.AreEqual(LearningMode.LanguageTools, hub.Mode);
        Assert.IsFalse(hub.ShowReviews);
        Assert.IsFalse(hub.ShowVocabulary);
        Assert.IsFalse(hub.ShowSentences);
        Assert.IsFalse(hub.ShowKana);
        Assert.IsFalse(hub.ShowProgress);
        Assert.IsTrue(hub.LanguageToolsOnly);
        Assert.AreEqual(0, hub.DueReviews);
    }

    [TestMethod]
    public async Task StudyShowsEveryModuleAndCountsDueReviews()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetModeAsync(LearningMode.Study);
        await fixture.AddDueLearningTermAsync();

        var hub = fixture.Hub();
        await hub.OnGetAsync(CancellationToken.None);

        Assert.IsTrue(hub.ShowReviews);
        Assert.IsTrue(hub.ShowVocabulary);
        Assert.IsTrue(hub.ShowSentences);
        Assert.IsTrue(hub.ShowKana);
        Assert.IsTrue(hub.ShowProgress);
        Assert.IsTrue(hub.ShowMetrics);
        Assert.IsFalse(hub.LanguageToolsOnly);
        Assert.AreEqual(1, hub.DueReviews);
        Assert.AreEqual(1, hub.LearningTerms);
    }

    [TestMethod]
    public async Task CustomVocabularyWithoutReviewsKeepsSavingButHidesSrs()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetModeAsync(LearningMode.Custom);
        await fixture.SetCapabilityAsync(LearningCapability.Vocabulary, true);
        var term = await fixture.AddTermAsync();

        var hub = fixture.Hub();
        await hub.OnGetAsync(CancellationToken.None);
        Assert.IsTrue(hub.ShowVocabulary);
        Assert.IsFalse(hub.ShowReviews);
        Assert.IsTrue(hub.ShowMetrics);

        var vocabulary = fixture.Vocabulary();
        var page = await vocabulary.OnGetAsync(null, null, CancellationToken.None);
        Assert.IsInstanceOfType<PageResult>(page);
        Assert.IsFalse(vocabulary.ShowReviewActions);

        Assert.IsInstanceOfType<RedirectToPageResult>(
            await vocabulary.OnPostSaveAsync(term.Id, CancellationToken.None));
        var saved = await fixture.UserTermAsync(term.Id);
        Assert.AreEqual(UserTermState.Saved, saved.State);
        Assert.IsNull(saved.NextReviewAt, "Saving must not schedule a review.");

        Assert.IsInstanceOfType<ForbidResult>(
            await vocabulary.OnPostLearnAsync(term.Id, CancellationToken.None),
            "Learning transition requires the Reviews capability.");
        Assert.IsInstanceOfType<ForbidResult>(
            await vocabulary.OnPostSuspendAsync(term.Id, CancellationToken.None));
        Assert.AreEqual(UserTermState.Saved, (await fixture.UserTermAsync(term.Id)).State);
    }

    [TestMethod]
    public async Task StudyVocabularyLifecycleFollowsCanonicalTransitions()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetModeAsync(LearningMode.Study);
        var term = await fixture.AddTermAsync();
        var vocabulary = fixture.Vocabulary();

        await vocabulary.OnPostSaveAsync(term.Id, CancellationToken.None);
        Assert.AreEqual(UserTermState.Saved, (await fixture.UserTermAsync(term.Id)).State);

        await vocabulary.OnPostLearnAsync(term.Id, CancellationToken.None);
        var learning = await fixture.UserTermAsync(term.Id);
        Assert.AreEqual(UserTermState.Learning, learning.State);
        Assert.IsNull(learning.NextReviewAt, "Queued, not due, until the daily limit admits it.");
        Assert.IsNotNull(learning.QueuePosition);

        await vocabulary.OnPostSuspendAsync(term.Id, CancellationToken.None);
        Assert.AreEqual(UserTermState.Suspended, (await fixture.UserTermAsync(term.Id)).State);

        await vocabulary.OnPostIgnoreAsync(term.Id, CancellationToken.None);
        var ignored = await fixture.UserTermAsync(term.Id);
        Assert.AreEqual(UserTermState.Ignored, ignored.State);
        Assert.IsNull(ignored.QueuePosition);

        await vocabulary.OnPostKnownAsync(term.Id, CancellationToken.None);
        Assert.AreEqual(UserTermState.Known, (await fixture.UserTermAsync(term.Id)).State);

        Assert.IsTrue(vocabulary.TempData["Status"] is string status && status.Contains(term.Canonical));
    }

    [TestMethod]
    public async Task ModulePagesRedirectToHubWhenLearningIsOff()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddDueLearningTermAsync();

        AssertHubRedirect(await fixture.Vocabulary().OnGetAsync(null, null, CancellationToken.None));
        AssertHubRedirect(await fixture.Review().OnGetAsync(CancellationToken.None));
        AssertHubRedirect(await fixture.Progress().OnGetAsync(CancellationToken.None));
        AssertHubRedirect(await fixture.Kana().OnGetAsync(null, 1, CancellationToken.None));

        Assert.AreEqual(
            0,
            await fixture.Db.Terms.CountAsync(x => x.Language == "ja-kana"),
            "Kana must not seed its catalog when the trainer is off.");
    }

    [TestMethod]
    public async Task ModulePagesRedirectToHubInLanguageToolsMode()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetModeAsync(LearningMode.LanguageTools);
        var term = await fixture.AddDueLearningTermAsync();

        AssertHubRedirect(await fixture.Vocabulary().OnGetAsync(null, null, CancellationToken.None));
        AssertHubRedirect(await fixture.Review().OnGetAsync(CancellationToken.None));
        AssertHubRedirect(await fixture.Progress().OnGetAsync(CancellationToken.None));
        AssertHubRedirect(await fixture.Kana().OnGetAsync(null, 1, CancellationToken.None));

        Assert.IsInstanceOfType<ForbidResult>(
            await fixture.Review().OnPostReviewAsync(term.Id, ReviewRating.Good, CancellationToken.None));
        Assert.IsInstanceOfType<ForbidResult>(
            await fixture.Vocabulary().OnPostSaveAsync(term.Id, CancellationToken.None));
    }

    [TestMethod]
    public async Task StudyModulePagesRender()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetModeAsync(LearningMode.Study);
        await fixture.AddDueLearningTermAsync();

        Assert.IsInstanceOfType<PageResult>(
            await fixture.Vocabulary().OnGetAsync(null, null, CancellationToken.None));
        Assert.IsInstanceOfType<PageResult>(await fixture.Review().OnGetAsync(CancellationToken.None));
        Assert.IsInstanceOfType<PageResult>(await fixture.Progress().OnGetAsync(CancellationToken.None));
        Assert.IsInstanceOfType<PageResult>(await fixture.Kana().OnGetAsync(null, 1, CancellationToken.None));
    }

    [TestMethod]
    public async Task EpisodeScopeResolvesNoLearningSurfacesWhenOff()
    {
        await using var fixture = await Fixture.CreateAsync();
        var store = new LearningConfigurationStore(fixture.Db);
        var context = new LearningScopeContext(
            LearningMediaType.Anime,
            WorkKey: "anime-1",
            ContentKey: "episode-1");

        var off = await store.ResolveAsync(Profile, context, CancellationToken.None);
        Assert.IsFalse(off.IsEnabled(LearningCapability.ContentMetrics));
        Assert.IsFalse(off.IsEnabled(LearningCapability.PreparationSuggestions));
        Assert.IsFalse(off.IsEnabled(LearningCapability.Vocabulary));
        Assert.IsFalse(off.IsEnabled(LearningCapability.PlayerTools));
        Assert.IsFalse(off.HasAnyVisibleLearning);

        await fixture.SetModeAsync(LearningMode.LanguageTools);
        var tools = await store.ResolveAsync(Profile, context, CancellationToken.None);
        Assert.IsTrue(tools.IsEnabled(LearningCapability.LanguageLookup));
        Assert.IsTrue(tools.IsEnabled(LearningCapability.PlayerTools));
        Assert.IsFalse(tools.IsEnabled(LearningCapability.Vocabulary));
        Assert.IsFalse(tools.IsEnabled(LearningCapability.Reviews));
        Assert.IsFalse(tools.IsEnabled(LearningCapability.ContentMetrics));
    }

    [TestMethod]
    public void EpisodePageConsumesResolverForLearningSurfaces()
    {
        // Read-only check of #277's gating: the Episode page resolves its scope
        // through the canonical store and wraps learning UI in those flags.
        var pages = Path.Combine(RepositoryRoot(), "src", "AniLingo.Web", "Pages", "Library");
        var model = File.ReadAllText(Path.Combine(pages, "Episode.cshtml.cs"));
        var view = File.ReadAllText(Path.Combine(pages, "Episode.cshtml"));

        StringAssert.Contains(model, "new LearningConfigurationStore(db).ResolveAsync(");
        StringAssert.Contains(model, "LearningMediaType.Anime");
        StringAssert.Contains(model, "LearningSettings.IsEnabled(LearningCapability.ContentMetrics)");
        StringAssert.Contains(model, "LearningSettings.IsEnabled(LearningCapability.PreparationSuggestions)");
        StringAssert.Contains(model, "LearningSettings.IsEnabled(LearningCapability.Vocabulary)");
        StringAssert.Contains(model, "LearningSettings.IsEnabled(LearningCapability.PlayerTools)");

        StringAssert.Contains(view, "@if (Model.ShowContentMetrics)");
        StringAssert.Contains(view, "@if (Model.ShowPreparationSuggestions)");
        StringAssert.Contains(view, "@if (Model.ShowVocabularyTools)");
        Assert.IsFalse(
            view.Contains("href=\"/Learn", StringComparison.Ordinal),
            "Episode must not link into the Learning hub directly.");
    }

    private static void AssertHubRedirect(IActionResult result)
    {
        var redirect = Assert.IsInstanceOfType<RedirectToPageResult>(result);
        Assert.AreEqual("/Learn/Index", redirect.PageName);
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

        private Fixture(string directory, AppDbContext db)
        {
            this.directory = directory;
            Db = db;
            Account = CreateAccount(Profile);
            Learning = new LearningService(db, new FsrsReviewScheduler(), Account);
        }

        public AppDbContext Db { get; }
        public CurrentAccountContext Account { get; }
        public LearningService Learning { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"anilingo-learning-hub-gating-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(
                    $"Data Source={Path.Combine(directory, "anilingo.db")};Foreign Keys=True")
                .Options;

            var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);
            return new Fixture(directory, db);
        }

        public IndexModel Hub() => new(Db, Account);

        public VocabularyModel Vocabulary() => WithTempData(new VocabularyModel(Db, Learning, Account));

        public ReviewModel Review() =>
            WithTempData(new ReviewModel(
                Db,
                Learning,
                new AiSentenceExplanationService(Db, new StubExplainer()),
                Account));

        public ProgressModel Progress() =>
            new(Db, new LearningStatisticsService(Db), Account);

        public KanaIndexModel Kana() => WithTempData(new KanaIndexModel(Db, Learning, Account));

        public Task SetModeAsync(LearningMode mode) =>
            new LearningConfigurationStore(Db).SetModeAsync(
                Profile,
                LearningScopeRef.Profile,
                mode,
                CancellationToken.None);

        public Task SetCapabilityAsync(LearningCapability capability, bool? enabled) =>
            new LearningConfigurationStore(Db).SetCapabilityOverrideAsync(
                Profile,
                LearningScopeRef.Profile,
                capability,
                enabled,
                CancellationToken.None);

        public async Task<Term> AddTermAsync()
        {
            var term = new Term
            {
                Language = "ja",
                Canonical = "学ぶ",
                Reading = "まなぶ",
                Meaning = "learn"
            };
            Db.Terms.Add(term);
            await Db.SaveChangesAsync();
            return term;
        }

        public async Task<Term> AddDueLearningTermAsync()
        {
            var term = await AddTermAsync();
            Db.UserTerms.Add(new UserTerm
            {
                ProfileId = Profile,
                TermId = term.Id,
                State = UserTermState.Learning,
                LearningStartedAt = DateTime.UtcNow.AddDays(-2),
                NextReviewAt = DateTime.UtcNow.AddHours(-1),
                UpdatedAt = DateTime.UtcNow
            });
            await Db.SaveChangesAsync();
            return term;
        }

        public Task<UserTerm> UserTermAsync(Guid termId) =>
            Db.UserTerms
                .AsNoTracking()
                .SingleAsync(x => x.ProfileId == Profile && x.TermId == termId);

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static T WithTempData<T>(T page) where T : PageModel
        {
            page.TempData = new TempDataDictionary(
                new DefaultHttpContext(),
                new StubTempDataProvider());
            return page;
        }

        private static CurrentAccountContext CreateAccount(string profileId)
        {
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, profileId)],
                        "test"))
            };

            return new CurrentAccountContext(
                new FixedHttpContextAccessor { HttpContext = httpContext });
        }
    }

    private sealed class FixedHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class StubTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    private sealed class StubExplainer : IAiSentenceExplainer
    {
        public string Id => "stub";

        public Task<AiSentenceExplanation> ExplainSentenceAsync(
            AiSentenceExplainRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiSentenceExplanation(request.Sentence, [], [], false));
    }
}
