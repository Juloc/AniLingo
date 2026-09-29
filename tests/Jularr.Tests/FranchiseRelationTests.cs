using Jularr.Web.Data;
using Jularr.Web.Features.Calendar;
using Jularr.Web.Features.Franchises;
using Jularr.Web.Features.Metadata;
using Jularr.Web.Features.Tracking;
using Jularr.Web.Features.Watchlist;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jularr.Tests;

/// <summary>
/// Franchise membership on a migrated SQLite database with a fake AniList: only the seed identity
/// comes from the browser, refresh runs are bounded and incremental, the member cap only limits
/// growth, rate limits stop a run, and manual refreshes are limited to followers and the owner.
/// </summary>
[TestClass]
public sealed class FranchiseRelationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task FollowStoresOnlyTheSeedAndTheRefreshBuildsMembersFromTheProvider()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Source.Add("100", "Series A", ("SEQUEL", "101"), ("CHARACTER", "500"));
        fixture.Source.Add("101", "Series A 2", ("PREQUEL", "100"));

        var franchiseId = await fixture.Service.FollowFromSeedAsync("profile-a", Anime("100"), CancellationToken.None);

        Assert.AreEqual(0, fixture.Source.Calls.Count, "Following makes no provider call.");
        var pending = await fixture.Store.GetAsync(franchiseId, CancellationToken.None);
        Assert.AreEqual("", pending!.Title);
        Assert.AreEqual(0, pending.MemberCount);
        CollectionAssert.AreEqual(new[] { franchiseId }, (await fixture.Store.ListRefreshDueAsync(Now.UtcDateTime, CancellationToken.None)).ToArray());

        var result = await fixture.Service.RefreshAsync(franchiseId, CancellationToken.None);

        Assert.IsTrue(result.Complete);
        Assert.AreEqual(2, result.Requests);
        Assert.AreEqual("Series A", (await fixture.Store.GetAsync(franchiseId, CancellationToken.None))!.Title);
        var members = await fixture.Store.GetMembersAsync(franchiseId, CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { "100", "101" }, members.Select(member => member.Media.Identity.ExternalKey).ToArray());
        var seed = members.Single(member => member.IsSeed);
        Assert.AreEqual("Series A", seed.Media.Title);
        Assert.IsNull(seed.RelationType, "Being listed as a prequel does not relabel the seed.");
        var sequel = members.Single(member => !member.IsSeed);
        Assert.AreEqual("SEQUEL", sequel.RelationType);
        Assert.AreEqual("https://img.anilist.test/101.jpg", sequel.Media.CoverImageUrl);

        var graph = await new MediaRelationStore(fixture.Db).GetForFranchiseAsync(franchiseId, CancellationToken.None);
        Assert.IsTrue(graph.Any(relation => relation.From.ExternalKey == "100" && relation.To.ExternalKey == "101" && relation.RelationType == "sequel"));
        Assert.AreEqual(0, (await fixture.Store.ListRefreshDueAsync(Now.UtcDateTime, CancellationToken.None)).Count);
    }

    [TestMethod]
    public async Task RunsAreBoundedAndContinueWhereTheyStopped()
    {
        await using var fixture = await Fixture.CreateAsync();
        const int works = 25;
        for (var id = 1; id <= works; id++)
        {
            fixture.Source.Add(id.ToString(), $"Season {id}", id < works ? [("SEQUEL", (id + 1).ToString())] : []);
        }

        var franchiseId = await fixture.Service.FollowFromSeedAsync("profile-a", Anime("1"), CancellationToken.None);
        var runs = new List<FranchiseRefreshResult>();
        do
        {
            runs.Add(await fixture.Service.RefreshAsync(franchiseId, CancellationToken.None));
        }
        while (!runs[^1].Complete && runs.Count < 30);

        Assert.IsTrue(runs.All(run => run.Requests <= FranchiseService.MaxRequestsPerRun));
        Assert.IsTrue(runs[^1].Complete);
        Assert.AreEqual(works, runs.Sum(run => run.Requests), "Every work is read once.");
        Assert.AreEqual(works, (await fixture.Store.GetMembersAsync(franchiseId, CancellationToken.None)).Count);

        var again = await fixture.Service.RefreshAsync(franchiseId, CancellationToken.None);
        Assert.AreEqual(0, again.Requests, "Nothing is read again before the recheck interval.");
    }

    [TestMethod]
    public async Task LargeFranchiseStillFindsANewSequel()
    {
        await using var fixture = await Fixture.CreateAsync();
        var franchiseId = await fixture.Store.GetOrCreateBySeedAsync(Anime("1"), CancellationToken.None);
        await fixture.Store.FollowAsync("profile-a", franchiseId, CancellationToken.None);
        for (var id = 1; id <= 80; id++)
        {
            var identity = Anime(id.ToString());
            await fixture.Store.UpsertMemberAsync(franchiseId, new WatchlistDraft(identity, $"Work {id}"), id == 1 ? null : "SEQUEL", id == 1, CancellationToken.None);
            await fixture.Store.MarkMemberCheckedAsync(franchiseId, identity, Now.UtcDateTime, CancellationToken.None);
        }

        fixture.Source.Add("80", "Work 80", ("SEQUEL", "81"));
        fixture.Source.Add("81", "Work 81");
        fixture.Clock.Advance(FranchiseService.MemberRecheckAfter);
        for (var run = 0; run < 12 && !(await fixture.Service.RefreshAsync(franchiseId, CancellationToken.None)).Complete; run++)
        {
        }

        var members = await fixture.Store.GetMembersAsync(franchiseId, CancellationToken.None);
        Assert.IsTrue(members.Any(member => member.Media.Identity.ExternalKey == "81"), "More than 60 works do not stop the refresh.");
    }

    [TestMethod]
    public async Task MemberCapOnlyLimitsGrowth()
    {
        await using var fixture = await Fixture.CreateAsync();
        var related = Enumerable.Range(1000, FranchiseService.MaxMembers + 20)
            .Select(id => ("SIDE_STORY", id.ToString()))
            .ToArray();
        fixture.Source.Add("1", "Seed", related);

        var franchiseId = await fixture.Service.FollowFromSeedAsync("profile-a", Anime("1"), CancellationToken.None);
        await fixture.Service.RefreshAsync(franchiseId, CancellationToken.None);

        Assert.AreEqual(FranchiseService.MaxMembers, (await fixture.Store.GetMembersAsync(franchiseId, CancellationToken.None)).Count);
    }

    [TestMethod]
    public async Task RateLimitStopsTheRunAndKeepsTheWorkDue()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Source.Add("1", "Seed", ("SEQUEL", "2"));
        fixture.Source.Add("2", "Sequel");
        var franchiseId = await fixture.Service.FollowFromSeedAsync("profile-a", Anime("1"), CancellationToken.None);

        fixture.Source.RateLimitOn = "1";
        var limited = await fixture.Service.RefreshAsync(franchiseId, CancellationToken.None);
        Assert.AreEqual(TimeSpan.FromMinutes(2), limited.RetryAfter);
        Assert.IsFalse(limited.Complete);
        Assert.AreEqual(0, (await fixture.Store.GetMembersAsync(franchiseId, CancellationToken.None)).Count);

        var blocked = await fixture.Service.RefreshAsync(franchiseId, CancellationToken.None);
        Assert.AreEqual(0, blocked.Requests, "No request while AniList's pause lasts.");

        fixture.Source.RateLimitOn = null;
        fixture.Clock.Advance(TimeSpan.FromMinutes(3));
        var resumed = await fixture.Service.RefreshAsync(franchiseId, CancellationToken.None);
        Assert.IsTrue(resumed.Complete);
        Assert.AreEqual(2, (await fixture.Store.GetMembersAsync(franchiseId, CancellationToken.None)).Count);
    }

    [TestMethod]
    public async Task ManualRefreshIsForFollowersOrTheOwnerWithACooldown()
    {
        await using var fixture = await Fixture.CreateAsync();
        var franchiseId = await fixture.Service.FollowFromSeedAsync("follower", Anime("1"), CancellationToken.None);

        Assert.AreEqual(FranchiseRefreshRequest.NotAllowed, await fixture.Service.RequestRefreshAsync(franchiseId, "stranger", false, CancellationToken.None));
        Assert.AreEqual(FranchiseRefreshRequest.Queued, await fixture.Service.RequestRefreshAsync(franchiseId, "follower", false, CancellationToken.None));
        Assert.AreEqual(FranchiseRefreshRequest.CoolingDown, await fixture.Service.RequestRefreshAsync(franchiseId, "follower", false, CancellationToken.None));
        Assert.AreEqual(FranchiseRefreshRequest.CoolingDown, await fixture.Service.RequestRefreshAsync(franchiseId, "owner", true, CancellationToken.None));

        fixture.Clock.Advance(FranchiseService.RefreshCooldown + TimeSpan.FromMinutes(1));
        Assert.AreEqual(FranchiseRefreshRequest.Queued, await fixture.Service.RequestRefreshAsync(franchiseId, "owner", true, CancellationToken.None));
        Assert.AreEqual(FranchiseRefreshRequest.NotFound, await fixture.Service.RequestRefreshAsync(Guid.NewGuid(), "owner", true, CancellationToken.None));
    }

    [TestMethod]
    public async Task ManualRefreshReadsKnownMembersAgain()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Source.Add("1", "Seed", ("SEQUEL", "2"));
        fixture.Source.Add("2", "Sequel");
        var franchiseId = await fixture.Service.FollowFromSeedAsync("profile-a", Anime("1"), CancellationToken.None);
        await fixture.Service.RefreshAsync(franchiseId, CancellationToken.None);

        fixture.Clock.Advance(FranchiseService.RefreshCooldown + TimeSpan.FromMinutes(1));
        fixture.Source.Add("2", "Sequel", ("SEQUEL", "3"));
        fixture.Source.Add("3", "Third");
        Assert.AreEqual(FranchiseRefreshRequest.Queued, await fixture.Service.RequestRefreshAsync(franchiseId, "profile-a", false, CancellationToken.None));
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await fixture.Service.RefreshAsync(franchiseId, CancellationToken.None);

        Assert.IsTrue((await fixture.Store.GetMembersAsync(franchiseId, CancellationToken.None)).Any(member => member.Media.Identity.ExternalKey == "3"));
    }

    [TestMethod]
    public void OnlyAniListWorksStartAFranchise()
    {
        Assert.IsTrue(FranchiseService.CanSeed(new WatchlistIdentity(WatchlistMediaType.LightNovel, "anilist", "1")));
        Assert.IsFalse(FranchiseService.CanSeed(new WatchlistIdentity(WatchlistMediaType.Book, "anilist", "1")));
        Assert.IsFalse(FranchiseService.CanSeed(new WatchlistIdentity(WatchlistMediaType.Anime, "tmdb", "1")));
    }

    [TestMethod]
    public async Task RelationGroupsBucketDirectEdgesByTypeAndFallBackToSameFranchise()
    {
        await using var fixture = await Fixture.CreateAsync();
        // 101 is a direct sequel of the seed, 102 a direct side story, 103 reached only through
        // AniList's CONTAINS (no specific relation, per FranchiseLabels), and 104 a sequel of 101
        // with no direct edge to the seed at all: every one of those is still a franchise member.
        fixture.Source.Add("100", "Series A", ("SEQUEL", "101"), ("SIDE_STORY", "102"), ("CONTAINS", "103"));
        fixture.Source.Add("101", "Series A 2", ("SEQUEL", "104"));
        fixture.Source.Add("102", "Side Story");
        fixture.Source.Add("103", "Collection");
        fixture.Source.Add("104", "Series A 3");

        var franchiseId = await fixture.Service.FollowFromSeedAsync("profile-a", Anime("100"), CancellationToken.None);
        for (var run = 0; run < 5 && !(await fixture.Service.RefreshAsync(franchiseId, CancellationToken.None)).Complete; run++)
        {
        }

        var groups = await fixture.Service.GetRelationGroupsAsync(Anime("100"), CancellationToken.None);
        var byKey = groups.ToDictionary(group => group.GroupKey, group => group.Items.Select(item => item.Identity.ExternalKey).ToArray());

        CollectionAssert.AreEquivalent(new[] { "101" }, byKey["franchise.group.sequelPrequel"]);
        CollectionAssert.AreEquivalent(new[] { "102" }, byKey["franchise.group.sideStory"]);
        CollectionAssert.AreEquivalent(
            new[] { "103", "104" },
            byKey[FranchiseLabels.SameFranchiseGroupKey],
            "CONTAINS names no specific relation, and 104 has no direct edge to the seed; both still belong to the franchise.");
        Assert.IsFalse(byKey.ContainsKey("franchise.group.spinOff"), "Empty groups are omitted.");
    }

    [TestMethod]
    public async Task RelationGroupsAreEmptyForAWorkThatIsNotAFranchiseMember()
    {
        await using var fixture = await Fixture.CreateAsync();

        var groups = await fixture.Service.GetRelationGroupsAsync(Anime("999"), CancellationToken.None);

        Assert.AreEqual(0, groups.Count);
    }

    [TestMethod]
    [DataRow("ADAPTATION", "franchise.group.adaptation")]
    [DataRow("SOURCE", "franchise.group.adaptation")]
    [DataRow("SEQUEL", "franchise.group.sequelPrequel")]
    [DataRow("PREQUEL", "franchise.group.sequelPrequel")]
    [DataRow("SIDE_STORY", "franchise.group.sideStory")]
    [DataRow("PARENT", "franchise.group.sideStory")]
    [DataRow("SPIN_OFF", "franchise.group.spinOff")]
    [DataRow("ALTERNATIVE", "franchise.group.alternative")]
    [DataRow("SUMMARY", "franchise.group.alternative")]
    [DataRow("COMPILATION", "franchise.group.alternative")]
    [DataRow("REMAKE", "franchise.group.alternative")]
    [DataRow("CONTAINS", "franchise.group.sameFranchise")]
    [DataRow("CHARACTER", "franchise.group.sameFranchise")]
    [DataRow(null, "franchise.group.sameFranchise")]
    public void RelationGroupKeyMapsEveryRelationTypeSomewhere(string? relationType, string expectedGroupKey)
    {
        Assert.AreEqual(expectedGroupKey, FranchiseLabels.RelationGroupKey(relationType));
    }

    [TestMethod]
    public void RemakeHasAPerCardRelationLabel()
    {
        Assert.AreEqual("franchise.relation.remake", FranchiseLabels.RelationKey("REMAKE"));
    }

    private static WatchlistIdentity Anime(string externalId) =>
        new(WatchlistMediaType.Anime, "anilist", externalId);

    private sealed class FakeRelationSource(AniListRateLimitGate gate, TimeProvider clock) : IFranchiseRelationSource
    {
        private readonly Dictionary<string, AniListRelatedMedia> works = new(StringComparer.Ordinal);

        public List<string> Calls { get; } = [];

        public string? RateLimitOn { get; set; }

        public void Add(string externalId, string title, params (string Relation, string ExternalId)[] related) =>
            works[externalId] = new AniListRelatedMedia(
                Summary(externalId, title),
                related.Select(item => new AniListMediaRelation(item.Relation, Summary(item.ExternalId, $"Work {item.ExternalId}"))).ToArray());

        public Task<AniListRelatedMedia> GetRelatedAsync(WatchlistIdentity work, CancellationToken cancellationToken)
        {
            Calls.Add(work.ExternalKey);
            if (RateLimitOn == work.ExternalKey)
            {
                // What AniListRateLimitHandler records before the provider throws.
                gate.Block(clock.GetUtcNow() + TimeSpan.FromMinutes(2));
                throw new MetadataProviderException("AniList returned HTTP 429.");
            }

            return Task.FromResult(works.GetValueOrDefault(work.ExternalKey) ?? new AniListRelatedMedia(null, []));
        }

        private static AniListMediaSummary Summary(string externalId, string title) =>
            new("ANIME", externalId, title, null, $"https://img.anilist.test/{externalId}.jpg", "TV", "FINISHED", 2020);
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan by) => current += by;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string directory;

        private Fixture(string directory, AppDbContext db)
        {
            this.directory = directory;
            Db = db;
            Store = new FranchiseStore(db);
            var gate = new AniListRateLimitGate();
            Source = new FakeRelationSource(gate, Clock);
            Service = new FranchiseService(
                Store,
                new MediaRelationStore(db),
                Source,
                new AniListRequestLimiter(gate, Clock) { Spacing = TimeSpan.Zero },
                new FranchiseRefreshSignal(),
                NullLogger<FranchiseService>.Instance,
                Clock);
        }

        public AppDbContext Db { get; }

        public MutableClock Clock { get; } = new(Now);

        public FranchiseStore Store { get; }

        public FakeRelationSource Source { get; }

        public FranchiseService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"jularr-franchise-{Guid.NewGuid():N}");
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
