using AniLingo.Web.Features.Learning;

namespace AniLingo.Tests;

[TestClass]
public sealed class LearningSchedulerTests
{
    [TestMethod]
    public void AgainReturnsToShortRetry()
    {
        var now = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
        var schedule = new BasicReviewScheduler().Schedule(now, 10, ReviewRating.Again);

        Assert.AreEqual(0, schedule.IntervalDays);
        Assert.AreEqual(now.AddMinutes(10), schedule.NextReviewAt);
    }

    [TestMethod]
    public void GoodExpandsExistingInterval()
    {
        var now = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
        var schedule = new BasicReviewScheduler().Schedule(now, 10, ReviewRating.Good);

        Assert.IsTrue(schedule.IntervalDays > 10);
        Assert.AreEqual(now.AddDays(schedule.IntervalDays), schedule.NextReviewAt);
    }
}
