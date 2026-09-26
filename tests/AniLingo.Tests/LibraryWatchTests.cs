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
    public async Task OverflowBatchQueuesOneFullWatchScanAndFolderBatchesCoalesce()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        var root = await host.AddRootAsync("Anime");
        var service = new LibraryWatchService(host.ScopeFactory, host.Scans, NullLogger<LibraryWatchService>.Instance);

        await service.QueueBatchAsync(new LibraryChangeBatch(root.Id, false, ["Frieren", "Bocchi"]), CancellationToken.None);
        await service.QueueBatchAsync(new LibraryChangeBatch(root.Id, false, ["Frieren"]), CancellationToken.None);
        await service.QueueBatchAsync(new LibraryChangeBatch(root.Id, true, []), CancellationToken.None);
        await service.QueueBatchAsync(new LibraryChangeBatch(root.Id, true, []), CancellationToken.None);

        var scans = await host.ListScansAsync();
        var details = scans.Select(x => LibraryScanDetails.TryParse(x.Details)!).ToArray();
        Assert.AreEqual(3, scans.Count);
        Assert.IsTrue(details.All(x => x.Trigger == LibraryScanTrigger.Watch));
        CollectionAssert.AreEquivalent(new string?[] { "Frieren", "Bocchi", null }, details.Select(x => x.Folder).ToArray());
        Assert.IsTrue(scans.All(x => x.Subject!.StartsWith("Anime", StringComparison.Ordinal)));
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
