using AniLingo.Web.Features.MediaMapping;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniLingo.Tests;

[TestClass]
public sealed class MediaMappingReviewStoreTests
{
    [TestMethod]
    public async Task ReviewTasksPersistAndUpsertByMediaPurpose()
    {
        var directory = TempDirectory();

        try
        {
            var store = CreateStore(directory);
            var first = await store.UpsertAsync(
                "anime",
                "local-1",
                "Example",
                "identity",
                "Ambiguous title match.",
                [
                    new MediaMappingReviewCandidate(
                        "anilist",
                        "100",
                        "Example",
                        80,
                        ["exact title"])
                ]);

            var second = await store.UpsertAsync(
                "anime",
                "local-1",
                "Example Updated",
                "identity",
                "Still ambiguous.",
                [
                    new MediaMappingReviewCandidate(
                        "anilist",
                        "101",
                        "Example Part 2",
                        78,
                        ["exact alias"])
                ]);

            Assert.AreEqual(first.Id, second.Id);
            Assert.AreEqual(first.CreatedAt, second.CreatedAt);

            var reloaded = CreateStore(directory);
            var tasks = await reloaded.ListAsync();

            Assert.AreEqual(1, tasks.Count);
            Assert.AreEqual("Example Updated", tasks[0].LocalTitle);
            Assert.AreEqual("Still ambiguous.", tasks[0].Reason);
            Assert.AreEqual("101", tasks[0].Candidates.Single().ExternalId);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [TestMethod]
    public async Task ResolvingOnePurposeKeepsOtherReviewTasks()
    {
        var directory = TempDirectory();

        try
        {
            var store = CreateStore(directory);
            await store.UpsertAsync(
                "anime",
                "local-1",
                "Example",
                "identity",
                "Identity review.",
                []);
            var episodeTask = await store.UpsertAsync(
                "anime",
                "local-1",
                "Example",
                "episode-ranges",
                "Episode review.",
                []);

            Assert.IsTrue(await store.ResolveAsync(
                "anime",
                "local-1",
                "identity"));

            var tasks = await store.ListAsync();
            Assert.AreEqual(1, tasks.Count);
            Assert.AreEqual(episodeTask.Id, tasks[0].Id);

            Assert.IsTrue(await store.DismissAsync(episodeTask.Id));
            Assert.AreEqual(0, (await store.ListAsync()).Count);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    private static MediaMappingReviewStore CreateStore(string directory) =>
        new(
            NullLogger<MediaMappingReviewStore>.Instance,
            new DirectoryInfo(directory));

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-mapping-review-{Guid.NewGuid():N}");
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
