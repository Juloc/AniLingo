using Jularr.Web.Features.ClientApi;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.OfflineLibrary;
using Jularr.Web.Features.Progress;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Tests;

/// <summary>
/// Smart offline prefetch (#415): Off by default, selection within a hard cap,
/// explicit downloads never evicted or counted, LRU eviction of prefetched
/// items only, plus next-up candidate selection from canonical progress.
/// </summary>
[TestClass]
public sealed class OfflinePrefetchTests
{
    private const long Mb = 1024L * 1024L;
    private static readonly DateTime Now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    private static readonly OfflinePrefetchPolicy On = new() { Enabled = true, CapBytes = 1024 * Mb };

    private static OfflinePrefetchCandidate Episode(long mb, Guid? id = null) =>
        new(OfflinePrefetchKind.Episode, id ?? Guid.NewGuid(), Guid.NewGuid(), "Show S01E01", mb * Mb);

    private static OfflinePrefetchCandidate Chapter(long mb, Guid? id = null) =>
        new(OfflinePrefetchKind.Chapter, id ?? Guid.NewGuid(), Guid.NewGuid(), "Novel", mb * Mb);

    private static OfflinePrefetchInventoryItem Stored(
        long mb,
        OfflinePrefetchOrigin origin,
        int minutesAgo,
        Guid? id = null,
        OfflinePrefetchKind kind = OfflinePrefetchKind.Episode,
        bool active = false) =>
        new(kind, id ?? Guid.NewGuid(), mb * Mb, origin, Now.AddMinutes(-minutesAgo), active);

    private static OfflinePrefetchDeviceState Device(
        IReadOnlyList<OfflinePrefetchInventoryItem>? inventory = null,
        OfflinePrefetchConnection connection = OfflinePrefetchConnection.Unmetered,
        long? deviceLimitMb = null) =>
        new(connection, deviceLimitMb is null ? null : deviceLimitMb * Mb, inventory ?? []);

    [TestMethod]
    public void PolicyIsOffByDefaultAndPlansNothingEvenWithCandidatesAndStoredItems()
    {
        Assert.IsFalse(OfflinePrefetchPolicy.Default.Enabled);
        Assert.IsFalse(OfflinePrefetchPolicy.Default.AllowMetered);

        var plan = OfflinePrefetchPlanner.Plan(
            OfflinePrefetchPolicy.Default,
            [Episode(10)],
            Device([Stored(50, OfflinePrefetchOrigin.Prefetched, 5)]));

        Assert.AreEqual(OfflinePrefetchReason.Disabled, plan.Reason);
        Assert.IsEmpty(plan.Downloads);
        Assert.IsEmpty(plan.Evictions, "Turning prefetch off must not delete anything by itself.");
    }

