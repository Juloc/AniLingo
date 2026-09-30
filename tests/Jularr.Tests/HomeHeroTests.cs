using Jularr.Web.Data;
using Jularr.Web.Features.Calendar;
using Jularr.Web.Features.Library;
using Jularr.Web.Features.Progress;
using Jularr.Web.Features.Watchlist;
using Jularr.Web.Pages;

namespace Jularr.Tests;

/// <summary>
/// Home per docs/mockups/home/SPEC.md: the hero carousel holds only the profile's own media, in the
/// order in-progress episodes → in-progress reading → up next → watchlist releases; the Continue row
/// merges watching and reading; there is no hero, chip row or stat tile without content.
/// </summary>
[TestClass]
public sealed class HomeHeroTests
{
    private const string Profile = "home-hero";

    [TestMethod]
    public async Task InProgressEpisodeIsAHeroSlideWithContinueAndProgress()
    {
        await using var fixture = await ContinueReadingQueryTests.ContinueReadingFixture.CreateAsync();
        var (animeId, episodeId) = await SeedAnimeEpisodeWithProgressAsync(fixture.Db);

        var home = await LoadHomeAsync(fixture.Db);

        var slide = Assert.ContainsSingle(home.Hero);
        Assert.AreEqual("Hero Anime", slide.Title);
        Assert.AreEqual(home.Ui["home.continueWatching.eyebrow"], slide.Label);
        Assert.AreEqual($"/Library/Episode/{episodeId}", slide.PrimaryHref);
        Assert.AreEqual(home.Ui["home.spotlight.continue"], slide.PrimaryLabel);
        Assert.IsTrue(slide.PrimaryIsPlay);
        Assert.AreEqual($"/Library/Anime/{animeId}", slide.SecondaryHref);
        Assert.AreEqual(IndexModel.HomeHeroSecondary.Details, slide.SecondaryIcon);
        Assert.IsNotNull(slide.ProgressPercent);
        StringAssert.Contains(slide.ProgressText, "min");
        StringAssert.Contains(slide.Subtitle, "The Journey Continues", "The episode line carries the episode title.");
        Assert.AreEqual(home.Ui["calendar.media.anime"], slide.Meta, "Only facts Jularr has: no year, seasons or score here.");
    }

    [TestMethod]
    public async Task HeroMixesWatchingReadingAndWatchlistInOrder()
    {
        await using var fixture = await ContinueReadingQueryTests.ContinueReadingFixture.CreateAsync();
        await SeedAnimeEpisodeWithProgressAsync(fixture.Db);
        var novel = await fixture.SeedNovelAsync("Hero Novel", chapters: 2);
        await fixture.SetNovelProgressAsync(Profile, novel, 0, 200, DateTime.UtcNow.AddMinutes(-5));
        await FollowWithRecentReleaseAsync(fixture.Db);

        var home = await LoadHomeAsync(fixture.Db);

        CollectionAssert.AreEqual(
            new[] { "Hero Anime", "Hero Novel", "Frieren" },
            home.Hero.Select(slide => slide.Title).ToArray());

        var reading = home.Hero[1];
        Assert.AreEqual(home.Ui["home.continueReading.eyebrow"], reading.Label);
        Assert.AreEqual(home.ContinueReading[0].ResumeUrl, reading.PrimaryHref, "Read opens the exact resume position.");
        Assert.AreEqual(home.Ui["home.spotlight.read"], reading.PrimaryLabel);
        Assert.IsFalse(reading.ImageIsBackdrop);

        var release = home.Hero[2];
        Assert.AreEqual(home.Ui["home.spotlight.watchlist"], release.Label);
        Assert.AreEqual("https://cdn.example/cover.jpg", release.ImageUrl);
        Assert.IsFalse(release.ImageIsBackdrop, "A cover is a poster: shown over a derived background, never stretched.");
        Assert.AreEqual(IndexModel.HomeHeroSecondary.Calendar, release.SecondaryIcon);
        Assert.IsNull(release.ProgressPercent);
    }

