using Jularr.Web.Data;
using Jularr.Web.Features.Presentation;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Tests;

[TestClass]
public sealed class PresentationGroupStoreTests
{
    [TestMethod]
    public async Task ReplaceAndList_RoundTripsGroupsAndRanges()
    {
        await using var fixture = await Fixture.CreateAsync();
        var work = Guid.NewGuid();

        await fixture.Store.ReplaceForWorkAsync(
            PresentationMediaType.Anime,
            work,
            [
                new PresentationGroupDraft("Anna", [new PresentationRange(1, 5)]),
                new PresentationGroupDraft("Specials", [new PresentationRange(6, 6), new PresentationRange(20, 22)])
            ],
            CancellationToken.None);

        var groups = await fixture.Store.ListForWorkAsync(PresentationMediaType.Anime, work, CancellationToken.None);

        Assert.AreEqual(2, groups.Count);
        Assert.AreEqual("Anna", groups[0].Name);
        Assert.AreEqual(0, groups[0].SortOrder);
        Assert.AreEqual(1, groups[0].Ranges.Count);
        Assert.AreEqual(1, groups[0].Ranges[0].StartUnit);
        Assert.AreEqual(5, groups[0].Ranges[0].EndUnit);

        Assert.AreEqual("Specials", groups[1].Name);
        Assert.AreEqual(1, groups[1].SortOrder);
        Assert.AreEqual(2, groups[1].Ranges.Count);
        CollectionAssert.AreEqual(
            new[] { (6, 6), (20, 22) },
            groups[1].Ranges.Select(r => (r.StartUnit, r.EndUnit)).ToArray());
    }

    [TestMethod]
    public async Task Replace_ReplacesPreviousGroupsEntirely()
    {
        await using var fixture = await Fixture.CreateAsync();
        var work = Guid.NewGuid();

        await fixture.Store.ReplaceForWorkAsync(
            PresentationMediaType.Anime,
            work,
            [
                new PresentationGroupDraft("Part 1", [new PresentationRange(1, 11)]),
                new PresentationGroupDraft("Part 2", [new PresentationRange(12, 24)])
            ],
            CancellationToken.None);

        await fixture.Store.ReplaceForWorkAsync(
            PresentationMediaType.Anime,
            work,
            [new PresentationGroupDraft("Full run", [new PresentationRange(1, 24)])],
            CancellationToken.None);

        var groups = await fixture.Store.ListForWorkAsync(PresentationMediaType.Anime, work, CancellationToken.None);

        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual("Full run", groups[0].Name);
    }

    [TestMethod]
    public async Task Replace_SkipsBlankNamedAndRangelessDrafts()
    {
        await using var fixture = await Fixture.CreateAsync();
        var work = Guid.NewGuid();

        await fixture.Store.ReplaceForWorkAsync(
            PresentationMediaType.Anime,
            work,
            [
                new PresentationGroupDraft("   ", [new PresentationRange(1, 5)]),
                new PresentationGroupDraft("Rangeless", []),
                new PresentationGroupDraft("Keeps zero-range out", [new PresentationRange(0, 0)]),
                new PresentationGroupDraft("Valid", [new PresentationRange(1, 5)])
            ],
            CancellationToken.None);

        var groups = await fixture.Store.ListForWorkAsync(PresentationMediaType.Anime, work, CancellationToken.None);

        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual("Valid", groups[0].Name);
    }

    [TestMethod]
    public async Task Replace_NormalizesReversedRangeToLowHigh()
    {
        await using var fixture = await Fixture.CreateAsync();
        var work = Guid.NewGuid();

        await fixture.Store.ReplaceForWorkAsync(
            PresentationMediaType.Anime,
            work,
            [new PresentationGroupDraft("Reversed", [new PresentationRange(10, 3)])],
            CancellationToken.None);

        var groups = await fixture.Store.ListForWorkAsync(PresentationMediaType.Anime, work, CancellationToken.None);

        Assert.AreEqual(3, groups[0].Ranges[0].StartUnit);
        Assert.AreEqual(10, groups[0].Ranges[0].EndUnit);
    }

    [TestMethod]
    public async Task Groups_AreIsolatedPerMediaTypeAndWork()
    {
        await using var fixture = await Fixture.CreateAsync();
        var animeWork = Guid.NewGuid();
        var otherWork = Guid.NewGuid();

        await fixture.Store.ReplaceForWorkAsync(
            PresentationMediaType.Anime,
            animeWork,
            [new PresentationGroupDraft("Anime group", [new PresentationRange(1, 2)])],
            CancellationToken.None);
        // Same GUID reused under a different media type must not collide.
        await fixture.Store.ReplaceForWorkAsync(
            PresentationMediaType.Novel,
            animeWork,
            [new PresentationGroupDraft("Novel group", [new PresentationRange(1, 2)])],
            CancellationToken.None);

        Assert.IsTrue(await fixture.Store.HasGroupsAsync(PresentationMediaType.Anime, animeWork, CancellationToken.None));
        Assert.IsTrue(await fixture.Store.HasGroupsAsync(PresentationMediaType.Novel, animeWork, CancellationToken.None));
        Assert.IsFalse(await fixture.Store.HasGroupsAsync(PresentationMediaType.Anime, otherWork, CancellationToken.None));

        var animeGroups = await fixture.Store.ListForWorkAsync(PresentationMediaType.Anime, animeWork, CancellationToken.None);
        Assert.AreEqual(1, animeGroups.Count);
        Assert.AreEqual("Anime group", animeGroups[0].Name);
    }

    [TestMethod]
    public async Task DeleteForWork_RemovesEverything()
    {
        await using var fixture = await Fixture.CreateAsync();
        var work = Guid.NewGuid();

        await fixture.Store.ReplaceForWorkAsync(
            PresentationMediaType.Anime,
            work,
            [new PresentationGroupDraft("Part 1", [new PresentationRange(1, 5)])],
            CancellationToken.None);

        await fixture.Store.DeleteForWorkAsync(PresentationMediaType.Anime, work, CancellationToken.None);

        Assert.IsFalse(await fixture.Store.HasGroupsAsync(PresentationMediaType.Anime, work, CancellationToken.None));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private readonly AppDbContext _db;

        private Fixture(string databasePath, AppDbContext db)
        {
            _databasePath = databasePath;
            _db = db;
            Store = new PresentationGroupStore(db);
        }

        public PresentationGroupStore Store { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"jularr-presentation-{Guid.NewGuid():N}.db");
            var db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
                    .Options);
            await DatabaseMigrationBridge.UpgradeAsync(db);
            return new Fixture(databasePath, db);
        }

        public async ValueTask DisposeAsync()
        {
            await _db.DisposeAsync();
            try
            {
                if (File.Exists(_databasePath))
                {
                    File.Delete(_databasePath);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup of the temp database file.
            }
        }
    }
}
