using AniLingo.Web.Data;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AniLingo.Tests;

[TestClass]
public sealed class LibraryScanLifecycleTests
{
    [TestMethod]
    public async Task QueuedScanPersistsCountersPhaseAndRootRelativeWarnings()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        var root = await host.AddRootAsync("Anime");
        host.WriteMedia(Path.Combine("Frieren", "Season 01", "Frieren - S01E01.mkv"));
        host.WriteMedia(Path.Combine("Frieren", "Season 01", "readme.mkv"));
        await File.WriteAllTextAsync(
            Path.Combine(host.LibraryPath, "Frieren", "tvshow.nfo"),
            "<tvshow><title>Broken");

        await host.StartWorkerAsync();
        var queued = await host.Scans.QueueAsync(
            new LibraryScanRequest(root.Id, LibraryScanTrigger.Manual, ProfileId: "owner"));

        Assert.AreEqual(LibraryScanQueueOutcome.Queued, queued.Outcome);
        Assert.IsNotNull(queued.OperationId);

        var operation = await host.WaitForAsync(
            queued.OperationId.Value,
            x => x.Status == OperationStatus.Succeeded);

        Assert.AreEqual(LibraryScanCoordinator.OperationKind, operation.Kind);
        Assert.AreEqual(OperationLane.Maintenance, operation.Lane);
        Assert.AreEqual(100, operation.ProgressPercent);
        Assert.AreEqual("owner", operation.ProfileId);
        StringAssert.Contains(operation.Message, "1 added");

        var details = LibraryScanDetails.TryParse(operation.Details);
        Assert.IsNotNull(details);
        Assert.AreEqual(root.Id, details.RootId);
        Assert.IsNull(details.Folder);
        Assert.AreEqual(LibraryScanTrigger.Manual, details.Trigger);
        Assert.AreEqual(LibraryScanPhase.Completed, details.Phase);
        Assert.AreEqual(2, details.FilesTotal);
        Assert.AreEqual(2, details.FilesProcessed);
        Assert.AreEqual(2, details.Warnings);
        Assert.IsNotNull(details.Counters);
        Assert.AreEqual(2, details.Counters.MediaFiles);
        Assert.AreEqual(1, details.Counters.Discovered);
        Assert.AreEqual(1, details.Counters.Skipped);
        Assert.AreEqual(1, details.Counters.Metadata);
        Assert.AreEqual(0, details.Counters.Removed);
        Assert.AreEqual(0, details.Counters.Errors);

