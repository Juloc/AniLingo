using AniLingo.Web.Data;
using AniLingo.Web.Features.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniLingo.Tests;

[TestClass]
public sealed class LibraryWatchTests
{
    private static readonly DateTime T0 = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void BurstOfEventsBecomesOneBatchPerRootAfterTheQuietPeriod()
    {
        var tracker = new LibraryChangeTracker(TimeSpan.FromSeconds(10));
        var rootId = Guid.NewGuid();
        var rootPath = Path.Combine(Path.GetTempPath(), "anime");

        for (var i = 0; i < 25; i++)
        {
            tracker.RecordChange(
                rootId,
                rootPath,
                Path.Combine(rootPath, "Frieren", "Season 01", $"Frieren - S01E{i:00}.mkv.partial~"),
                T0.AddMilliseconds(i * 100));
        }

        tracker.RecordChange(rootId, rootPath, Path.Combine(rootPath, "Frieren", "poster.jpg"), T0.AddSeconds(3));
        tracker.RecordChange(rootId, rootPath, Path.Combine(rootPath, "Bocchi", "tvshow.nfo"), T0.AddSeconds(4));

        Assert.IsTrue(tracker.HasPending);
        Assert.AreEqual(0, tracker.Drain(T0.AddSeconds(13)).Count, "Still inside the quiet period.");

        var batches = tracker.Drain(T0.AddSeconds(14));
        Assert.AreEqual(1, batches.Count);
        Assert.AreEqual(rootId, batches[0].RootId);
        Assert.IsFalse(batches[0].FullReconciliation);
        CollectionAssert.AreEqual(new[] { "Bocchi", "Frieren" }, batches[0].Folders.ToArray());
        Assert.IsFalse(tracker.HasPending);
        Assert.AreEqual(0, tracker.Drain(T0.AddSeconds(60)).Count);
    }

    [TestMethod]
    public void OverflowAndRootLevelChangesRequestAFullReconciliation()
    {
        var tracker = new LibraryChangeTracker(TimeSpan.FromSeconds(10));
        var rootPath = Path.Combine(Path.GetTempPath(), "anime");
        var overflowRoot = Guid.NewGuid();
        var rootLevelRoot = Guid.NewGuid();
        var renamedRoot = Guid.NewGuid();

        tracker.RecordChange(overflowRoot, rootPath, Path.Combine(rootPath, "Frieren", "a.mkv"), T0);
        tracker.RecordOverflow(overflowRoot, T0.AddSeconds(1));
        tracker.RecordChange(rootLevelRoot, rootPath, rootPath, T0);
        tracker.RecordChange(renamedRoot, rootPath, Path.Combine(rootPath, "Old Name"), T0);
        tracker.RecordChange(renamedRoot, rootPath, Path.Combine(rootPath, "New Name"), T0);

        var batches = tracker.Drain(T0.AddSeconds(20)).ToDictionary(x => x.RootId);
        Assert.IsTrue(batches[overflowRoot].FullReconciliation);
        Assert.AreEqual(0, batches[overflowRoot].Folders.Count);
        Assert.IsTrue(batches[rootLevelRoot].FullReconciliation);
        Assert.IsFalse(batches[renamedRoot].FullReconciliation);
        CollectionAssert.AreEqual(new[] { "New Name", "Old Name" }, batches[renamedRoot].Folders.ToArray());

        Assert.IsNull(LibraryChangeTracker.TopLevelFolder(rootPath, Path.Combine(Path.GetTempPath(), "elsewhere", "x.mkv")));
        Assert.AreEqual("Frieren", LibraryChangeTracker.TopLevelFolder(rootPath, Path.Combine(rootPath, "Frieren", "Season 01", "x.mkv")));
    }

    [TestMethod]
    public void PeriodicReconciliationRespectsIntervalAndBacksOffAfterAnAttempt()
    {
        Assert.IsFalse(LibraryReconciliationSchedule.IsDue(0, null, null, T0), "0 disables the schedule.");
        Assert.IsTrue(LibraryReconciliationSchedule.IsDue(30, null, null, T0), "Never scanned roots are due.");
        Assert.IsFalse(LibraryReconciliationSchedule.IsDue(30, T0.AddMinutes(-10), null, T0));
        Assert.IsTrue(LibraryReconciliationSchedule.IsDue(30, T0.AddMinutes(-30), null, T0));
        Assert.IsFalse(
            LibraryReconciliationSchedule.IsDue(30, T0.AddHours(-5), T0.AddMinutes(-5), T0),
            "A skipped attempt on an offline root backs off for a whole interval.");
        Assert.IsTrue(LibraryReconciliationSchedule.IsDue(30, T0.AddHours(-5), T0.AddMinutes(-31), T0));
    }

    [TestMethod]
    public async Task DebouncedBurstQueuesOnePartialScanOfTheDirtyFolders()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        var root = await host.AddRootAsync("Anime");
        var service = new LibraryWatchService(host.ScopeFactory, host.Scans, NullLogger<LibraryWatchService>.Instance);

