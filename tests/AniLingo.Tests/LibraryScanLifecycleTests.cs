using AniLingo.Web.Features.Acquisition.Import;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Acquisition.Naming;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Pages.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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
        Assert.IsNull(details.Folders);
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
        var periodic = await host.Scans.QueueAsync(new LibraryScanRequest(first.Id, LibraryScanTrigger.Periodic));
        var folder = await host.Scans.QueueAsync(new LibraryScanRequest(first.Id, LibraryScanTrigger.Watch, "Frieren"));
        var other = await host.Scans.QueueAsync(new LibraryScanRequest(second.Id, LibraryScanTrigger.Manual));

        Assert.AreEqual(LibraryScanQueueOutcome.Queued, manual.Outcome);
        foreach (var coalesced in new[] { duplicate, startup, periodic, folder })
        {
            Assert.AreEqual(LibraryScanQueueOutcome.Merged, coalesced.Outcome);
            Assert.AreEqual(manual.OperationId, coalesced.OperationId);
        }

        Assert.AreEqual(LibraryScanQueueOutcome.Queued, other.Outcome);

        var scans = await host.ListScansAsync();
        Assert.AreEqual(2, scans.Count);
        var firstRun = scans.Single(x => x.Id == manual.OperationId);
        Assert.IsNull(LibraryScanDetails.TryParse(firstRun.Details)!.Folders, "A whole-root run stays whole-root.");
        Assert.AreEqual(1, host.Scans.GetActive(first.Id).Count);
        Assert.AreEqual(1, host.Scans.GetActive(second.Id).Count);
    }

    [TestMethod]
    public async Task QueuedRunMergesFoldersAndWidensToTheWholeRoot()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        var root = await host.AddRootAsync("Anime");

        var frieren = await host.Scans.QueueAsync(new LibraryScanRequest(root.Id, LibraryScanTrigger.Watch, "Frieren"));
        var frierenAgain = await host.Scans.QueueAsync(new LibraryScanRequest(root.Id, LibraryScanTrigger.Watch, "Frieren/"));
        var bocchi = await host.Scans.QueueAsync(new LibraryScanRequest(root.Id, LibraryScanTrigger.Watch, "Bocchi"));

        Assert.AreEqual(LibraryScanQueueOutcome.Queued, frieren.Outcome);
        Assert.AreEqual(LibraryScanQueueOutcome.Merged, frierenAgain.Outcome);
        Assert.AreEqual(LibraryScanQueueOutcome.Merged, bocchi.Outcome);
        Assert.AreEqual(frieren.OperationId, bocchi.OperationId);

        var run = (await host.ListScansAsync()).Single();
        CollectionAssert.AreEqual(
            new[] { "Bocchi", "Frieren" },
            LibraryScanDetails.TryParse(run.Details)!.Folders!.ToArray(),
            "The queued run's persisted scope lists every merged folder.");

        var full = await host.Scans.QueueAsync(new LibraryScanRequest(root.Id, LibraryScanTrigger.Periodic));
        var lateFolder = await host.Scans.QueueAsync(new LibraryScanRequest(root.Id, LibraryScanTrigger.Watch, "Gintama"));
        Assert.AreEqual(LibraryScanQueueOutcome.Merged, full.Outcome);
        Assert.AreEqual(LibraryScanQueueOutcome.Merged, lateFolder.Outcome);

        run = (await host.ListScansAsync()).Single();
        Assert.IsNull(LibraryScanDetails.TryParse(run.Details)!.Folders, "A whole-root request widens the queued run.");
        Assert.IsNull(host.Scans.GetActive(root.Id).Single().Folders);
    }

    [TestMethod]
    public async Task MergedFoldersRunInOneOperationLikeSeparateFolderScans()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        var root = await host.AddRootAsync("Anime");
        host.WriteMedia(Path.Combine("Frieren", "Season 01", "Frieren - S01E01.mkv"));
        host.WriteMedia(Path.Combine("Bocchi", "Season 01", "Bocchi - S01E01.mkv"));
        host.WriteMedia(Path.Combine("Bocchi", "Season 01", "extras.mkv"));
        host.WriteMedia(Path.Combine("Gintama", "Season 01", "Gintama - S01E01.mkv"));

        var first = await host.Scans.QueueAsync(new LibraryScanRequest(root.Id, LibraryScanTrigger.Watch, "Frieren"));
        await host.Scans.QueueAsync(new LibraryScanRequest(root.Id, LibraryScanTrigger.Watch, "Bocchi"));
        await host.StartWorkerAsync();

        var operation = await host.WaitForAsync(first.OperationId!.Value, x => x.Status == OperationStatus.Succeeded);
        var details = LibraryScanDetails.TryParse(operation.Details)!;
        CollectionAssert.AreEqual(new[] { "Bocchi", "Frieren" }, details.Folders!.ToArray());
        Assert.AreEqual(3, details.Counters!.MediaFiles);
        Assert.AreEqual(2, details.Counters.Discovered);
        Assert.AreEqual(1, details.Counters.Skipped);
        Assert.AreEqual(1, details.Warnings);

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.AreEqual(2, await db.MediaFiles.CountAsync(), "Gintama is outside the merged folders.");
        Assert.IsNull((await db.LibraryRoots.AsNoTracking().SingleAsync(x => x.Id == root.Id)).LastScannedAt);
    }

    [TestMethod]
    public async Task RunningScanRejectsRequestsAndTheWatcherKeepsTheChangePending()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        var root = await host.AddRootAsync("Anime");
        host.WriteMedia(Path.Combine("Frieren", "Season 01", "Frieren - S01E01.mkv"));

        // Hold the scan inside its media-analysis phase.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Probe.Gate = gate.Task;
        await host.StartWorkerAsync();
        var running = await host.Scans.QueueAsync(new LibraryScanRequest(root.Id, LibraryScanTrigger.Manual));
        await host.WaitForAsync(running.OperationId!.Value, _ => host.Probe.Calls.Count > 0);
        Assert.IsTrue(host.Scans.GetActive(root.Id).Single().IsRunning);

        var manual = await host.Scans.QueueAsync(new LibraryScanRequest(root.Id, LibraryScanTrigger.Manual));
        Assert.AreEqual(LibraryScanQueueOutcome.AlreadyActive, manual.Outcome);
        Assert.AreEqual(running.OperationId, manual.OperationId);

        var watcher = new LibraryWatchService(host.ScopeFactory, host.Scans, NullLogger<LibraryWatchService>.Instance);
        await watcher.QueueBatchAsync(new LibraryChangeBatch(root.Id, false, ["Frieren"]), CancellationToken.None);
        Assert.IsTrue(watcher.HasPendingChanges, "A change seen during a running scan must be offered again later.");
        Assert.AreEqual(1, (await host.ListScansAsync()).Count);

        gate.SetResult();
        await host.WaitForAsync(running.OperationId.Value, x => x.Status == OperationStatus.Succeeded);
        await WaitUntilReleasedAsync(host, root.Id);

        var next = await host.Scans.QueueAsync(new LibraryScanRequest(root.Id, LibraryScanTrigger.Watch, "Frieren"));
        Assert.AreEqual(LibraryScanQueueOutcome.Queued, next.Outcome);
        watcher.Dispose();
    }

    [TestMethod]
    [DataRow(AnimeRenameService.OperationKind, "Rename files: Frieren")]
    [DataRow(AnimeImportExecutor.OperationKind, "Import Frieren S01E02")]
    public async Task ScanWaitsForARunningFileMoveBeforeTouchingTheLibrary(string kind, string title)
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        var root = await host.AddRootAsync("Anime");
        host.WriteMedia(Path.Combine("Frieren", "Season 01", "Frieren - S01E01.mkv"));

        // The rename/import belongs to the current process, so lane recovery leaves it running.
        await host.StartWorkerAsync();
        Guid moveId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var store = new OperationStore(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            moveId = await store.CreateAsync(new OperationDescriptor(
                kind,
                LibraryScanCoordinator.OperationCategory,
                title,
                Retryable: false));
            await store.MarkRunningAsync(moveId);
        }

        var queued = await host.Scans.QueueAsync(new LibraryScanRequest(root.Id, LibraryScanTrigger.Manual));
        var waiting = await host.WaitForAsync(
            queued.OperationId!.Value,
            x => x.Message == "Waiting for a file rename or import to finish.");
        Assert.AreEqual(OperationStatus.Running, waiting.Status);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.AreEqual(0, await db.MediaFiles.CountAsync(), "Nothing is reconciled while files are being moved.");
            await new OperationStore(db).MarkSucceededAsync(moveId);
        }

        var done = await host.WaitForAsync(queued.OperationId.Value, x => x.Status == OperationStatus.Succeeded);
        Assert.AreEqual(1, LibraryScanDetails.TryParse(done.Details)!.Counters!.Discovered);
    }

    [TestMethod]
    public async Task CancelledQueuedRunNoLongerBlocksTheRoot()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        var root = await host.AddRootAsync("Anime");

        var queued = await host.Scans.QueueAsync(new LibraryScanRequest(root.Id, LibraryScanTrigger.Manual));
        Assert.IsTrue(await host.Queue.CancelAsync(queued.OperationId!.Value));

        var next = await host.Scans.QueueAsync(new LibraryScanRequest(root.Id, LibraryScanTrigger.Manual));
        Assert.AreEqual(LibraryScanQueueOutcome.Queued, next.Outcome);
        Assert.AreNotEqual(queued.OperationId, next.OperationId);
    }

    private static async Task WaitUntilReleasedAsync(LibraryScanTestHost host, Guid rootId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (host.Scans.GetActive(rootId).Count > 0)
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, "The root guard was not released.");
            await Task.Delay(20);
        }
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
    public async Task FolderScanAnalysesOnlyTheMediaOfItsSubtree()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        var root = await host.AddRootAsync("Anime");
        var frieren1 = Path.GetFullPath(host.WriteMedia(Path.Combine("Frieren", "Season 01", "Frieren - S01E01.mkv")));
        host.WriteMedia(Path.Combine("Bocchi", "Season 01", "Bocchi - S01E01.mkv"));

        await using var scope = host.Services.CreateAsyncScope();
        var scanner = scope.ServiceProvider.GetRequiredService<LibraryScanner>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var full = await scanner.ScanAsync(root.Id, CancellationToken.None);
        Assert.AreEqual(2, host.Probe.Calls.Count);
        Assert.AreEqual(2, full.MediaInventory.Failed, "The fake probe rejects every file like invalid media.");

        // Without analyses every file of the root would need a probe; a folder scan must only
        // probe inside its folder.
        await db.MediaAnalyses.ExecuteDeleteAsync();
        host.Probe.ClearCalls();
        var frieren2 = Path.GetFullPath(host.WriteMedia(Path.Combine("Frieren", "Season 01", "Frieren - S01E02.mkv")));

        var folder = await scanner.ScanFolderAsync(root.Id, "Frieren", null, CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] { frieren1, frieren2 }, host.Probe.Calls.ToArray());
        Assert.AreEqual(2, folder.MediaInventory.Failed);
    }

    [TestMethod]
    public async Task FolderScanOfAnEmptyMountPointIsRejectedInsteadOfDeletingMedia()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        var root = await host.AddRootAsync("Anime");
        host.WriteMedia(Path.Combine("Frieren", "Season 01", "Frieren - S01E01.mkv"));

        await using var scope = host.Services.CreateAsyncScope();
        var scanner = scope.ServiceProvider.GetRequiredService<LibraryScanner>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await scanner.ScanAsync(root.Id, CancellationToken.None);

        // The NAS unmounted: the mount point still exists but is empty.
        Directory.Delete(Path.Combine(host.LibraryPath, "Frieren"), recursive: true);

        await Assert.ThrowsExactlyAsync<IOException>(
            () => scanner.ScanFolderAsync(root.Id, "Frieren", null, CancellationToken.None));
        db.ChangeTracker.Clear();
        Assert.AreEqual(1, await db.MediaFiles.CountAsync());
    }

    [TestMethod]
    public async Task LaneRecoveryOnlyAbandonsWorkOfThePreviousProcess()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();

        await using var scope = host.Services.CreateAsyncScope();
        var store = new OperationStore(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        var abandoned = await store.CreateAsync(new OperationDescriptor(
            LibraryScanCoordinator.OperationKind,
            LibraryScanCoordinator.OperationCategory,
            "Previous process",
            Lane: OperationLane.Maintenance));
        await Task.Delay(20);
        var processStartedUtc = DateTime.UtcNow;
        await Task.Delay(20);

        // Queued by the current process (for example the startup scan) before recovery ran.
        var current = await store.CreateAsync(new OperationDescriptor(
            LibraryScanCoordinator.OperationKind,
            LibraryScanCoordinator.OperationCategory,
            "Current process",
            Lane: OperationLane.Maintenance));

        var recovered = await store.RecoverInterruptedAsync(OperationLane.Maintenance, processStartedUtc);

        Assert.AreEqual(1, recovered);
        Assert.AreEqual(OperationStatus.Interrupted, (await store.GetAsync(abandoned))!.Status);
        Assert.AreEqual(OperationStatus.Queued, (await store.GetAsync(current))!.Status);
    }

    [TestMethod]
    public async Task ScanHistoryPageShowsCountersPerRoot()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        var root = await host.AddRootAsync("Anime");
        host.WriteMedia(Path.Combine("Frieren", "Season 01", "Frieren - S01E01.mkv"));
        host.WriteMedia(Path.Combine("Frieren", "notes.mkv"));

        await host.StartWorkerAsync();
        var queued = await host.Scans.QueueAsync(new LibraryScanRequest(root.Id, LibraryScanTrigger.Manual));
        await host.WaitForAsync(queued.OperationId!.Value, x => x.Status == OperationStatus.Succeeded);

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var page = new ScansModel(db, host.Scans, new CurrentAccountContext(new HttpContextAccessor()))
        {
            RootId = root.Id
        };
        var pageHttpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton<IModelMetadataProvider, EmptyModelMetadataProvider>()
                .BuildServiceProvider()
        };
        page.PageContext = new PageContext
        {
            HttpContext = pageHttpContext,
            ViewData = new ViewDataDictionary<ScansModel>(
                new EmptyModelMetadataProvider(),
                new ModelStateDictionary())
        };
        await page.OnGetAsync(CancellationToken.None);

        var row = page.Scans.Single();
        Assert.AreEqual("Anime", row.RootName);
        Assert.AreEqual("Whole root", row.Scope);
        Assert.AreEqual("Manual", row.Trigger);
        Assert.AreEqual("Completed", row.Phase);
        Assert.IsTrue(row.CanRunAgain);
        Assert.IsNotNull(row.Counters);
        Assert.AreEqual(2, row.Counters.MediaFiles);
        Assert.AreEqual(1, row.Counters.Discovered);
        Assert.AreEqual(1, row.Counters.Skipped);
        Assert.AreEqual(1, row.Warnings);

        page.RootId = Guid.NewGuid();
        await page.OnGetAsync(CancellationToken.None);
        Assert.AreEqual(0, page.Scans.Count);
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
