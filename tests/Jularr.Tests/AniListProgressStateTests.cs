using Jularr.Web.Features.MediaMapping;
using Jularr.Web.Features.Tracking;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jularr.Tests;

[TestClass]
public sealed class AniListProgressStateTests
{
    [TestMethod]
    public void EqualChapterProgressIsSynced()
    {
        var state = AniListAccountService.ClassifyProgressState(
            "Example",
            localProgress: 7,
            remoteProgress: 7,
            message: "AniList already has progress 7.",
            canSync: false);

        Assert.AreEqual(AniListExternalProgressStateKind.Synced, state.Kind);
        Assert.IsTrue(state.IsSynced);
        Assert.AreEqual(
            "Local and AniList progress are synchronized.",
            state.Message);
    }

    [TestMethod]
    public void EqualProgressWithPendingWriteIsNotSynced()
    {
        // A PLANNING entry at the same episode still offers "start tracking".
        var state = AniListAccountService.ClassifyProgressState(
            "Example",
            localProgress: 3,
            remoteProgress: 3,
            message: "Ready to start AniList tracking and set progress to 3.",
            canSync: true,
            remoteStatus: "PLANNING");

        Assert.AreNotEqual(AniListExternalProgressStateKind.Synced, state.Kind);
        Assert.IsFalse(state.IsSynced);
        Assert.IsTrue(state.CanSync);
        Assert.AreEqual(
            "Ready to start AniList tracking and set progress to 3.",
            state.Message);
    }

    [TestMethod]
    public void TrackingStartDateUsesTheViewersCalendarDay()
    {
        var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var justAfterMidnightInBerlin = new DateTimeOffset(2026, 9, 27, 22, 30, 0, TimeSpan.Zero);

        Assert.AreEqual(
            new AniListFuzzyDate(2026, 9, 28),
            AniListAccountService.TrackingStartDate(justAfterMidnightInBerlin, berlin));
        Assert.AreEqual(
            new AniListFuzzyDate(2026, 9, 27),
            AniListAccountService.TrackingStartDate(justAfterMidnightInBerlin, TimeZoneInfo.Utc));
    }

    [TestMethod]
    public void HigherLocalChapterProgressIsLocalAhead()
    {
        var state = AniListAccountService.ClassifyProgressState(
            "Example",
            localProgress: 8,
            remoteProgress: 6,
            message: "Ready to increase AniList progress.",
            canSync: true);

        Assert.AreEqual(AniListExternalProgressStateKind.LocalAhead, state.Kind);
        Assert.IsTrue(state.CanSync);
    }

    [TestMethod]
    public void HigherAniListChapterProgressIsAniListAhead()
    {
        var state = AniListAccountService.ClassifyProgressState(
            "Example",
            localProgress: 5,
            remoteProgress: 9,
            message: "AniList already has higher progress.",
            canSync: false);

        Assert.AreEqual(AniListExternalProgressStateKind.AniListAhead, state.Kind);
        Assert.IsFalse(state.CanSync);
    }

    [TestMethod]
    public void VolumeProgressBreaksTieWhenChaptersAreEqual()
    {
        var localAhead = AniListAccountService.ClassifyProgressState(
            "Example Manga",
            localProgress: 20,
            remoteProgress: 20,
            message: "Volume progress can advance.",
            canSync: true,
            localVolumeProgress: 4,
            remoteVolumeProgress: 3);

        var remoteAhead = AniListAccountService.ClassifyProgressState(
            "Example Manga",
            localProgress: 20,
            remoteProgress: 20,
            message: "AniList volume progress is ahead.",
            canSync: false,
            localVolumeProgress: 3,
            remoteVolumeProgress: 5);

        Assert.AreEqual(
            AniListExternalProgressStateKind.LocalAhead,
            localAhead.Kind);
        Assert.AreEqual(
            AniListExternalProgressStateKind.AniListAhead,
            remoteAhead.Kind);
    }

    [TestMethod]
    public void ForcedNotOnListStateDoesNotDependOnRemoteProgress()
    {
        var state = AniListAccountService.ClassifyProgressState(
            "Example Novel",
            localProgress: 12,
            remoteProgress: null,
            message: "Add this entry to AniList first.",
            canSync: false,
            forcedKind: AniListExternalProgressStateKind.NotOnList);

        Assert.AreEqual(AniListExternalProgressStateKind.NotOnList, state.Kind);
        Assert.IsTrue(state.NeedsAttention);
        Assert.AreEqual(12, state.LocalProgress);
    }

    [TestMethod]
    public void MappingReviewTakesPriorityOverComparableProgress()
    {
        var state = AniListAccountService.ClassifyProgressState(
            "Example",
            localProgress: 8,
            remoteProgress: 8,
            message: "The relation chain is ambiguous.",
            canSync: false,
            forcedKind: AniListExternalProgressStateKind.MappingNeedsReview);

        Assert.AreEqual(
            AniListExternalProgressStateKind.MappingNeedsReview,
            state.Kind);
        Assert.IsFalse(state.IsSynced);
    }

    [TestMethod]
    public async Task PendingReviewLookupFiltersByMediaAndPurpose()
    {
        var directory = TempDirectory();

        try
        {
            var store = new MediaMappingReviewStore(
                NullLogger<MediaMappingReviewStore>.Instance,
                new DirectoryInfo(directory));

            await store.UpsertAsync(
                "manga",
                "series-1",
                "Example Manga",
                "reading-segments",
                "Ambiguous relation chain.",
                []);

            await store.UpsertAsync(
                "manga",
                "series-1",
                "Example Manga",
                "identity",
                "Identity candidate needs review.",
                []);

            var segmentReview = await store.FindPendingAsync(
                "manga",
                "series-1",
                ["reading-segments"]);

            var missing = await store.FindPendingAsync(
                "novel",
                "series-1",
                ["reading-segments"]);

            Assert.IsNotNull(segmentReview);
            Assert.AreEqual("reading-segments", segmentReview.Purpose);
            Assert.IsNull(missing);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"jularr-progress-state-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