    [TestMethod]
    public async Task StoreDefaultsToOffPersistsPerProfileAndIgnoresDamagedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"jularr-prefetch-{Guid.NewGuid():N}");
        try
        {
            var store = new OfflinePrefetchPolicyStore(root);

            Assert.IsFalse((await store.LoadAsync("alice")).Enabled, "No file means Off.");

            await store.SaveAsync("alice", new OfflinePrefetchPolicy
            {
                Enabled = true,
                CapBytes = 2048 * Mb,
                IncludeChapters = false,
                EpisodesAhead = 4,
                AllowMetered = true
            });

            var alice = await store.LoadAsync("alice");
            Assert.IsTrue(alice.Enabled);
            Assert.AreEqual(2048 * Mb, alice.CapBytes);
            Assert.IsFalse(alice.IncludeChapters);
            Assert.AreEqual(4, alice.EpisodesAhead);
            Assert.IsTrue(alice.AllowMetered);
            Assert.IsFalse((await store.LoadAsync("bob")).Enabled, "Policies are per profile.");

            await File.WriteAllTextAsync(
                Path.Combine(root, "offline", "prefetch", "alice.json"),
                "{ not json");
            Assert.IsFalse((await store.LoadAsync("alice")).Enabled, "A damaged file must never switch prefetch on.");

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                store.SaveAsync("alice", new OfflinePrefetchPolicy { CapBytes = 1 }));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                store.SaveAsync("alice", new OfflinePrefetchPolicy { Enabled = true, IncludeEpisodes = false, IncludeChapters = false }));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                store.LoadAsync("../escape"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public void SelectionFollowsPriorityAndNeverExceedsTheCap()
    {
        var policy = On with { CapBytes = 300 * Mb };
        var first = Episode(120);
        var second = Episode(120);
        var tooBigForRest = Episode(120);
        var small = Chapter(20);
        var huge = Episode(400);

        var plan = OfflinePrefetchPlanner.Plan(
            policy,
            [first, second, huge, tooBigForRest, small],
            Device());

        CollectionAssert.AreEqual(
            new[] { first.ItemId, second.ItemId, small.ItemId },
            plan.Downloads.Select(x => x.ItemId).ToArray(),
            "Priority order is kept; an item that cannot fit is skipped, a later smaller one still fits.");
        Assert.AreEqual(260 * Mb, plan.PrefetchedBytesAfter);
        Assert.IsLessThanOrEqualTo(policy.CapBytes, plan.PrefetchedBytesAfter);
        Assert.IsEmpty(plan.Evictions);
    }

    [TestMethod]
    public void ItemsAlreadyOnTheDeviceAreNotDownloadedAgainAndScopeIsHonoured()
    {
        var have = Episode(10);
        var wantedChapter = Chapter(1);
        var wantedEpisode = Episode(10);

        var plan = OfflinePrefetchPlanner.Plan(
            On with { IncludeChapters = false },
            [have, wantedChapter, wantedEpisode, wantedEpisode],
            Device([Stored(10, OfflinePrefetchOrigin.Explicit, 1, have.ItemId)]));

        CollectionAssert.AreEqual(
            new[] { wantedEpisode.ItemId },
            plan.Downloads.Select(x => x.ItemId).ToArray(),
            "Explicit copies count as present, chapters are out of scope, duplicates collapse.");
    }

    [TestMethod]
    public void ExplicitDownloadsAreNeverEvictedAndNeverCountAgainstTheCap()
    {
        var policy = On with { CapBytes = 100 * Mb };
        var explicitBig = Stored(900, OfflinePrefetchOrigin.Explicit, 10_000);
        var oldPrefetched = Stored(60, OfflinePrefetchOrigin.Prefetched, 500);
        var wantedNew = Episode(80);

        var plan = OfflinePrefetchPlanner.Plan(
            policy,
            [wantedNew],
            Device([explicitBig, oldPrefetched]));

        Assert.AreEqual(wantedNew.ItemId, Assert.ContainsSingle(plan.Downloads).ItemId,
            "900 MB of explicit downloads must not eat the 100 MB prefetch budget.");
        Assert.AreEqual(oldPrefetched.ItemId, Assert.ContainsSingle(plan.Evictions).ItemId);
        Assert.IsFalse(plan.Evictions.Any(x => x.ItemId == explicitBig.ItemId));

        var wantsTooMuch = OfflinePrefetchPlanner.Plan(
            policy,
            [Episode(150)],
            Device([explicitBig]));
        Assert.IsEmpty(wantsTooMuch.Downloads);
        Assert.IsEmpty(wantsTooMuch.Evictions, "An explicit item is never sacrificed to make room.");
    }

    [TestMethod]
    public void AnItemReportedAsBothExplicitAndPrefetchedStaysProtected()
    {
        var id = Guid.NewGuid();
        var policy = On with { CapBytes = 100 * Mb };

        var plan = OfflinePrefetchPlanner.Plan(
            policy,
            [Episode(90)],
            Device(
            [
                Stored(90, OfflinePrefetchOrigin.Prefetched, 900, id),
                Stored(90, OfflinePrefetchOrigin.Explicit, 5, id)
            ]));

        Assert.IsEmpty(plan.Evictions, "The user kept it: a stale duplicate label cannot expose it to eviction.");
        Assert.HasCount(1, plan.Downloads, "It is treated as explicit, so it does not count against the prefetch cap either.");
    }

    [TestMethod]
    public void EvictionIsLeastRecentlyUsedFirstAndStopsAsSoonAsThereIsRoom()
    {
        var policy = On with { CapBytes = 300 * Mb };
        var newest = Stored(100, OfflinePrefetchOrigin.Prefetched, 10);
        var middle = Stored(100, OfflinePrefetchOrigin.Prefetched, 200);
        var oldest = Stored(100, OfflinePrefetchOrigin.Prefetched, 3000);
        var wanted = Episode(150);

        var plan = OfflinePrefetchPlanner.Plan(policy, [wanted], Device([newest, middle, oldest]));

        CollectionAssert.AreEqual(
            new[] { oldest.ItemId, middle.ItemId },
            plan.Evictions.Select(x => x.ItemId).ToArray(),
            "Oldest use first; the newest stays because 200 MB already make room for 150 MB.");
        Assert.AreEqual(wanted.ItemId, Assert.ContainsSingle(plan.Downloads).ItemId);
        Assert.AreEqual(250 * Mb, plan.PrefetchedBytesAfter);
    }

    [TestMethod]
    public void RunningDownloadsAndItemsWantedNextAreNotEvictedToMakeRoom()
    {
        var policy = On with { CapBytes = 200 * Mb };
        var downloading = Stored(100, OfflinePrefetchOrigin.Prefetched, 9000, active: true);
        var stillWanted = Episode(100);
        var storedButWanted = Stored(100, OfflinePrefetchOrigin.Prefetched, 8000, stillWanted.ItemId);
        var another = Episode(100);

        var plan = OfflinePrefetchPlanner.Plan(
            policy,
            [stillWanted, another],
            Device([downloading, storedButWanted]));

        Assert.IsEmpty(plan.Evictions);
        Assert.IsEmpty(plan.Downloads, "Nothing may be evicted, so the second candidate does not fit.");
    }

    [TestMethod]
    public void LoweredCapShrinksPrefetchedContentLeastRecentlyUsedFirst()
    {
        var policy = On with { CapBytes = 100 * Mb };
        var keep = Stored(60, OfflinePrefetchOrigin.Prefetched, 1);
        var drop = Stored(60, OfflinePrefetchOrigin.Prefetched, 60);
        var explicitItem = Stored(500, OfflinePrefetchOrigin.Explicit, 100_000);

        var plan = OfflinePrefetchPlanner.Plan(policy, [], Device([keep, drop, explicitItem]));

        Assert.AreEqual(drop.ItemId, Assert.ContainsSingle(plan.Evictions).ItemId);
        Assert.AreEqual(60 * Mb, plan.PrefetchedBytesAfter);
    }

    [TestMethod]
    public void MeteredConnectionsOnlyPrefetchWhenTheProfileAllowsIt()
    {
        var candidate = Episode(10);

        var blocked = OfflinePrefetchPlanner.Plan(
            On, [candidate], Device(connection: OfflinePrefetchConnection.Metered));
        Assert.AreEqual(OfflinePrefetchReason.MeteredConnection, blocked.Reason);
        Assert.IsEmpty(blocked.Downloads);

        var allowed = OfflinePrefetchPlanner.Plan(
            On with { AllowMetered = true }, [candidate], Device(connection: OfflinePrefetchConnection.Metered));
        Assert.AreEqual(OfflinePrefetchReason.Planned, allowed.Reason);
        Assert.HasCount(1, allowed.Downloads);
    }

    [TestMethod]
    public void DeviceLimitLeavesRoomForExplicitDownloadsFirst()
    {
        var plan = OfflinePrefetchPlanner.Plan(
            On,
            [Episode(100), Episode(100)],
            Device(
                [Stored(900, OfflinePrefetchOrigin.Explicit, 1)],
                deviceLimitMb: 1050));

        Assert.AreEqual(150 * Mb, plan.BudgetBytes, "Budget is what the device limit leaves after explicit downloads.");
        Assert.HasCount(1, plan.Downloads);

        var full = OfflinePrefetchPlanner.Plan(
            On,
            [Episode(1)],
            Device([Stored(900, OfflinePrefetchOrigin.Explicit, 1)], deviceLimitMb: 800));
        Assert.AreEqual(0, full.BudgetBytes);
        Assert.IsEmpty(full.Downloads);
    }

    [TestMethod]
    public void PlanSizeIsBounded()
    {
        var candidates = Enumerable.Range(0, 50).Select(_ => (OfflinePrefetchCandidate)Chapter(1)).ToArray();

        var plan = OfflinePrefetchPlanner.Plan(On, candidates, Device());

        Assert.HasCount(OfflinePrefetchPlanner.MaxDownloadsPerPlan, plan.Downloads);
    }

    [TestMethod]
    public void ClientContractMapsWireNamesStrictlyAndPointsAtTheExistingOfflineEndpoints()
    {
        Assert.IsTrue(ClientApiOfflinePrefetchContract.TryParseKind("Episode", out var kind));
        Assert.AreEqual(OfflinePrefetchKind.Episode, kind);
        Assert.IsFalse(ClientApiOfflinePrefetchContract.TryParseKind("movie", out _));
        Assert.IsTrue(ClientApiOfflinePrefetchContract.TryParseOrigin("prefetched", out var origin));
        Assert.AreEqual(OfflinePrefetchOrigin.Prefetched, origin);
        Assert.IsFalse(ClientApiOfflinePrefetchContract.TryParseOrigin(null, out _));
        Assert.IsFalse(
            ClientApiOfflinePrefetchContract.TryParseConnection(null, out _),
            "An unstated connection is rejected instead of guessed.");
        Assert.AreEqual("metered_connection", ClientApiOfflinePrefetchContract.ReasonName(OfflinePrefetchReason.MeteredConnection));

        var episode = Episode(1);
        var chapter = Chapter(1);
        Assert.AreEqual(
            ClientApiOfflineRoutes.Download(episode.ItemId),
            ClientApiOfflinePrefetchContract.DownloadUrl(episode));
        Assert.AreEqual(
            ClientApiOfflineLibraryRoutes.Chapter(chapter.ItemId),
            ClientApiOfflinePrefetchContract.DownloadUrl(chapter));
    }

    [TestMethod]
    public async Task EpisodeCandidatesContinueTheSeriesRankedByDepthAndSkipWatchedEpisodes()
    {
        await using var fixture = await EpisodeFlowFixture.CreateAsync();
        var alpha = await fixture.AddAnimeAsync("alpha");
        var beta = await fixture.AddAnimeAsync("beta");
        var a1 = await fixture.AddEpisodeAsync(alpha, 1, 1);
        var a2 = await fixture.AddEpisodeAsync(alpha, 1, 2);
        var a3 = await fixture.AddEpisodeAsync(alpha, 1, 3);
        var a4 = await fixture.AddEpisodeAsync(alpha, 1, 4);
        var b1 = await fixture.AddEpisodeAsync(beta, 1, 1);
        var b2 = await fixture.AddEpisodeAsync(beta, 1, 2);

        var reader = fixture.Service("reader");
        // alpha: episode 1 watched, episode 2 partly watched -> continue with 2;
        // episode 3 was watched out of order: it must be skipped, 4 follows 2.
        await reader.SetWatchedAsync(a1.Id, true);
        await reader.SetWatchedAsync(a3.Id, true);
        await reader.UpdateAsync(a2.Id, new EpisodeProgressUpdate(400_000, 1_400_000, false));
        // beta: episode 1 watched -> up next is 2, nothing after it.
        await reader.SetWatchedAsync(b1.Id, true);
        await fixture.SetUpdatedAtAsync("reader", a1.Id, Now.AddHours(-1));
        await fixture.SetUpdatedAtAsync("reader", a3.Id, Now.AddHours(-2));
        await fixture.SetUpdatedAtAsync("reader", a2.Id, Now);
        await fixture.SetUpdatedAtAsync("reader", b1.Id, Now.AddMinutes(-5));

        var source = new OfflinePrefetchCandidateSource(
            fixture.Db, reader, EpisodeFlowFixture.Account("reader"));

        var candidates = await source.GetAsync(On with { EpisodesAhead = 3, IncludeChapters = false });

        CollectionAssert.AreEqual(
            new[] { a2.Id, b2.Id, a4.Id },
            candidates.Select(x => x.ItemId).ToArray(),
            "Depth 0 of every series first (most recent series first), then depth 1; watched episodes are skipped.");
        Assert.IsTrue(candidates.All(x => x.Kind == OfflinePrefetchKind.Episode && x.SizeBytes == 1));

        var none = await source.GetAsync(On with { IncludeEpisodes = false, IncludeChapters = false });
        Assert.IsEmpty(none);

        var other = new OfflinePrefetchCandidateSource(
            fixture.Db, fixture.Service("other"), EpisodeFlowFixture.Account("other"));
        Assert.IsEmpty(await other.GetAsync(On), "Another profile's progress must not leak.");
    }

    [TestMethod]
    public async Task ChapterCandidatesStartAtTheCurrentChapterOrTheNextOneAfterAFinishedChapter()
    {
        await using var fixture = await EpisodeFlowFixture.CreateAsync();
        var db = fixture.Db;

        var work = new NovelWork
        {
            SourceProvider = "test",
            SourceKey = Guid.NewGuid().ToString("N"),
            SourceUrl = "https://example.invalid/work",
            Title = "Series"
        };
        var volume = new NovelVolume
        {
            WorkId = work.Id,
            Number = 1,
            Kind = NovelVolumeKinds.Web,
            SourceKey = Guid.NewGuid().ToString("N")
        };
        db.NovelWorks.Add(work);
        db.NovelVolumes.Add(volume);
        var chapters = Enumerable.Range(1, 6).Select(number => new NovelChapter
        {
            WorkId = work.Id,
            VolumeId = volume.Id,
            Number = number,
            Title = $"Chapter {number}",
            // Chapter 4 has no downloadable content and is skipped.
            OriginalText = number == 4 ? "" : new string('あ', 1000),
            SourceHash = $"hash-{number}",
            SourceUrl = $"https://example.invalid/{number}"
        }).ToArray();
        db.NovelChapters.AddRange(chapters);
        db.NovelTranslations.Add(new NovelTranslation
        {
            ChapterId = chapters[1].Id,
            TargetLanguage = "de",
            ProviderId = "test",
            SourceHash = chapters[1].SourceHash,
            Text = new string('x', 500)
        });
        db.NovelProgress.Add(new NovelProgress
        {
            ProfileId = "reader",
            WorkId = work.Id,
            ChapterId = chapters[1].Id,
            PositionPermille = 400
        });
        await db.SaveChangesAsync();

        var source = new OfflinePrefetchCandidateSource(
            db, fixture.Service("reader"), EpisodeFlowFixture.Account("reader"));
        var policy = On with { IncludeEpisodes = false, ChaptersAhead = 3 };

        var midChapter = await source.GetAsync(policy);
        CollectionAssert.AreEqual(
            new[] { chapters[1].Id, chapters[2].Id, chapters[4].Id },
            midChapter.Select(x => x.ItemId).ToArray());
        Assert.AreEqual((1000 + 500) * OfflinePrefetchCandidateSource.EstimatedBytesPerCharacter, midChapter[0].SizeBytes);
        Assert.AreEqual(work.Id, midChapter[0].ContainerId);

        var progress = await db.NovelProgress.SingleAsync();
        progress.PositionPermille = 980;
        await db.SaveChangesAsync();

        var afterFinished = await source.GetAsync(policy);
        CollectionAssert.AreEqual(
            new[] { chapters[2].Id, chapters[4].Id, chapters[5].Id },
            afterFinished.Select(x => x.ItemId).ToArray(),
            "A chapter read to the end is not fetched again.");

        var other = new OfflinePrefetchCandidateSource(
            db, fixture.Service("other"), EpisodeFlowFixture.Account("other"));
        Assert.IsEmpty(await other.GetAsync(policy));
    }
}
