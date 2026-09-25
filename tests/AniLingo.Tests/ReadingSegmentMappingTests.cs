using AniLingo.Web.Features.MediaMapping;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniLingo.Tests;

[TestClass]
public sealed class ReadingSegmentMappingTests
{
    [TestMethod]
    public async Task SegmentStorePersistsAndRejectsOverlappingChapterRanges()
    {
        var directory = TempDirectory();

        try
        {
            var store = CreateStore(directory);
            var localId = Guid.NewGuid().ToString();

            var first = await store.AddAsync(
                Mapping(
                    localId,
                    localStart: 1,
                    localEnd: 12,
                    externalId: "100",
                    remoteStart: 1));

            var reloaded = CreateStore(directory);
            var stored = await reloaded.ListAsync(
                "manga",
                localId);

            Assert.AreEqual(1, stored.Count);
            Assert.AreEqual(first.Id, stored[0].Id);

            var overlapRejected = false;
            try
            {
                await reloaded.AddAsync(
                    Mapping(
                        localId,
                        localStart: 12,
                        localEnd: 24,
                        externalId: "101",
                        remoteStart: 1));
            }
            catch (InvalidOperationException)
            {
                overlapRejected = true;
            }

            Assert.IsTrue(overlapRejected);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [TestMethod]
    public async Task SecondAniListEntryResolvesLocalChapterOffset()
    {
        var directory = TempDirectory();

        try
        {
            var store = CreateStore(directory);
            var localId = Guid.NewGuid().ToString();

            await store.AddAsync(
                Mapping(
                    localId,
                    localStart: 1,
                    localEnd: 12,
                    externalId: "200",
                    remoteStart: 1));
            await store.AddAsync(
                Mapping(
                    localId,
                    localStart: 13,
                    localEnd: 24,
                    externalId: "201",
                    remoteStart: 1,
                    remoteChapterCount: 12));

            var resolved = await store.ResolveAsync(
                "manga",
                localId,
                localChapterNumber: 18,
                localVolumeNumber: null,
                position: 99,
                completedThreshold: 99);

            Assert.IsNotNull(resolved);
            Assert.IsTrue(resolved.CanSync);
            Assert.AreEqual("201", resolved.ExternalId);
            Assert.AreEqual(6, resolved.Progress);
            Assert.AreEqual(12, resolved.RemoteChapterCount);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [TestMethod]
    public async Task UnfinishedChapterUsesPreviouslyCompletedMappedChapter()
    {
        var directory = TempDirectory();

        try
        {
            var store = CreateStore(directory);
            var localId = Guid.NewGuid().ToString();

            await store.AddAsync(
                Mapping(
                    localId,
                    localStart: 13,
                    localEnd: 24,
                    externalId: "300",
                    remoteStart: 1,
                    mediaType: "novel"));

            var resolved = await store.ResolveAsync(
                "novel",
                localId,
                localChapterNumber: 15,
                localVolumeNumber: null,
                position: 400,
                completedThreshold: 950);

            Assert.IsNotNull(resolved);
            Assert.IsTrue(resolved.CanSync);
            Assert.AreEqual(2, resolved.Progress);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [TestMethod]
    public async Task FirstUnfinishedChapterInNewSegmentDoesNotWriteProgress()
    {
        var directory = TempDirectory();

        try
        {
            var store = CreateStore(directory);
            var localId = Guid.NewGuid().ToString();

            await store.AddAsync(
                Mapping(
                    localId,
                    localStart: 13,
                    localEnd: 24,
                    externalId: "400",
                    remoteStart: 1,
                    mediaType: "novel"));

            var resolved = await store.ResolveAsync(
                "novel",
                localId,
                localChapterNumber: 13,
                localVolumeNumber: null,
                position: 200,
                completedThreshold: 950);

            Assert.IsNotNull(resolved);
            Assert.IsFalse(resolved.CanSync);
            StringAssert.Contains(resolved.Reason, "No mapped chapter");
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [TestMethod]
    public async Task ConfiguredMappingSetBlocksUncoveredChapters()
    {
        var directory = TempDirectory();

        try
        {
            var store = CreateStore(directory);
            var localId = Guid.NewGuid().ToString();

            await store.AddAsync(
                Mapping(
                    localId,
                    localStart: 1,
                    localEnd: 10,
                    externalId: "500",
                    remoteStart: 1));

            var resolved = await store.ResolveAsync(
                "manga",
                localId,
                localChapterNumber: 11,
                localVolumeNumber: null,
                position: 99,
                completedThreshold: 99);

            Assert.IsNotNull(resolved);
            Assert.IsFalse(resolved.CanSync);
            StringAssert.Contains(resolved.Reason, "outside all configured");
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [TestMethod]
    public async Task MangaVolumeOffsetsResolveAlongsideChapterOffsets()
    {
        var directory = TempDirectory();

        try
        {
            var store = CreateStore(directory);
            var localId = Guid.NewGuid().ToString();

            await store.AddAsync(
                Mapping(
                    localId,
                    localStart: 25,
                    localEnd: 48,
                    externalId: "600",
                    remoteStart: 1,
                    localVolumeStart: 3,
                    localVolumeEnd: 4,
                    remoteVolumeStart: 1));

            var resolved = await store.ResolveAsync(
                "manga",
                localId,
                localChapterNumber: 37,
                localVolumeNumber: 4,
                position: 99,
                completedThreshold: 99);

            Assert.IsNotNull(resolved);
            Assert.IsTrue(resolved.CanSync);
            Assert.AreEqual(13, resolved.Progress);
            Assert.AreEqual(2, resolved.VolumeProgress);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [TestMethod]
    public async Task FractionalLocalChapterCanMapWhenRangeUsesSameFractionalBase()
    {
        var directory = TempDirectory();

        try
        {
            var store = CreateStore(directory);
            var localId = Guid.NewGuid().ToString();

            await store.AddAsync(
                Mapping(
                    localId,
                    localStart: 12.5,
                    localEnd: 14.5,
                    externalId: "700",
                    remoteStart: 1));

            var resolved = await store.ResolveAsync(
                "manga",
                localId,
                localChapterNumber: 13.5,
                localVolumeNumber: null,
                position: 99,
                completedThreshold: 99);

            Assert.IsNotNull(resolved);
            Assert.IsTrue(resolved.CanSync);
            Assert.AreEqual(2, resolved.Progress);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    private static ReadingMediaSegmentMapping Mapping(
        string localId,
        double localStart,
        double localEnd,
        string externalId,
        int remoteStart,
        string mediaType = "manga",
        int? remoteChapterCount = null,
        int? localVolumeStart = null,
        int? localVolumeEnd = null,
        int? remoteVolumeStart = null) =>
        new(
            Guid.NewGuid(),
            mediaType,
            localId,
            localStart,
            localEnd,
            remoteStart,
            "anilist",
            externalId,
            $"AniList {externalId}",
            remoteChapterCount,
            localVolumeStart,
            localVolumeEnd,
            remoteVolumeStart,
            DateTimeOffset.UtcNow);

    private static ReadingSegmentMappingStore CreateStore(string directory) =>
        new(
            NullLogger<ReadingSegmentMappingStore>.Instance,
            new DirectoryInfo(directory));

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-reading-segments-{Guid.NewGuid():N}");
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