        var logs = await host.ListLogsAsync(operation.Id);
        var warnings = logs.Where(x => x.Level == OperationLogLevel.Warning && x.Module == "Scan").ToArray();
        Assert.AreEqual(2, warnings.Length);
        Assert.IsTrue(warnings.Any(x => x.Message == "Unmatched media file: Frieren/Season 01/readme.mkv"));
        Assert.IsTrue(warnings.Any(x => x.Message == "Ignored NFO: Frieren/tvshow.nfo"));
        Assert.IsFalse(
            logs.Any(x => x.Message.Contains(host.TempRoot, StringComparison.OrdinalIgnoreCase)),
            "Operation logs must never contain the host path of the root.");
        Assert.IsFalse(
            (operation.Details ?? "").Contains(host.TempRoot, StringComparison.OrdinalIgnoreCase));

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.LibraryRoots.AsNoTracking().SingleAsync(x => x.Id == root.Id);
        Assert.IsNotNull(stored.LastScannedAt);
        Assert.AreEqual(1, await db.MediaFiles.CountAsync());
    }

    [TestMethod]
    public async Task FailedScanRecordsSafeErrorWithoutHostPath()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        var root = await host.AddRootAsync("Anime");

        // Established media plus an empty root trips the mass-deletion guard.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var anime = new Anime { Key = "frieren", Title = "Frieren" };
            var episode = new Episode { AnimeId = anime.Id, Number = 1, Title = "Episode 1" };
            db.Anime.Add(anime);
            db.Episodes.Add(episode);
            db.MediaFiles.Add(new MediaFile
            {
                LibraryRootId = root.Id,
                EpisodeId = episode.Id,
                Path = Path.Combine(host.LibraryPath, "Frieren", "Season 01", "Frieren - S01E01.mkv"),
                SizeBytes = 1,
                LastWriteTimeUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await host.StartWorkerAsync();
        var queued = await host.Scans.QueueAsync(
            new LibraryScanRequest(root.Id, LibraryScanTrigger.Watch));
        Assert.AreEqual(LibraryScanQueueOutcome.Queued, queued.Outcome);

        var operation = await host.WaitForAsync(
            queued.OperationId!.Value,
            x => x.Status == OperationStatus.Failed);

        StringAssert.Contains(operation.Error, "mass deletion");
        Assert.IsFalse(operation.Error!.Contains(host.TempRoot, StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(operation.Retryable);
        Assert.AreEqual(0, host.Scans.GetActive(root.Id).Count, "The guard must be released after a failure.");
    }

    [TestMethod]
    public async Task DuplicateScanForSameRootIsCoalescedWhileOtherRootsQueue()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        var first = await host.AddRootAsync("Anime");
        var otherPath = Path.Combine(host.TempRoot, "movies");
        Directory.CreateDirectory(otherPath);
        var second = await host.AddRootAsync("Movies", otherPath);

        // No worker: the first request stays queued and therefore active.
        var manual = await host.Scans.QueueAsync(new LibraryScanRequest(first.Id, LibraryScanTrigger.Manual));
        var duplicate = await host.Scans.QueueAsync(new LibraryScanRequest(first.Id, LibraryScanTrigger.Manual));
        var startup = await host.Scans.QueueAsync(new LibraryScanRequest(first.Id, LibraryScanTrigger.Startup));
        var folder = await host.Scans.QueueAsync(new LibraryScanRequest(first.Id, LibraryScanTrigger.Watch, "Frieren"));
        var other = await host.Scans.QueueAsync(new LibraryScanRequest(second.Id, LibraryScanTrigger.Manual));

        Assert.AreEqual(LibraryScanQueueOutcome.Queued, manual.Outcome);
        Assert.AreEqual(LibraryScanQueueOutcome.AlreadyActive, duplicate.Outcome);
        Assert.AreEqual(manual.OperationId, duplicate.OperationId);
        Assert.AreEqual(LibraryScanQueueOutcome.AlreadyActive, startup.Outcome);
        Assert.AreEqual(LibraryScanQueueOutcome.AlreadyActive, folder.Outcome);
        Assert.AreEqual(LibraryScanQueueOutcome.Queued, other.Outcome);

        var scans = await host.ListScansAsync();
        Assert.AreEqual(2, scans.Count);
        Assert.AreEqual(1, host.Scans.GetActive(first.Id).Count);
        Assert.AreEqual(1, host.Scans.GetActive(second.Id).Count);
    }

    [TestMethod]
    public async Task FolderScansCoalescePerFolderAndAreAbsorbedByAFullScan()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        var root = await host.AddRootAsync("Anime");

        var frieren = await host.Scans.QueueAsync(new LibraryScanRequest(root.Id, LibraryScanTrigger.Watch, "Frieren"));
        var frierenAgain = await host.Scans.QueueAsync(new LibraryScanRequest(root.Id, LibraryScanTrigger.Watch, "Frieren/"));
        var bocchi = await host.Scans.QueueAsync(new LibraryScanRequest(root.Id, LibraryScanTrigger.Watch, "Bocchi"));
        var full = await host.Scans.QueueAsync(new LibraryScanRequest(root.Id, LibraryScanTrigger.Periodic));
        var lateFolder = await host.Scans.QueueAsync(new LibraryScanRequest(root.Id, LibraryScanTrigger.Watch, "Gintama"));

        Assert.AreEqual(LibraryScanQueueOutcome.Queued, frieren.Outcome);
        Assert.AreEqual(LibraryScanQueueOutcome.AlreadyActive, frierenAgain.Outcome);
        Assert.AreEqual(LibraryScanQueueOutcome.Queued, bocchi.Outcome);
        Assert.AreEqual(LibraryScanQueueOutcome.Queued, full.Outcome);
        Assert.AreEqual(LibraryScanQueueOutcome.AlreadyActive, lateFolder.Outcome);
        Assert.AreEqual(full.OperationId, lateFolder.OperationId);

        var scans = await host.ListScansAsync();
        var folders = scans
            .Select(x => LibraryScanDetails.TryParse(x.Details)?.Folder)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(new string?[] { null, "Bocchi", "Frieren" }, folders);
    }

    [TestMethod]
    public async Task RestartMarksRunningScanInterruptedAndRunAgainQueuesAFreshScan()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        var root = await host.AddRootAsync("Anime");
        host.WriteMedia(Path.Combine("Frieren", "Season 01", "Frieren - S01E01.mkv"));

        // A run from the previous process: persisted as Running, its delegate is gone.
        Guid abandonedId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var store = new OperationStore(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            abandonedId = await store.CreateAsync(new OperationDescriptor(
                LibraryScanCoordinator.OperationKind,
                LibraryScanCoordinator.OperationCategory,
                "Library scan",
                root.Name,
                Lane: OperationLane.Maintenance,
                Details: new LibraryScanDetails(
                    root.Id, null, LibraryScanTrigger.Manual, LibraryScanPhase.Reconciling, 3, 10, 0, null).Serialize()));
            await store.MarkRunningAsync(abandonedId);
        }

        await host.StartWorkerAsync();
        var interrupted = await host.WaitForAsync(abandonedId, x => x.Status == OperationStatus.Interrupted);
        Assert.AreEqual("Interrupted by server restart.", interrupted.Message);
        Assert.IsFalse(host.Queue.HasRuntimeWork(abandonedId));

        var retry = await host.Scans.RetryAsync(abandonedId, "owner");
        Assert.IsNotNull(retry);
        Assert.AreEqual(LibraryScanQueueOutcome.Queued, retry.Outcome);
        Assert.AreNotEqual(abandonedId, retry.OperationId);

        var fresh = await host.WaitForAsync(retry.OperationId!.Value, x => x.Status == OperationStatus.Succeeded);
        var details = LibraryScanDetails.TryParse(fresh.Details);
        Assert.IsNotNull(details);
        Assert.AreEqual(root.Id, details.RootId);
        Assert.AreEqual(LibraryScanTrigger.Retry, details.Trigger);
        Assert.AreEqual(1, details.Counters!.Discovered);

        var unchanged = await host.GetOperationAsync(abandonedId);
        Assert.AreEqual(OperationStatus.Interrupted, unchanged!.Status);
        Assert.IsNull(await host.Scans.RetryAsync(Guid.NewGuid(), null));
    }

    [TestMethod]
    public async Task FolderScanTouchesOnlyItsSubtreeAndMatchesAFullScan()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        var root = await host.AddRootAsync("Anime");
        host.WriteMedia(Path.Combine("Frieren", "Season 01", "Frieren - S01E01.mkv"));
        var bocchiPath = Path.GetFullPath(host.WriteMedia(Path.Combine("Bocchi", "Season 01", "Bocchi - S01E01.mkv")));

        await using var scope = host.Services.CreateAsyncScope();
        var scanner = scope.ServiceProvider.GetRequiredService<LibraryScanner>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var initial = await scanner.ScanAsync(root.Id, CancellationToken.None);
        Assert.AreEqual(2, initial.Discovered);
        var scannedAt = (await db.LibraryRoots.AsNoTracking().SingleAsync(x => x.Id == root.Id)).LastScannedAt;

        // Change both subtrees, then reconcile only Frieren.
        host.WriteMedia(Path.Combine("Frieren", "Season 01", "Frieren - S01E02.mkv"));
        File.Delete(bocchiPath);

        var phases = new List<LibraryScanPhase>();
        var folderResult = await scanner.ScanFolderAsync(
            root.Id,
            "Frieren",
            (progress, _) =>
            {
                phases.Add(progress.Phase);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.AreEqual(1, folderResult.Discovered);
        Assert.AreEqual(0, folderResult.Removed);
        Assert.AreEqual(2, folderResult.MediaFiles);
        CollectionAssert.Contains(phases, LibraryScanPhase.Enumerating);
        CollectionAssert.Contains(phases, LibraryScanPhase.Reconciling);
        Assert.AreEqual(LibraryScanPhase.Completed, phases[^1]);

        db.ChangeTracker.Clear();
        Assert.AreEqual(3, await db.MediaFiles.CountAsync(), "Bocchi's stale file is outside the scanned folder and must remain.");
        Assert.AreEqual(1, await db.MediaFiles.CountAsync(x => x.Path == bocchiPath));
        Assert.AreEqual(2, await db.Anime.CountAsync());
        Assert.AreEqual(
            scannedAt,
            (await db.LibraryRoots.AsNoTracking().SingleAsync(x => x.Id == root.Id)).LastScannedAt,
            "A folder scan is not a full reconciliation and must not move LastScannedAt.");

        // A full scan afterwards finds nothing left to do for Frieren and only repairs Bocchi.
        var full = await scanner.ScanAsync(root.Id, CancellationToken.None);
        Assert.AreEqual(0, full.Discovered);
        Assert.AreEqual(0, full.Updated);
        Assert.AreEqual(1, full.Removed);

        db.ChangeTracker.Clear();
        var frieren = await db.Anime.SingleAsync(x => x.Key == "frieren");
        var episodes = await db.Episodes.Where(x => x.AnimeId == frieren.Id).OrderBy(x => x.Number).ToListAsync();
        CollectionAssert.AreEqual(new[] { 1, 2 }, episodes.Select(x => x.Number).ToArray());

        // A deleted folder reconciles as removal of its media, a traversal attempt is rejected.
        Directory.Delete(Path.Combine(host.LibraryPath, "Frieren"), recursive: true);
        var removed = await scanner.ScanFolderAsync(root.Id, "Frieren", null, CancellationToken.None);
        Assert.AreEqual(2, removed.Removed);
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => scanner.ScanFolderAsync(root.Id, "../outside", null, CancellationToken.None));
    }

    [TestMethod]
    public async Task ScansAreNotQueuedForUnavailableOrDisabledRoots()
    {
        await using var host = await LibraryScanTestHost.CreateAsync(createLibraryDirectory: false);
        var offline = await host.AddRootAsync("Offline");

        var periodic = await host.Scans.QueueAsync(new LibraryScanRequest(offline.Id, LibraryScanTrigger.Periodic));
        Assert.AreEqual(LibraryScanQueueOutcome.RootUnavailable, periodic.Outcome);
        Assert.AreEqual(0, (await host.ListScansAsync()).Count, "An offline root must not produce an operation per attempt.");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var root = await db.LibraryRoots.SingleAsync(x => x.Id == offline.Id);
            root.IsEnabled = false;
            await db.SaveChangesAsync();
        }

        var disabled = await host.Scans.QueueAsync(new LibraryScanRequest(offline.Id, LibraryScanTrigger.Manual));
        Assert.AreEqual(LibraryScanQueueOutcome.RootDisabled, disabled.Outcome);

        var missing = await host.Scans.QueueAsync(new LibraryScanRequest(Guid.NewGuid(), LibraryScanTrigger.Manual));
        Assert.AreEqual(LibraryScanQueueOutcome.RootNotFound, missing.Outcome);
    }

    [TestMethod]
    public async Task ScanHistoryIsBoundedPerKind()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();

        await using var scope = host.Services.CreateAsyncScope();
        var store = new OperationStore(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        var finished = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var id = await store.CreateAsync(new OperationDescriptor(
                LibraryScanCoordinator.OperationKind,
                LibraryScanCoordinator.OperationCategory,
                $"Scan {i}",
                Lane: OperationLane.Maintenance));
            await store.MarkRunningAsync(id);
            await store.MarkSucceededAsync(id);
            finished.Add(id);
            await Task.Delay(5);
        }

        var running = await store.CreateAsync(new OperationDescriptor(
            LibraryScanCoordinator.OperationKind,
            LibraryScanCoordinator.OperationCategory,
            "Active scan",
            Lane: OperationLane.Maintenance));
        await store.MarkRunningAsync(running);
        var unrelated = await store.CreateAsync(new OperationDescriptor("other", "Task", "Other"));
        await store.MarkSucceededAsync(unrelated);

        await store.PruneFinishedAsync(LibraryScanCoordinator.OperationKind, keep: 2);

        var scans = await store.ListAsync(new OperationListFilter(Kind: LibraryScanCoordinator.OperationKind));
        Assert.AreEqual(3, scans.Count);
        Assert.IsTrue(scans.Any(x => x.Id == running));
        Assert.IsTrue(scans.Any(x => x.Title == "Scan 4"));
        Assert.IsTrue(scans.Any(x => x.Title == "Scan 3"));
        Assert.IsNotNull(await store.GetAsync(unrelated));
        Assert.IsNull(await store.GetAsync(finished[0]));
        Assert.AreEqual(0, (await store.ListLogsAsync(new OperationLogFilter(OperationId: finished[0]))).Count);
        Assert.AreNotEqual(0, (await store.ListLogsAsync(new OperationLogFilter(OperationId: finished[4]))).Count);
    }
}
