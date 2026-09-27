using Jularr.Web.Data;
using Jularr.Web.Features.Franchises;
using Jularr.Web.Features.Watchlist;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Tests;

[TestClass]
public sealed class FranchiseDetailLinkTests
{
    [TestMethod]
    public async Task CanonicalMemberResolvesItsFranchise()
    {
        await using var fixture = await Fixture.CreateAsync();
        var store = new FranchiseStore(fixture.Db);

        var seed = Draft(WatchlistMediaType.Anime, "100", "Series");
        var franchiseId = await store.GetOrCreateBySeedAsync(seed, CancellationToken.None);

        var manga = Draft(WatchlistMediaType.Manga, "200", "Series Manga");
        await store.UpsertMemberAsync(
            franchiseId,
            manga,
            "SOURCE",
            false,
            CancellationToken.None);

        var matches = await store.FindForMemberAsync(
            manga.Identity,
            CancellationToken.None);

        Assert.AreEqual(1, matches.Count);
        Assert.AreEqual(franchiseId, matches[0].Id);
        Assert.AreEqual("Series", matches[0].Title);
        Assert.AreEqual(2, matches[0].MemberCount);
    }

    [TestMethod]
    public async Task UnknownCanonicalMemberHasNoFranchiseLink()
    {
        await using var fixture = await Fixture.CreateAsync();
        var store = new FranchiseStore(fixture.Db);

        var matches = await store.FindForMemberAsync(
            new WatchlistIdentity(WatchlistMediaType.LightNovel, "anilist", "999"),
            CancellationToken.None);

        Assert.AreEqual(0, matches.Count);
    }

    private static WatchlistDraft Draft(
        WatchlistMediaType type,
        string externalId,
        string title) =>
        new(
            new WatchlistIdentity(type, "anilist", externalId),
            title,
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
                $"jularr-franchise-detail-{Guid.NewGuid():N}");
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
