using AniLingo.Web.Features.MediaMapping;
using AniLingo.Web.Features.Tracking;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniLingo.Tests;

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
            "AniList already has progress 7.",
            canSync: false);

        Assert.AreEqual(AniListExternalProgressStateKind.Synced, state.Kind);
        Assert.IsTrue(state.IsSynced);
        Assert.AreEqual(
            "Local and AniList progress are synchronized.",
            state.Message);
    }

    [TestMethod]
    public void HigherLocalChapterProgressIsLocalAhead()
    {
        var state = AniListAccountService.ClassifyProgressState(
            "Example",
            localProgress: 8,
            remoteProgress: 6,
            "Ready to increase AniList progress.",
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
            "AniList already has higher progress.",
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
            "Volume progress can advance.",
            canSync: true,
            localVolumeProgress: 4,
            remoteVolumeProgress: 3);

        var remoteAhead = AniListAccountService.ClassifyProgressState(
            "Example Manga",
            localProgress: 20,
            remoteProgress: 20,
            "AniList volume progress is ahead.",
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
            "Add this entry to AniList first.",
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
            "The relation chain is ambiguous.",
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
            $"anilingo-progress-state-{Guid.NewGuid():N}");
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