        var tracker = new LibraryChangeTracker(TimeSpan.FromSeconds(10));
        for (var i = 0; i < 20; i++)
        {
            tracker.RecordChange(root.Id, host.LibraryPath, Path.Combine(host.LibraryPath, "Frieren", "Season 01", $"part{i}.mkv"), T0.AddMilliseconds(i * 200));
        }

        tracker.RecordChange(root.Id, host.LibraryPath, Path.Combine(host.LibraryPath, "Bocchi", "poster.jpg"), T0.AddSeconds(4));

        foreach (var batch in tracker.Drain(T0.AddSeconds(15)))
        {
            await service.QueueBatchAsync(batch, CancellationToken.None);
        }

        // A second, later batch for a folder of the still queued run is merged into it.
        await service.QueueBatchAsync(new LibraryChangeBatch(root.Id, false, ["Frieren"]), CancellationToken.None);

        var scans = await host.ListScansAsync();
        Assert.AreEqual(1, scans.Count, "A burst becomes exactly one partial scan.");
        var details = LibraryScanDetails.TryParse(scans[0].Details)!;
        Assert.AreEqual(LibraryScanTrigger.Watch, details.Trigger);
        CollectionAssert.AreEqual(new[] { "Bocchi", "Frieren" }, details.Folders!.ToArray());
        Assert.AreEqual("Anime", scans[0].Subject);
        service.Dispose();
    }

    [TestMethod]
    public async Task OverflowBatchQueuesAFullReconciliation()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        var root = await host.AddRootAsync("Anime");
        var service = new LibraryWatchService(host.ScopeFactory, host.Scans, NullLogger<LibraryWatchService>.Instance);

        var tracker = new LibraryChangeTracker(TimeSpan.FromSeconds(10));
        tracker.RecordChange(root.Id, host.LibraryPath, Path.Combine(host.LibraryPath, "Frieren", "a.mkv"), T0);
        tracker.RecordOverflow(root.Id, T0.AddSeconds(1));
        foreach (var batch in tracker.Drain(T0.AddSeconds(20)))
        {
            await service.QueueBatchAsync(batch, CancellationToken.None);
        }

        await service.QueueBatchAsync(new LibraryChangeBatch(root.Id, true, []), CancellationToken.None);

        var scans = await host.ListScansAsync();
        Assert.AreEqual(1, scans.Count);
        var details = LibraryScanDetails.TryParse(scans[0].Details)!;
        Assert.AreEqual(LibraryScanTrigger.Watch, details.Trigger);
        Assert.IsNull(details.Folders, "Lost events are repaired by a whole-root reconciliation.");
        service.Dispose();
    }

    [TestMethod]
    public async Task PeriodicPassQueuesOnlyDueReadableRootsAndNothingForOfflineRoots()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        var due = await host.AddRootAsync("Due");
        var recent = await host.AddRootAsync("Recent", CreateDirectory(host, "recent"));
        await host.AddRootAsync("Off", CreateDirectory(host, "off"), intervalMinutes: 0);
        await host.AddRootAsync("Offline", Path.Combine(host.TempRoot, "missing"));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var root = await db.LibraryRoots.FindAsync(recent.Id);
            root!.LastScannedAt = DateTime.UtcNow.AddMinutes(-10);
            await db.SaveChangesAsync();
        }

        var service = new LibraryWatchService(host.ScopeFactory, host.Scans, NullLogger<LibraryWatchService>.Instance);
        await service.ReconcileDueRootsAsync(CancellationToken.None);
        await service.ReconcileDueRootsAsync(CancellationToken.None);

        var scans = await host.ListScansAsync();
        Assert.AreEqual(1, scans.Count, "Only the due, readable root is reconciled, and only once.");
        var details = LibraryScanDetails.TryParse(scans[0].Details)!;
        Assert.AreEqual(due.Id, details.RootId);
        Assert.AreEqual(LibraryScanTrigger.Periodic, details.Trigger);
        Assert.IsNull(details.Folders);
        service.Dispose();
    }

    [TestMethod]
    public async Task WatcherAttachesToReadableRootsRecordsEventsAndDisposesOnShutdown()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        await host.AddRootAsync("Anime");
        await host.AddRootAsync("Offline", Path.Combine(host.TempRoot, "missing"));

        var service = new LibraryWatchService(host.ScopeFactory, host.Scans, NullLogger<LibraryWatchService>.Instance);
        await service.StartAsync(CancellationToken.None);

        await WaitUntilAsync(() => service.WatchedRootCount == 1, "one watcher for the readable root");
        Assert.IsFalse(service.HasPendingChanges);

        host.WriteMedia(Path.Combine("Frieren", "Season 01", "Frieren - S01E01.mkv"));
        await WaitUntilAsync(() => service.HasPendingChanges, "a recorded filesystem change");

        await service.StopAsync(CancellationToken.None);
        service.Dispose();
        Assert.AreEqual(0, service.WatchedRootCount);
    }

    private static string CreateDirectory(LibraryScanTestHost host, string name)
    {
        var path = Path.Combine(host.TempRoot, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string expectation)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"Timed out waiting for {expectation}.");
    }
}
