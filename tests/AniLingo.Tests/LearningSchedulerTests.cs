using AniLingo.Web.Features.Learning;

namespace AniLingo.Tests;

[TestClass]
public sealed class LearningSchedulerTests
{
    private static readonly Guid CardId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [TestMethod]
    public void NewCardAgainUsesFirstLearningStep()
    {
        var now = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
        var schedule = new FsrsReviewScheduler().Schedule(CardId, now, [], ReviewRating.Again);

        Assert.AreEqual(now.AddMinutes(1), schedule.NextReviewAt);
        Assert.AreEqual(0, schedule.IntervalDays);
    }

    [TestMethod]
    public void NewCardGoodUsesSecondLearningStep()
    {
        var now = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
        var schedule = new FsrsReviewScheduler().Schedule(CardId, now, [], ReviewRating.Good);

        Assert.AreEqual(now.AddMinutes(10), schedule.NextReviewAt);
        Assert.AreEqual(0, schedule.IntervalDays);
    }

    [TestMethod]
    public void MatureCardAgainUsesRelearningStep()
    {
        var first = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
        ReviewHistoryItem[] history =
        [
            new(ReviewRating.Easy, first),
            new(ReviewRating.Good, first.AddDays(7))
        ];

        var schedule = new FsrsReviewScheduler().Schedule(
            CardId,
            now,
            history,
            ReviewRating.Again);

        Assert.AreEqual(now.AddMinutes(10), schedule.NextReviewAt);
        Assert.AreEqual(0, schedule.IntervalDays);
    }

    [TestMethod]
    public void PreviewMatchesSubmittedSchedule()
    {
        var now = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
        ReviewHistoryItem[] history =
        [
            new(ReviewRating.Easy, now.AddDays(-20)),
            new(ReviewRating.Good, now.AddDays(-10))
        ];
        var scheduler = new FsrsReviewScheduler();

        var preview = scheduler.Preview(CardId, now, history);
        var actual = scheduler.Schedule(CardId, now, history, ReviewRating.Good);

        Assert.AreEqual(preview[ReviewRating.Good], actual);
        Assert.IsTrue(actual.NextReviewAt > now);
    }
}
