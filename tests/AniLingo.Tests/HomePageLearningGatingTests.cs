using System.Security.Claims;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Vocabulary;
using AniLingo.Web.Pages;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

/// <summary>
/// Verifies #230/#232 on Home: no due-review prompt, no coverage percentages
/// and no learning queries unless the resolved scope opts in. Home leads with
/// Continue Watching.
/// </summary>
[TestClass]
public sealed class HomePageLearningGatingTests
{
    private const string Profile = "home-user";

    [TestMethod]
    public async Task OffShowsNoLearningWidgetsOrCoverage()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedEpisodeWithDueVocabularyAsync();

        var home = fixture.Home();
        await home.OnGetAsync(CancellationToken.None);

        Assert.IsFalse(home.ShowLearningHomeWidget);
        Assert.IsFalse(home.ShowContentMetrics);
        Assert.AreEqual(0, home.DueReviews);
        Assert.AreEqual(1, home.RecentEpisodes.Count);
        Assert.AreEqual(0, home.RecentEpisodes[0].TotalOccurrences, "Coverage must not be loaded.");
        Assert.AreEqual(0, home.RecentEpisodes[0].PreparationPercent);
    }

    [TestMethod]
    public async Task StudyKeepsHomeQuietUntilWidgetsAreOptedIn()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedEpisodeWithDueVocabularyAsync();
        await fixture.SetModeAsync(LearningMode.Study);

        var home = fixture.Home();
        await home.OnGetAsync(CancellationToken.None);

        Assert.IsFalse(home.ShowLearningHomeWidget, "HomeWidget is opt-in even in Study.");
        Assert.IsFalse(home.ShowContentMetrics, "ContentMetrics is opt-in even in Study.");
        Assert.AreEqual(0, home.DueReviews);
        Assert.AreEqual(0, home.RecentEpisodes[0].TotalOccurrences);
    }

    [TestMethod]
    public async Task OptedInWidgetsResolveThroughTheHierarchy()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedEpisodeWithDueVocabularyAsync();
        await fixture.SetModeAsync(LearningMode.Study);
        var store = new LearningConfigurationStore(fixture.Db);
        await store.SetCapabilityOverrideAsync(
            Profile,
            LearningScopeRef.Profile,
            LearningCapability.HomeWidget,
            true,
            CancellationToken.None);
        await store.SetCapabilityOverrideAsync(
            Profile,
            LearningScopeRef.ForMedia(LearningMediaType.Anime),
            LearningCapability.ContentMetrics,
            true,
            CancellationToken.None);

        var home = fixture.Home();
        await home.OnGetAsync(CancellationToken.None);

        Assert.IsTrue(home.ShowLearningHomeWidget);
        Assert.IsTrue(home.ShowContentMetrics);
        Assert.AreEqual(1, home.DueReviews);
        Assert.AreEqual(3, home.RecentEpisodes[0].TotalOccurrences);
        Assert.AreEqual(2, home.RecentEpisodes[0].PreparedOccurrences);
        Assert.AreEqual(66, home.RecentEpisodes[0].PreparationPercent);
    }

    [TestMethod]
    public async Task LanguageToolsShowsNoLearningWidgets()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedEpisodeWithDueVocabularyAsync();
        await fixture.SetModeAsync(LearningMode.LanguageTools);

        var home = fixture.Home();
        await home.OnGetAsync(CancellationToken.None);

        Assert.IsFalse(home.ShowLearningHomeWidget);
        Assert.IsFalse(home.ShowContentMetrics);
        Assert.AreEqual(0, home.DueReviews);
    }

    [TestMethod]
    public void HomeLeadsWithContinueWatchingAndGatesEveryLearningLink()
    {
        var view = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "AniLingo.Web", "Pages", "Index.cshtml"));

        var continueWatching = view.IndexOf("data-continue-watching", StringComparison.Ordinal);
        var metrics = view.IndexOf("class=\"metric-grid\"", StringComparison.Ordinal);
        var library = view.IndexOf("home.library.recent", StringComparison.Ordinal);
        Assert.IsTrue(continueWatching > 0);
        Assert.IsTrue(continueWatching < metrics, "Continue Watching renders before the metric grid.");
        Assert.IsTrue(metrics < library);

        var learningLinks = CountOccurrences(view, "href=\"/Learn");
        var gatedWidgets = CountOccurrences(view, "data-home-learning-widget");
        Assert.IsTrue(learningLinks > 0);
        Assert.AreEqual(learningLinks, gatedWidgets, "Every Learning link on Home is a gated widget.");
        Assert.AreEqual(
            gatedWidgets,
            CountOccurrences(view, "Model.ShowLearningHomeWidget"),
            "Every gated widget checks the resolved HomeWidget capability.");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
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
        private readonly string path;

        private Fixture(string path, AppDbContext db)
        {
            this.path = path;
            Db = db;
        }

        public AppDbContext Db { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                $"anilingo-home-gating-{Guid.NewGuid():N}.db");
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={path};Foreign Keys=True")
                .Options;

            var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);
            return new Fixture(path, db);
        }

        public IndexModel Home() => new(Db, Account());

        public Task SetModeAsync(LearningMode mode) =>
            new LearningConfigurationStore(Db).SetModeAsync(
                Profile,
                LearningScopeRef.Profile,
                mode,
                CancellationToken.None);

        /// <summary>
        /// One episode with three term occurrences; two of them belong to a
        /// due Learning word so coverage resolves to 66% when metrics are on.
        /// </summary>
        public async Task SeedEpisodeWithDueVocabularyAsync()
        {
            var anime = new Anime { Key = "test", Title = "Test" };
            var episode = new Episode
            {
                AnimeId = anime.Id,
                SeasonNumber = 1,
                Number = 1,
                Title = "Episode 1",
                DiscoveredAt = DateTime.UtcNow
            };
            var known = new Term { Canonical = "見る", Reading = "みる", Meaning = "see" };
            var unknown = new Term { Canonical = "走る", Reading = "はしる", Meaning = "run" };

            Db.AddRange(
                anime,
                episode,
                known,
                unknown,
                new EpisodeTerm { EpisodeId = episode.Id, TermId = known.Id, Occurrences = 2 },
                new EpisodeTerm { EpisodeId = episode.Id, TermId = unknown.Id, Occurrences = 1 },
                new UserTerm
                {
                    ProfileId = Profile,
                    TermId = known.Id,
                    State = UserTermState.Learning,
                    LearningStartedAt = DateTime.UtcNow.AddDays(-1),
                    NextReviewAt = DateTime.UtcNow.AddHours(-1),
                    UpdatedAt = DateTime.UtcNow
                });
            await Db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }

        private static CurrentAccountContext Account()
        {
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, Profile)],
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
}
