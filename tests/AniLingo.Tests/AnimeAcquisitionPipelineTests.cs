using AniLingo.Web.Features.Acquisition;
using AniLingo.Web.Features.Acquisition.Import;
using AniLingo.Web.Features.Acquisition.Monitoring;
using AniLingo.Web.Features.Acquisition.Ownership;
using AniLingo.Web.Features.Acquisition.Pipeline;
using AniLingo.Web.Features.Operations;

namespace AniLingo.Tests;

[TestClass]
public sealed class AnimeAcquisitionPipelineTests
{
    private const string Best = "Frieren.S01E02.1080p.WEB-DL.AAC.H.264-GRP";
    private const string Lower = "Frieren.S01E02.720p.WEB-DL.AAC.H.264-GRP";
    private const string Rejected = "Frieren.S01E02.480p.WEB-DL.AAC.H.264-GRP";
    private const string OtherSeries = "Dungeon.Meshi.S01E02.1080p.WEB-DL.AAC.H.264-GRP";

    [TestMethod]
    public async Task ScheduledRunGrabsBestAcceptedReleaseOnceAndRecordsEveryDecision()
    {
        await using var environment = await AnimeAcquisitionEnvironment.CreateAsync();
        await environment.SeedFrierenAsync();
        AddStandardReleases(environment);

        var first = await environment.Scheduler.RunNowAsync(null, AnimeSearchTrigger.PeriodicMissing, CancellationToken.None);

        Assert.AreEqual(1, first.Grabs, first.ToString());
        var grab = environment.Sabnzbd.Grabs.Single();
        Assert.AreEqual(Best, grab.NzbName);
        Assert.AreEqual("anime", grab.Category);

        var acquisition = (await environment.Acquisitions.LoadAsync()).Acquisitions.Single();
        Assert.AreEqual(new AnimeEpisodeKey(AnimeAcquisitionEnvironment.AnimeKey, 1, 2, 2), acquisition.Episodes.Single());
        Assert.AreEqual(1, acquisition.PendingCandidates.Length, "The lower accepted release stays as the fallback candidate.");
        var monitoring = await environment.MonitoringStateAsync();
        Assert.AreEqual(AnimeAcquisitionAttemptStatus.Grabbed, monitoring.Attempts["frieren:S01E02"].Status);
        var job = (await environment.Ownership.LoadAsync()).Jobs[acquisition.Id.ToString()];
        Assert.AreEqual(AcquisitionOwner.AniLingo, job.Owner);
        Assert.AreEqual(AcquisitionOwnershipStatus.Pending, job.Status);

        var decisions = (await environment.LogsAsync(AnimeAcquisitionPipeline.LogModule)).Select(entry => entry.Message).ToArray();
        Assert.IsTrue(decisions.Any(message => message.StartsWith($"Accepted: {Best}", StringComparison.Ordinal)), string.Join(Environment.NewLine, decisions));
        Assert.IsTrue(decisions.Any(message => message.StartsWith($"Rejected: {Rejected}", StringComparison.Ordinal) && message.Contains("quality profile", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(decisions.Any(message => message.StartsWith($"Rejected: {OtherSeries}", StringComparison.Ordinal) && message.Contains("does not match this anime", StringComparison.Ordinal)));
        Assert.IsTrue(decisions.Any(message => message.StartsWith("Sent to SABnzbd", StringComparison.Ordinal)));

        var second = await environment.Scheduler.RunNowAsync(null, AnimeSearchTrigger.PeriodicMissing, CancellationToken.None);
        Assert.AreEqual(0, second.Grabs);
        Assert.AreEqual(0, second.Searches, "A grabbed episode is not searched again.");
        Assert.AreEqual(1, environment.Sabnzbd.Grabs.Count);
    }

    [TestMethod]
    public async Task SonarrOwnedAnimeAndReleasesAreNeverGrabbed()
    {
        await using var environment = await AnimeAcquisitionEnvironment.CreateAsync();
        await environment.SeedFrierenAsync(mode: null);
        AddStandardReleases(environment);

        // Default mode: Sonarr keeps the anime (read-only coexistence).
        var readOnly = await environment.Scheduler.RunNowAsync(null, AnimeSearchTrigger.PeriodicMissing, CancellationToken.None);
        Assert.AreEqual(0, readOnly.Searches);
        Assert.AreEqual(0, environment.Prowlarr.Queries.Count, "No indexer search for an anime Sonarr manages.");
        Assert.IsTrue(readOnly.Notes.Any(note => note.Contains("read-only Sonarr coexistence", StringComparison.Ordinal)));

        // Parallel mode, but Sonarr owns both acceptable releases.
        var now = DateTimeOffset.UtcNow;
        await environment.Ownership.UpdateAsync(state =>
        {
            var parallel = SonarrParallelSafety.SetMode(state, AnimeAcquisitionEnvironment.AnimeKey, AnimeManagementMode.ParallelAcquisition, now);
            parallel = SonarrParallelSafety.RegisterJob(parallel, new AcquisitionOwnership("sonarr-1", AnimeAcquisitionEnvironment.AnimeKey, AcquisitionOwner.Sonarr, AnimeReleaseParser.Parse(Best).ReleaseKey, AcquisitionOwnershipStatus.Pending, now));
            return SonarrParallelSafety.RegisterJob(parallel, new AcquisitionOwnership("sonarr-2", AnimeAcquisitionEnvironment.AnimeKey, AcquisitionOwner.Sonarr, AnimeReleaseParser.Parse(Lower).ReleaseKey, AcquisitionOwnershipStatus.Pending, now));
        });

        var parallelRun = await environment.Scheduler.RunNowAsync(null, AnimeSearchTrigger.PeriodicMissing, CancellationToken.None);

        Assert.AreEqual(1, parallelRun.Searches);
        Assert.AreEqual(0, parallelRun.Grabs);
        Assert.AreEqual(0, environment.Sabnzbd.Grabs.Count);
        var decisions = (await environment.LogsAsync(AnimeAcquisitionPipeline.LogModule)).Select(entry => entry.Message).ToArray();
        Assert.IsTrue(decisions.Any(message => message.StartsWith($"Rejected: {Best}", StringComparison.Ordinal) && message.Contains("Ownership:", StringComparison.Ordinal)), string.Join(Environment.NewLine, decisions));
        var attempt = (await environment.MonitoringStateAsync()).Attempts["frieren:S01E02"];
        Assert.AreEqual(AnimeAcquisitionAttemptStatus.Failed, attempt.Status, "The episode backs off instead of being searched every run.");
    }

    [TestMethod]
    public async Task RestartNeverGrabsTheSameEpisodeTwice()
    {
        await using var environment = await AnimeAcquisitionEnvironment.CreateAsync();
        await environment.SeedFrierenAsync();
        AddStandardReleases(environment);
        await environment.Scheduler.RunNowAsync(null, AnimeSearchTrigger.PeriodicMissing, CancellationToken.None);
        var acquisition = (await environment.Acquisitions.LoadAsync()).Acquisitions.Single();

        // The process stopped after SABnzbd accepted the release but before the grab was recorded.
        await environment.Monitoring.UpdateAsync(state => state with
        {
            Attempts = new Dictionary<string, AnimeAcquisitionAttempt>(StringComparer.OrdinalIgnoreCase)
            {
                ["frieren:S01E02"] = new(new AnimeEpisodeKey(AnimeAcquisitionEnvironment.AnimeKey, 1, 2, 2), AnimeAcquisitionAttemptStatus.Pending, null, 0, acquisition.CreatedAtUtc.AddSeconds(-1), null)
            }
        });
        await environment.RestartAsync();
        await environment.Scheduler.RecoverAsync(CancellationToken.None);

        Assert.AreEqual(AnimeAcquisitionAttemptStatus.Grabbed, (await environment.MonitoringStateAsync()).Attempts["frieren:S01E02"].Status);
        var afterRestart = await environment.Scheduler.RunNowAsync(null, AnimeSearchTrigger.PeriodicMissing, CancellationToken.None);
        Assert.AreEqual(0, afterRestart.Grabs);

        // Even a lost monitoring state cannot cause a second download: the relation store and the
        // running Operation are the source of truth.
        await environment.Monitoring.UpdateAsync(state => state with
        {
            Attempts = new Dictionary<string, AnimeAcquisitionAttempt>(StringComparer.OrdinalIgnoreCase)
        });
        await environment.RestartAsync();
        var lostState = await environment.Scheduler.RunNowAsync(null, AnimeSearchTrigger.Manual, CancellationToken.None);

        Assert.AreEqual(0, lostState.Grabs);
        Assert.AreEqual(1, environment.Sabnzbd.Grabs.Count);
        Assert.AreEqual(1, (await environment.Acquisitions.LoadAsync()).Acquisitions.Count);
        Assert.AreEqual(AnimeAcquisitionAttemptStatus.Grabbed, (await environment.MonitoringStateAsync()).Attempts["frieren:S01E02"].Status);
    }

    [TestMethod]
    public async Task CompletedDownloadIsImportedWithNamingAndReconciledIntoTheLibrary()
    {
        await using var environment = await AnimeAcquisitionEnvironment.CreateAsync();
        await environment.SeedFrierenAsync();
        AddStandardReleases(environment);
        await environment.Scheduler.RunNowAsync(null, AnimeSearchTrigger.PeriodicMissing, CancellationToken.None);

        var download = environment.AddCompletedDownload(Best, $"{Best}.mkv");
        var completed = await environment.CompleteLatestDownloadAsync(download);
        var record = await environment.ImportCompletedAsync(completed, download);

        Assert.AreEqual(AnimeImportStatus.Imported, record!.Status, record.Message);
        var expected = Path.Combine(environment.SeriesFolder, "Season 01", "Frieren - S01E02 - Episode 2.mkv");
        Assert.IsTrue(File.Exists(expected), "The file is named by the naming profile.");
        Assert.IsFalse(File.Exists(Path.Combine(download, $"{Best}.mkv")));
        var mediaFile = await environment.MediaFileAsync(1, 2);
        Assert.IsNotNull(mediaFile, "The folder reconciliation added the episode.");
        Assert.AreEqual(Path.GetFullPath(expected), mediaFile.Path);
        StringAssert.Contains(record.Message, "Library reconciled");

        var acquisition = (await environment.Acquisitions.LoadAsync()).Acquisitions.Single();
        Assert.AreEqual(AcquisitionOwnershipStatus.Completed, (await environment.Ownership.LoadAsync()).Jobs[acquisition.Id.ToString()].Status);
        var importLog = (await environment.LogsAsync(AnimeImportExecutor.LogModule)).Select(entry => entry.Message).ToArray();
        Assert.IsTrue(importLog.Any(message => message.StartsWith($"Imported {Best}.mkv as S01E02", StringComparison.Ordinal)), string.Join(Environment.NewLine, importLog));

        // Running the import again changes nothing.
        Assert.AreEqual(record.Id, (await environment.ImportCompletedAsync(completed, download))!.Id);

        var next = await environment.Scheduler.RunNowAsync(null, AnimeSearchTrigger.PeriodicMissing, CancellationToken.None);
        Assert.AreEqual(0, next.Searches);
        var monitoring = await environment.MonitoringStateAsync();
        Assert.AreEqual(0, monitoring.Wanted.Count, "The imported episode is no longer wanted.");
        Assert.IsFalse(monitoring.Attempts.ContainsKey("frieren:S01E02"));
    }

    [TestMethod]
    public async Task AbsoluteNumberedDownloadIsImportedAsTheMappedLocalEpisode()
    {
        await using var environment = await AnimeAcquisitionEnvironment.CreateAsync();
        await environment.SeedFrierenAsync(episodeCount: 1);
        const string title = "[Group] Frieren - 13 WEB-DL 1080p HEVC AAC[JA]";
        var started = await environment.StartAcquisitionAsync([new AnimeEpisodeKey(AnimeAcquisitionEnvironment.AnimeKey, 2, 1, 13)], title);
        Assert.IsTrue(started.Submitted, started.Message);

        var download = environment.AddCompletedDownload(title, $"{title}.mkv");
        var record = await environment.ImportCompletedAsync(await environment.CompleteLatestDownloadAsync(download), download);

        Assert.AreEqual(AnimeImportStatus.Imported, record!.Status, record.Message);
        var expected = Path.Combine(environment.SeriesFolder, "Season 02", "Frieren - S02E01 - Episode 1.mkv");
        Assert.IsTrue(File.Exists(expected));
        Assert.AreEqual(Path.GetFullPath(expected), (await environment.MediaFileAsync(2, 1))?.Path, "AniList episode 13 is local S02E01.");
        Assert.IsNull(await environment.MediaFileAsync(1, 13));
    }

    [TestMethod]
    public async Task UnwritableLibraryFolderBecomesManualImportThatCanBeCompleted()
    {
        await using var environment = await AnimeAcquisitionEnvironment.CreateAsync();
        await environment.SeedFrierenAsync(seasonFolders: false);
        AddStandardReleases(environment);
        await environment.Scheduler.RunNowAsync(null, AnimeSearchTrigger.PeriodicMissing, CancellationToken.None);

        // A file where the season folder must be created makes the library folder unwritable for
        // the import, like a read-only mount.
        var blocker = Path.Combine(environment.SeriesFolder, "Season 01");
        await File.WriteAllTextAsync(blocker, "not a folder");
        var download = environment.AddCompletedDownload(Best, $"{Best}.mkv");
        var record = await environment.ImportCompletedAsync(await environment.CompleteLatestDownloadAsync(download), download);

        Assert.AreEqual(AnimeImportStatus.ManualRequired, record!.Status);
        var file = record.Files.Single(item => item.SourcePath.EndsWith(".mkv", StringComparison.Ordinal));
        Assert.AreEqual(AnimeImportFileStatus.Failed, file.Status);
        StringAssert.Contains(file.Error, "not writable");
        Assert.IsTrue(File.Exists(Path.Combine(download, $"{Best}.mkv")), "The download is left untouched.");
        Assert.IsTrue(record.NeedsAttention);
        var operation = await environment.Operations.GetAsync(record.ImportOperationId!.Value);
        Assert.IsFalse(operation!.IsActive, "A manual-import state does not keep the operation running.");

        File.Delete(blocker);
        var manual = await environment.ImportManuallyAsync(record.Id, file.SourcePath, 1, 2);

        Assert.IsTrue(manual.Success, manual.Message);
        Assert.IsTrue(File.Exists(Path.Combine(environment.SeriesFolder, "Season 01", "Frieren - S01E02 - Episode 2.mkv")));
        Assert.IsNotNull(await environment.MediaFileAsync(1, 2));
        Assert.AreEqual(AnimeImportStatus.Imported, (await environment.Imports.GetAsync(record.Id))!.Status);
    }

    [TestMethod]
    public async Task ExistingDestinationAndSonarrOwnedPathsAreNeverOverwritten()
    {
        await using var environment = await AnimeAcquisitionEnvironment.CreateAsync();
        await environment.SeedFrierenAsync();
        AddStandardReleases(environment);
        await environment.Scheduler.RunNowAsync(null, AnimeSearchTrigger.PeriodicMissing, CancellationToken.None);

        var destination = Path.Combine(environment.SeriesFolder, "Season 01", "Frieren - S01E02 - Episode 2.mkv");
        await File.WriteAllTextAsync(destination, "someone else's file");
        var download = environment.AddCompletedDownload(Best, $"{Best}.mkv");
        var record = await environment.ImportCompletedAsync(await environment.CompleteLatestDownloadAsync(download), download);

        Assert.AreEqual(AnimeImportStatus.ManualRequired, record!.Status);
        StringAssert.Contains(record.Files.Single(item => item.SourcePath.EndsWith(".mkv", StringComparison.Ordinal)).Error, "already exists");
        Assert.AreEqual("someone else's file", await File.ReadAllTextAsync(destination));

        // Sonarr owns the destination: a manual import is refused as well.
        File.Delete(destination);
        await environment.Ownership.UpdateAsync(state => SonarrParallelSafety.RegisterPath(
            state,
            new ManagedMediaPath(destination, AnimeAcquisitionEnvironment.AnimeKey, AcquisitionOwner.Sonarr, null, DateTimeOffset.UtcNow)));
        var source = record.Files.Single(item => item.SourcePath.EndsWith(".mkv", StringComparison.Ordinal)).SourcePath;
        var manual = await environment.ImportManuallyAsync(record.Id, source, 1, 2);

        Assert.IsFalse(manual.Success);
        Assert.IsFalse(File.Exists(destination));
        Assert.IsTrue(File.Exists(source));
        var updated = await environment.Imports.GetAsync(record.Id);
        StringAssert.Contains(updated!.Files.Single(item => item.SourcePath == source).Error, "owned by Sonarr");
    }

    [TestMethod]
    public async Task DownloadCompletedWhileStoppedIsImportedByRestartRecovery()
    {
        await using var environment = await AnimeAcquisitionEnvironment.CreateAsync();
        await environment.SeedFrierenAsync();
        AddStandardReleases(environment);
        await environment.Scheduler.RunNowAsync(null, AnimeSearchTrigger.PeriodicMissing, CancellationToken.None);

        // SABnzbd finished and the monitor recorded it, but AniLingo stopped before importing.
        var download = environment.AddCompletedDownload(Best, $"{Best}.mkv");
        await environment.CompleteLatestDownloadAsync(download);
        await environment.RestartAsync();

        Assert.AreEqual(1, await environment.Scheduler.RecoverAsync(CancellationToken.None));

        Assert.IsNotNull(await environment.MediaFileAsync(1, 2));
        var record = (await environment.Imports.LoadAsync()).Imports.Single();
        Assert.AreEqual(AnimeImportStatus.Imported, record.Status, record.Message);
        Assert.IsFalse((await environment.MonitoringStateAsync()).Attempts.ContainsKey("frieren:S01E02"), "Recovery also clears the finished attempt.");
        Assert.AreEqual(0, await environment.Scheduler.RecoverAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task ImportWaitsForRunningLibraryScanAndResumesOnTheNextRun()
    {
        await using var environment = await AnimeAcquisitionEnvironment.CreateAsync();
        await environment.SeedFrierenAsync();
        AddStandardReleases(environment);
        await environment.Scheduler.RunNowAsync(null, AnimeSearchTrigger.PeriodicMissing, CancellationToken.None);
        var scan = await environment.Operations.CreateAsync(new OperationDescriptor("library-scan", "Library", "Library scan"));
        await environment.Operations.MarkRunningAsync(scan);

        var download = environment.AddCompletedDownload(Best, $"{Best}.mkv");
        var record = await environment.ImportCompletedAsync(await environment.CompleteLatestDownloadAsync(download), download);

        Assert.AreEqual(AnimeImportStatus.Importing, record!.Status);
        StringAssert.Contains(record.Message, "Waiting for 'Library scan'");
        Assert.IsTrue(File.Exists(Path.Combine(download, $"{Best}.mkv")));

        await environment.Operations.MarkSucceededAsync(scan);
        await environment.Scheduler.RunNowAsync(null, AnimeSearchTrigger.PeriodicMissing, CancellationToken.None);

        Assert.AreEqual(AnimeImportStatus.Imported, (await environment.Imports.GetAsync(record.Id))!.Status);
        Assert.IsNotNull(await environment.MediaFileAsync(1, 2));
    }

    private static void AddStandardReleases(AnimeAcquisitionEnvironment environment)
    {
        environment.Prowlarr.Releases.AddRange(
        [
            AnimeAcquisitionEnvironment.Release(Lower, "g720"),
            AnimeAcquisitionEnvironment.Release(Rejected, "g480"),
            AnimeAcquisitionEnvironment.Release(Best, "g1080"),
            AnimeAcquisitionEnvironment.Release(OtherSeries, "other")
        ]);
    }
}
