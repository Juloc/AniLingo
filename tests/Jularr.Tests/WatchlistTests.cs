using Jularr.Web.Data;
using Jularr.Web.Features.Calendar;
using Jularr.Web.Features.Franchises;
using Jularr.Web.Features.Watchlist;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Tests;

[TestClass]
public sealed class WatchlistTests
{
    [TestMethod]
    public async Task LocalFollowStateOverridesFranchiseInheritance()
    {
        await using var fixture = await Fixture.CreateAsync();
        var watchlist = new WatchlistStore(fixture.Db);
        var franchises = new FranchiseStore(fixture.Db);
        const string profile = "profile-a";

        var seed = Draft("100", "Series A");
        var sequel = Draft("101", "Series A 2");
        var franchiseId = await franchises.GetOrCreateBySeedAsync(seed, CancellationToken.None);
        await franchises.UpsertMemberAsync(
            franchiseId,
            sequel,
            "SEQUEL",
            false,
            CancellationToken.None);
        await franchises.FollowAsync(profile, franchiseId, CancellationToken.None);

        var inherited = await watchlist.GetEffectiveAsync(profile, CancellationToken.None);
        Assert.AreEqual(2, inherited.Count);
        Assert.IsTrue(inherited.All(item => item.IsFromFranchise));

        await watchlist.UnfollowAsync(profile, sequel.Identity, CancellationToken.None);
        var excluded = await watchlist.GetEffectiveAsync(profile, CancellationToken.None);
        Assert.AreEqual(1, excluded.Count);
        Assert.AreEqual(seed.Identity.Key, excluded.Single().Identity.Key);

        await watchlist.FollowAsync(profile, sequel, CancellationToken.None);
        var restored = await watchlist.GetEffectiveAsync(profile, CancellationToken.None);
        Assert.AreEqual(2, restored.Count);
        var explicitSequel = restored.Single(item => item.Identity.Key == sequel.Identity.Key);
        Assert.IsTrue(explicitSequel.IsExplicit);
        Assert.AreEqual(franchiseId, explicitSequel.FranchiseId);
    }

    [TestMethod]
    public async Task RemoteFollowedAnimeUsesCachedReleaseDataInProfilesCalendar()
    {
        await using var fixture = await Fixture.CreateAsync();
        var watchlist = new WatchlistStore(fixture.Db);
        var cache = new ReleaseCalendarCacheStore(fixture.Db);
        const string profile = "profile-a";
        var followed = Draft("154587", "Frieren");

        await watchlist.FollowAsync(profile, followed, CancellationToken.None);
        var airing = ReleaseDate.FromInstant(
            new DateTimeOffset(2026, 10, 5, 15, 0, 0, TimeSpan.Zero));
        await cache.SaveAsync(
            "anilist",
            [
                new ReleaseSourceSnapshot(
                    "154587",
                    "RELEASING",
                    [new CachedRelease("anilist", "154587", ReleaseKind.Episode, 3, airing)])
            ],
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        var source = new WatchlistReleaseEventSource(cache, watchlist);
        var query = new ReleaseEventQuery(
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 31),
            TimeZoneInfo.Utc,
            new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
            ProfileId: profile);

        var events = await source.GetEventsAsync(query, CancellationToken.None);
        var release = events.Single();
        Assert.AreEqual(ReleaseMediaType.Anime, release.MediaType);
        Assert.AreEqual(3, release.Unit?.Number);
        Assert.AreEqual(ReleaseLocalState.Monitored, release.Local.State);
        Assert.IsFalse(release.Local.InLibrary);
        Assert.AreEqual("/Watchlist", release.DetailsUrl);

        var otherProfile = await source.GetEventsAsync(
            query with { ProfileId = "profile-b" },
            CancellationToken.None);
        Assert.AreEqual(0, otherProfile.Count);
    }

    [TestMethod]
    public void WatchlistInputRejectsUnsafeDetailsUrl()
    {
        Assert.IsTrue(WatchlistDraftInput.TryCreate(
            "anime",
            "anilist",
            "1",
            "Title",
            null,
            "https://cdn.example/cover.jpg",
            "TV",
            "RELEASING",
            2026,
            null,
            "javascript:alert(1)",
            out var draft));

        Assert.IsNull(draft.DetailsUrl);
        Assert.AreEqual("https://cdn.example/cover.jpg", draft.CoverImageUrl);
    }

    private static WatchlistDraft Draft(string externalId, string title) =>
        new(
            new WatchlistIdentity(WatchlistMediaType.Anime, "anilist", externalId),
            title,
            CoverImageUrl: "https://cdn.example/cover.jpg",
            Format: "TV",
            Status: "RELEASING",
            Year: 2026,
            DetailsUrl: "/Watchlist");

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string directory;

        private Fixture(string directory, AppDbContext db)
        {
            this.directory = directory;
            Db = db;
        }

        public AppDbContext Db { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"jularr-watchlist-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite($"Data Source={Path.Combine(directory, "app.db")};Foreign Keys=True")
                    .Options);
            await DatabaseMigrationBridge.UpgradeAsync(db);
            return new Fixture(directory, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
