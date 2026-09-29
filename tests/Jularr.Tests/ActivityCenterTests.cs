using Jularr.Web.Features.Operations;

namespace Jularr.Tests;

/// <summary>
/// The Activity center (#413) is a view over existing operation snapshots: these tests pin the
/// lane grouping, the aggregation per lane and the cancel/retry gating.
/// </summary>
[TestClass]
public sealed class ActivityCenterTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    [DataRow("library-scan", "Library", false, OperationLane.Maintenance, ActivityLane.Scans)]
    [DataRow("anime-grab", "Acquisition", false, OperationLane.Normal, ActivityLane.Downloads)]
    [DataRow("anime-search-request", "Acquisition", false, OperationLane.Normal, ActivityLane.Downloads)]
    [DataRow("anime-sabnzbd-download", "External downloads", true, OperationLane.Normal, ActivityLane.Downloads)]
    [DataRow("book-usenet-download", "Books", false, OperationLane.Normal, ActivityLane.Downloads)]
    [DataRow("anime-import", "Library", false, OperationLane.Normal, ActivityLane.Imports)]
    [DataRow("media-inbox-import", "Library", false, OperationLane.Normal, ActivityLane.Imports)]
    [DataRow("sonarr-artwork-import", "Artwork", false, OperationLane.Normal, ActivityLane.Imports)]
    [DataRow("playback-preparation", "Playback", false, OperationLane.Interactive, ActivityLane.Preparation)]
    [DataRow("trickplay-generation", "Library", false, OperationLane.Maintenance, ActivityLane.Preparation)]
    [DataRow("segment-detection", "Library", false, OperationLane.Maintenance, ActivityLane.Preparation)]
    [DataRow("novel-chapter-download", "Novels", false, OperationLane.Normal, ActivityLane.Preparation)]
    [DataRow("novel-chapter-translation", "Novels", false, OperationLane.Normal, ActivityLane.Preparation)]
    [DataRow("anilist-anime-progress-sync", "Tracking", false, OperationLane.Normal, ActivityLane.Metadata)]
    [DataRow("anime-metadata-refresh", "Anime", false, OperationLane.Normal, ActivityLane.Metadata)]
    [DataRow("novel-episode-mapping", "Novels", false, OperationLane.Normal, ActivityLane.Metadata)]
    [DataRow("media-optimization", "Library", false, OperationLane.Maintenance, ActivityLane.Maintenance)]
    [DataRow("something-new", "Misc", false, OperationLane.Normal, ActivityLane.Other)]
    public void OperationsLandInTheirLane(
        string kind,
        string category,
        bool isDownload,
        OperationLane lane,
        ActivityLane expected)
    {
        var operation = Op(kind: kind, category: category, isDownload: isDownload, lane: lane);

        Assert.AreEqual(expected, ActivityLaneClassifier.Classify(operation));
    }

    [TestMethod]
    public void BoardShowsActiveWorkAndRecentProblemsButNotHistory()
    {
        var snapshot = ActivityCenterBuilder.Build(
            [
                Op(status: OperationStatus.Running),
                Op(status: OperationStatus.Queued),
                Op(status: OperationStatus.Failed, updated: Now.AddHours(-1)),
                Op(status: OperationStatus.Interrupted, updated: Now.AddHours(-2)),
                Op(status: OperationStatus.Failed, updated: Now.AddDays(-3)),
                Op(status: OperationStatus.Succeeded),
                Op(status: OperationStatus.Cancelled)
            ],
            completedToday: 7,
            Now);

        Assert.AreEqual(1, snapshot.Groups.Count);
        Assert.AreEqual(4, snapshot.Groups[0].Items.Count);
        Assert.AreEqual(1, snapshot.Running);
        Assert.AreEqual(1, snapshot.Queued);
        Assert.AreEqual(2, snapshot.NeedsAttention);
        Assert.AreEqual(7, snapshot.CompletedToday);
    }

    [TestMethod]
    public void EmptyBoardHasNoGroups()
    {
        var snapshot = ActivityCenterBuilder.Build(
            [Op(status: OperationStatus.Succeeded)],
            completedToday: 1,
            Now);

        Assert.IsTrue(snapshot.IsEmpty);
        Assert.AreEqual(0, snapshot.Running);
        Assert.AreEqual(1, snapshot.CompletedToday);
    }

    [TestMethod]
    public void GroupsFollowLaneOrderAndSkipEmptyLanes()
    {
        var snapshot = ActivityCenterBuilder.Build(
            [
                Op(kind: "media-optimization", lane: OperationLane.Maintenance, status: OperationStatus.Queued),
                Op(kind: "library-scan", status: OperationStatus.Running),
                Op(kind: "anime-grab", category: "Acquisition", status: OperationStatus.Running)
            ],
            completedToday: 0,
            Now);

        CollectionAssert.AreEqual(
            new[] { ActivityLane.Downloads, ActivityLane.Scans, ActivityLane.Maintenance },
            snapshot.Groups.Select(group => group.Lane).ToArray());
    }

    [TestMethod]
    public void ItemsAreRunningThenQueueOrderThenProblems()
    {
        var running = Op(title: "running", status: OperationStatus.Running, created: Now.AddMinutes(-9));
        var queuedFirst = Op(title: "queued-first", status: OperationStatus.Queued, created: Now.AddMinutes(-8));
        var queuedSecond = Op(title: "queued-second", status: OperationStatus.Queued, created: Now.AddMinutes(-2));
        var failedOld = Op(title: "failed-old", status: OperationStatus.Failed, updated: Now.AddHours(-5));
        var failedNew = Op(title: "failed-new", status: OperationStatus.Interrupted, updated: Now.AddMinutes(-5));

        var snapshot = ActivityCenterBuilder.Build(
            [failedOld, queuedSecond, failedNew, running, queuedFirst],
            completedToday: 0,
            Now);

        CollectionAssert.AreEqual(
            new[] { "running", "queued-first", "queued-second", "failed-new", "failed-old" },
            snapshot.Groups[0].Items.Select(item => item.Title).ToArray());
    }

    [TestMethod]
    public void LaneAggregatesRunningProgressSpeedAndLatestEta()
    {
        var soon = Now.AddMinutes(2);
        var later = Now.AddMinutes(9);

        var snapshot = ActivityCenterBuilder.Build(
            [
                Op(kind: "anime-sabnzbd-download", isDownload: true, status: OperationStatus.Running, progress: 20, bytesPerSecond: 1000, eta: soon),
                Op(kind: "anime-sabnzbd-download", isDownload: true, status: OperationStatus.Running, progress: 60, bytesPerSecond: 500, eta: later),
                Op(kind: "anime-sabnzbd-download", isDownload: true, status: OperationStatus.Running),
                Op(kind: "anime-sabnzbd-download", isDownload: true, status: OperationStatus.Queued, progress: 99, bytesPerSecond: 9999)
            ],
            completedToday: 0,
            Now);

        var group = snapshot.Groups.Single();
        Assert.AreEqual(3, group.Running);
        Assert.AreEqual(1, group.Queued);
        Assert.AreEqual(40, group.ProgressPercent, "Only running operations with known progress are averaged.");
        Assert.AreEqual(1500d, group.BytesPerSecond);
        Assert.AreEqual(later, group.EtaUtc);
    }

    [TestMethod]
    public void LaneWithoutMeasuredProgressReportsNone()
    {
        var snapshot = ActivityCenterBuilder.Build(
            [Op(kind: "library-scan", status: OperationStatus.Running), Op(kind: "library-scan", status: OperationStatus.Queued)],
            completedToday: 0,
            Now);

        var group = snapshot.Groups.Single();
        Assert.IsNull(group.ProgressPercent);
        Assert.IsNull(group.BytesPerSecond);
        Assert.IsNull(group.EtaUtc);
    }

    [TestMethod]
    public void ActiveInternalWorkCanBeCancelledButExternalWorkCannot()
    {
        Assert.IsTrue(OperationActionPolicy.CanCancel(Op(status: OperationStatus.Running)));
        Assert.IsTrue(OperationActionPolicy.CanCancel(Op(status: OperationStatus.Queued)));
        Assert.IsFalse(OperationActionPolicy.CanCancel(Op(status: OperationStatus.Failed)));
        Assert.IsFalse(OperationActionPolicy.CanCancel(Op(status: OperationStatus.Succeeded)));
        Assert.IsFalse(
            OperationActionPolicy.CanCancel(Op(status: OperationStatus.Running, externalProvider: "other-client", externalId: "1")));
    }

    [TestMethod]
    public void SabnzbdJobsAreCancellableThroughTheirProvider()
    {
        var job = Op(
            status: OperationStatus.Running,
            externalProvider: Jularr.Web.Features.Acquisition.Sabnzbd.SabnzbdClient.ProviderId,
            externalId: "SABnzbd_nzo_1");

        Assert.IsFalse(job.CanCancel, "The generic snapshot rule excludes external providers.");
        Assert.IsTrue(OperationActionPolicy.CanCancel(job));
        Assert.IsTrue(OperationActionPolicy.Evaluate(job, runtimeAvailable: true).CanCancel);
    }

    [TestMethod]
    public void RetryIsOfferedOnlyForRetryableFailedOrInterruptedWork()
    {
        var failed = OperationActionPolicy.Evaluate(Op(status: OperationStatus.Failed), runtimeAvailable: true);
        var interrupted = OperationActionPolicy.Evaluate(Op(status: OperationStatus.Interrupted), runtimeAvailable: true);
        var running = OperationActionPolicy.Evaluate(Op(status: OperationStatus.Running), runtimeAvailable: true);
        var cancelled = OperationActionPolicy.Evaluate(Op(status: OperationStatus.Cancelled), runtimeAvailable: true);
        var notRetryable = OperationActionPolicy.Evaluate(
            Op(status: OperationStatus.Failed, retryable: false),
            runtimeAvailable: true);

        Assert.IsTrue(failed.CanRetry);
        Assert.IsTrue(interrupted.CanRetry);
        Assert.IsFalse(running.RetryOffered);
        Assert.IsFalse(cancelled.RetryOffered);
        Assert.IsFalse(notRetryable.RetryOffered);
    }

    [TestMethod]
    public void RetryIsDisabledWhenTheWorkDelegateIsGone()
    {
        var state = OperationActionPolicy.Evaluate(Op(status: OperationStatus.Failed), runtimeAvailable: false);

        Assert.IsFalse(state.CanRetry);
        Assert.IsTrue(state.RetryPayloadMissing);
        Assert.IsTrue(state.RetryOffered, "The button stays visible but disabled so the reason can be shown.");
    }

    private static OperationSnapshot Op(
        string kind = "test-kind",
        string category = "Test",
        OperationLane lane = OperationLane.Normal,
        OperationStatus status = OperationStatus.Running,
        string title = "Operation",
        bool isDownload = false,
        int? progress = null,
        double? bytesPerSecond = null,
        DateTime? eta = null,
        bool retryable = true,
        string? externalProvider = null,
        string? externalId = null,
        DateTime? created = null,
        DateTime? updated = null) =>
        new(
            Guid.NewGuid(),
            kind,
            category,
            lane,
            status,
            null,
            title,
            null,
            progress,
            null,
            null,
            isDownload,
            null,
            null,
            bytesPerSecond,
            eta,
            1,
            retryable,
            externalProvider,
            externalId,
            created ?? Now.AddMinutes(-30),
            null,
            null,
            updated ?? Now.AddMinutes(-1));
}
