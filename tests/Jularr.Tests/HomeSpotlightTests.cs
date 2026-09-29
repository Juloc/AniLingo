using Jularr.Web.Data;
using Jularr.Web.Features.Calendar;
using Jularr.Web.Features.Library;
using Jularr.Web.Features.Progress;
using Jularr.Web.Features.Watchlist;
using Jularr.Web.Pages;

namespace Jularr.Tests;

/// <summary>
/// The Home spotlight banner shows only the profile's own media: the most recent Continue
/// Watching series, else the newest release of a followed watchlist work from the local release
/// cache, else nothing at all. No generic or featured content, and no decorative hero.
/// </summary>
[TestClass]
public sealed class HomeSpotlightTests
{
    private const string Profile = "home-spotlight";

    [TestMethod]
    public async Task ContinueWatchingSeriesIsTheSpotlight()
    {
        await using var fixture = await ContinueReadingQueryTests.ContinueReadingFixture.CreateAsync();
        var (animeId, episodeId) = await SeedAnimeEpisodeWithProgressAsync(fixture.Db);

        var home = await LoadHomeAsync(fixture.Db);

        var slide = Assert.ContainsSingle(home.Spotlight);
        Assert.AreEqual("Spotlight Anime", slide.Title);
        Assert.AreEqual(home.Ui["home.continueWatching.eyebrow"], slide.Label);
        Assert.AreEqual(home.WatchingCaption(home.ContinueWatching[0]), slide.Subtitle);
        Assert.AreEqual($"/Library/Episode/{episodeId}", slide.PrimaryHref);
        Assert.AreEqual(home.Ui["home.continueWatching.resume"], slide.PrimaryLabel);
        Assert.AreEqual($"/Library/Anime/{animeId}", slide.SecondaryHref);
        Assert.IsNotNull(slide.ProgressPercent);
    }

    [TestMethod]
    public async Task NewWatchlistReleaseIsTheSpotlightWhenNothingIsInProgress()
    {
        await using var fixture = await ContinueReadingQueryTests.ContinueReadingFixture.CreateAsync();
        var watchlist = new WatchlistStore(fixture.Db);
        await watchlist.FollowAsync(
            Profile,
            new WatchlistDraft(
                new WatchlistIdentity(WatchlistMediaType.Anime, "anilist", "154587"),
                "Frieren",
                CoverImageUrl: "https://cdn.example/cover.jpg",
                Format: "TV",
                Status: "RELEASING",
                Year: 2026),
            CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        await new ReleaseCalendarCacheStore(fixture.Db).SaveAsync(
            "anilist",
            [
                new ReleaseSourceSnapshot(
                    "154587",
                    "RELEASING",
                    [new CachedRelease("anilist", "154587", ReleaseKind.Episode, 3, ReleaseDate.FromInstant(now.AddDays(-2)))])
            ],
            now.AddDays(-30),
            now.UtcDateTime,
            CancellationToken.None);

        var home = await LoadHomeAsync(fixture.Db);

        var slide = Assert.ContainsSingle(home.Spotlight);
        Assert.AreEqual("Frieren", slide.Title);
        Assert.AreEqual(home.Ui["home.spotlight.watchlist"], slide.Label);
        Assert.AreEqual("https://cdn.example/cover.jpg", slide.ImageUrl);
        Assert.IsFalse(slide.ImageIsBackdrop, "A watchlist cover is a poster: shown over a derived background, not stretched.");
        StringAssert.StartsWith(slide.PrimaryHref, "/Watchlist");
        Assert.IsNull(slide.ProgressPercent);
    }

    [TestMethod]
    public async Task NothingInProgressAndNoWatchlistMeansNoBanner()
    {
        await using var fixture = await ContinueReadingQueryTests.ContinueReadingFixture.CreateAsync();

        var home = await LoadHomeAsync(fixture.Db);

        Assert.AreEqual(0, home.Spotlight.Count);
    }

    [TestMethod]
    public void HomeHasNoDecorativeHeroOrBrandArtwork()
    {
        var web = Path.Combine(RepositoryRoot(), "src", "Jularr.Web");
        var view = File.ReadAllText(Path.Combine(web, "Pages", "Index.cshtml"));
        var css = File.ReadAllText(Path.Combine(web, "wwwroot", "css", "home.css"));

        foreach (var gone in new[] { "home-hero", "_AppBrandMark", "/brand/", "jularr-hero" })
        {
            Assert.IsFalse(view.Contains(gone, StringComparison.Ordinal), gone);
            Assert.IsFalse(css.Contains(gone, StringComparison.Ordinal), gone);
        }

        // The banner is conditional: without spotlight data Home renders no banner or placeholder.
        StringAssert.Contains(view, "@if (Model.Spotlight.Count > 0)");
    }

    private static async Task<(Guid AnimeId, Guid EpisodeId)> SeedAnimeEpisodeWithProgressAsync(AppDbContext db)
    {
        var anime = new Anime { Key = "home-spotlight-anime", Title = "Spotlight Anime" };
        var episode = new Episode
        {
            AnimeId = anime.Id,
            SeasonNumber = 1,
            Number = 4,
            Title = "Episode 4"
        };
        var root = new LibraryRoot { Name = "Test", Path = Path.GetTempPath() };
        db.AddRange(anime, episode, root);
        db.Add(new MediaFile
        {
            LibraryRootId = root.Id,
            EpisodeId = episode.Id,
            Path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mkv"),
            SizeBytes = 1,
            LastWriteTimeUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var progress = new EpisodeProgressService(db, EpisodeFlowFixture.Account(Profile));
        await progress.UpdateAsync(episode.Id, new EpisodeProgressUpdate(500_000, 1_400_000, false));
        return (anime.Id, episode.Id);
    }

    private static async Task<IndexModel> LoadHomeAsync(AppDbContext db)
    {
        var home = new IndexModel(db, EpisodeFlowFixture.Account(Profile));
        await home.OnGetAsync(CancellationToken.None);
        return home;
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

        throw new DirectoryNotFoundException("Could not locate Jularr repository root.");
    }
}
