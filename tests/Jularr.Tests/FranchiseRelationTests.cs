using Jularr.Web.Data;
using Jularr.Web.Features.Franchises;
using Jularr.Web.Features.Watchlist;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Tests;

[TestClass]
public sealed class FranchiseRelationTests
{
    [TestMethod]
    public async Task ProviderRelationIsStoredDirectionally()
    {
        await using var fixture = await Fixture.CreateAsync();
        var store = new MediaRelationStore(fixture.Db);
        var from = Identity("100");
        var to = Identity("101");

        await store.UpsertProviderAsync(
            from,
            to,
            "SEQUEL",
            "anilist",
            1.0,
            confirmed: true,
            CancellationToken.None);

        var relations = await store.GetForMembersAsync([from, to], CancellationToken.None);
        var relation = relations.Single();

        Assert.AreEqual(from.Key, relation.From.Key);
        Assert.AreEqual(to.Key, relation.To.Key);
        Assert.AreEqual("sequel", relation.RelationType);
        Assert.AreEqual(MediaRelationReviewState.Confirmed, relation.ReviewState);
        Assert.IsFalse(relation.IsManual);
    }

    [TestMethod]
    public async Task ManualDecisionSurvivesProviderRefresh()
    {
        await using var fixture = await Fixture.CreateAsync();
        var store = new MediaRelationStore(fixture.Db);
        var from = Identity("100");
        var to = Identity("101");

        await store.UpsertProviderAsync(
            from,
            to,
            "SEQUEL",
            "anilist",
            0.8,
            confirmed: true,
            CancellationToken.None);

        var relation = (await store.GetForMembersAsync([from, to], CancellationToken.None)).Single();
        await store.SetReviewStateAsync(
            relation.Id,
            MediaRelationReviewState.Rejected,
            CancellationToken.None);

        await store.UpsertProviderAsync(
            from,
            to,
            "SEQUEL",
            "anilist",
            1.0,
            confirmed: true,
            CancellationToken.None);

        var visible = await store.GetForMembersAsync([from, to], CancellationToken.None);
        Assert.AreEqual(0, visible.Count);
    }

    [TestMethod]
    public async Task ManualRelationReplacesProviderMetadata()
    {
        await using var fixture = await Fixture.CreateAsync();
        var store = new MediaRelationStore(fixture.Db);
        var from = Identity("100");
        var to = Identity("101");

        await store.UpsertProviderAsync(
            from,
            to,
            "RELATED",
            "anilist",
            0.6,
            confirmed: false,
            CancellationToken.None);

        await store.UpsertManualAsync(
            from,
            to,
            "RELATED",
            CancellationToken.None);

        var relation = (await store.GetForMembersAsync([from, to], CancellationToken.None)).Single();
        Assert.IsTrue(relation.IsManual);
        Assert.AreEqual("manual", relation.Source);
        Assert.AreEqual(1.0, relation.Confidence);
        Assert.AreEqual(MediaRelationReviewState.Confirmed, relation.ReviewState);
    }

    private static WatchlistIdentity Identity(string externalId) =>
        new(WatchlistMediaType.Anime, "anilist", externalId);

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
                $"jularr-franchise-relations-{Guid.NewGuid():N}");
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
