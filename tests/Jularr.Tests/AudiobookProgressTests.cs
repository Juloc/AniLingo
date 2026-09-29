using Jularr.Web.Data;
using Jularr.Web.Features.Audiobooks;

namespace Jularr.Tests;

/// <summary>
/// Per-profile audiobook listening progress (#440): a checkpoint stores position and chapter, completion
/// is sticky, and it is scoped per profile.
/// </summary>
[TestClass]
public sealed class AudiobookProgressTests
{
    private const string Reader = "reader-1";

    [TestMethod]
    public async Task SaveThenGetReturnsPositionAndChapter()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        var audiobookId = await SeedAudiobookAsync(db);
        var service = new AudiobookProgressService(db);

        await service.SaveAsync(Reader, audiobookId, positionMs: 120_000, durationMs: 3_600_000, chapterNumber: 3, completed: false, CancellationToken.None);

        var snapshot = await service.GetAsync(Reader, audiobookId, CancellationToken.None);
        Assert.IsNotNull(snapshot);
        Assert.AreEqual(120_000, snapshot!.PositionMs);
        Assert.AreEqual(3, snapshot.ChapterNumber);
        Assert.IsFalse(snapshot.IsCompleted);
        Assert.AreEqual(3, snapshot.Percent);
    }

    [TestMethod]
    public async Task ReachingTheEndMarksCompletedAndClearsPosition()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        var audiobookId = await SeedAudiobookAsync(db);
        var service = new AudiobookProgressService(db);

        var snapshot = await service.SaveAsync(
            Reader, audiobookId, positionMs: 3_500_000, durationMs: 3_600_000, chapterNumber: 40, completed: false, CancellationToken.None);

        Assert.IsTrue(snapshot.IsCompleted);
        Assert.AreEqual(0, snapshot.PositionMs);
        Assert.AreEqual(100, snapshot.Percent);
    }

    [TestMethod]
    public async Task CompletionIsStickyAcrossLaterPartialCheckpoint()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        var audiobookId = await SeedAudiobookAsync(db);
        var service = new AudiobookProgressService(db);

        await service.SetCompletedAsync(Reader, audiobookId, completed: true, CancellationToken.None);
        var snapshot = await service.SaveAsync(
            Reader, audiobookId, positionMs: 5_000, durationMs: 3_600_000, chapterNumber: 1, completed: false, CancellationToken.None);

        Assert.IsTrue(snapshot.IsCompleted, "A partial checkpoint must not un-complete a finished audiobook.");
    }

    [TestMethod]
    public async Task ProgressIsScopedPerProfile()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        var audiobookId = await SeedAudiobookAsync(db);
        var service = new AudiobookProgressService(db);

        await service.SaveAsync(Reader, audiobookId, positionMs: 60_000, durationMs: 3_600_000, chapterNumber: 2, completed: false, CancellationToken.None);

        Assert.IsNull(await service.GetAsync("reader-2", audiobookId, CancellationToken.None));
    }

    private static async Task<Guid> SeedAudiobookAsync(AppDbContext db)
    {
        var audiobook = new Audiobook { Key = "seed", Title = "Seed Audiobook" };
        db.Add(audiobook);
        await db.SaveChangesAsync();
        return audiobook.Id;
    }
}