    [TestMethod]
    public async Task ContinueRowMergesWatchingAndReadingNewestFirst()
    {
        await using var fixture = await ContinueReadingQueryTests.ContinueReadingFixture.CreateAsync();
        var (_, episodeId) = await SeedAnimeEpisodeWithProgressAsync(fixture.Db);
        var novel = await fixture.SeedNovelAsync("Hero Novel", chapters: 2);
        await fixture.SetNovelProgressAsync(Profile, novel, 0, 200, DateTime.UtcNow.AddMinutes(-5));

        var home = await LoadHomeAsync(fixture.Db);

        CollectionAssert.AreEqual(
            new[] { "Hero Anime", "Hero Novel" },
            home.ContinueTiles.Select(tile => tile.Title).ToArray());
        Assert.AreEqual($"/Library/Episode/{episodeId}", home.ContinueTiles[0].Href);
        Assert.AreEqual(home.WatchingCaption(home.ContinueWatching[0]), home.ContinueTiles[0].Caption);
        Assert.AreEqual(home.ContinueReading[0].ResumeUrl, home.ContinueTiles[1].Href);
        Assert.AreEqual(home.Ui["home.continueWatching"], home.ContinueHeading, "The row keeps the Continue Watching heading.");
    }

    [TestMethod]
    public async Task NothingOfTheProfilesOwnMeansNoHeroAndNoRows()
    {
        await using var fixture = await ContinueReadingQueryTests.ContinueReadingFixture.CreateAsync();

        var home = await LoadHomeAsync(fixture.Db);

        Assert.AreEqual(0, home.Hero.Count);
        Assert.AreEqual(0, home.ContinueTiles.Count);
        Assert.AreEqual(0, home.ForYou.Count);
    }

    [TestMethod]
    public void HomeMarkupFollowsTheMockupWithoutChipsStatsOrDecoration()
    {
        var web = Path.Combine(RepositoryRoot(), "src", "Jularr.Web");
        var view = File.ReadAllText(Path.Combine(web, "Pages", "Index.cshtml"));
        var css = File.ReadAllText(Path.Combine(web, "wwwroot", "css", "home.css"));
        var script = File.ReadAllText(Path.Combine(web, "wwwroot", "js", "home-spotlight.js"));

        foreach (var gone in new[] { "home-filter-chip", "data-home-filter", "metric-grid", "_AppBrandMark", "/brand/", "jularr-hero" })
        {
            Assert.IsFalse(view.Contains(gone, StringComparison.Ordinal), gone);
            Assert.IsFalse(css.Contains(gone, StringComparison.Ordinal), gone);
        }

        // Every row and the hero render only with content; no placeholders.
        StringAssert.Contains(view, "@if (Model.Hero.Count > 0)");
        StringAssert.Contains(view, "@if (Model.ContinueTiles.Count > 0)");
        StringAssert.Contains(view, "@if (Model.ForYou.Count > 0)");
        StringAssert.Contains(view, "<partial name=\"_HomeContinueTile\"");

        // Slow auto-advance that respects reduced motion and pauses on interaction.
        StringAssert.Contains(script, "prefers-reduced-motion: reduce");
        StringAssert.Contains(script, "INTERVAL_MS = 8000");
        foreach (var pause in new[] { "mouseenter", "focusin", "touchstart" })
        {
            StringAssert.Contains(script, pause);
        }
    }

    private static async Task FollowWithRecentReleaseAsync(AppDbContext db)
    {
        await new WatchlistStore(db).FollowAsync(
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
        await new ReleaseCalendarCacheStore(db).SaveAsync(
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
    }

    private static async Task<(Guid AnimeId, Guid EpisodeId)> SeedAnimeEpisodeWithProgressAsync(AppDbContext db)
    {
        var anime = new Anime { Key = "home-hero-anime", Title = "Hero Anime" };
        var episode = new Episode
        {
            AnimeId = anime.Id,
            SeasonNumber = 1,
            Number = 8,
            Title = "The Journey Continues"
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
