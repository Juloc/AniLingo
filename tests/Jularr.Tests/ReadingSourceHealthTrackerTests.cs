using Jularr.Web.Features.ReadingSources;

namespace Jularr.Tests;

[TestClass]
public sealed class ReadingSourceHealthTrackerTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ASourceNobodyAskedYetIsUnknownAndAllowed()
    {
        var tracker = new ReadingSourceHealthTracker(new ManualTime(Start));

        Assert.AreEqual(ReadingSourceHealthStatus.Unknown, tracker.Get("bookwalker").Status);
        Assert.IsTrue(tracker.CanAttempt("bookwalker"));
    }

    [TestMethod]
    public void ASuccessfulSearchMarksTheSourceHealthyAndClearsAnyCooldown()
    {
        var clock = new ManualTime(Start);
        var tracker = new ReadingSourceHealthTracker(clock);

        tracker.RecordFailure("anilist");
        Assert.IsFalse(tracker.CanAttempt("anilist"));

        clock.Advance(TimeSpan.FromMinutes(1));
        tracker.RecordSuccess("anilist");

        var health = tracker.Get("anilist");
        Assert.AreEqual(ReadingSourceHealthStatus.Healthy, health.Status);
        Assert.IsNull(health.RetryAt);
        Assert.IsTrue(tracker.CanAttempt("anilist"));
    }

    [TestMethod]
    public void RepeatedFailuresBackOffExponentiallyUpToTenMinutes()
    {
        var clock = new ManualTime(Start);
        var tracker = new ReadingSourceHealthTracker(clock);
        var expected = new[] { 30, 60, 120, 240, 480, 600, 600 };

        foreach (var seconds in expected)
        {
            tracker.RecordFailure("webnovel");
            Assert.AreEqual(
                Start + clock.Elapsed + TimeSpan.FromSeconds(seconds),
                tracker.Get("webnovel").RetryAt);

            // Wait out the cooldown, then fail again.
            clock.Advance(TimeSpan.FromSeconds(seconds));
        }
    }

    [TestMethod]
    public void ARateLimitedSourceWaitsAsLongAsItAskedWithinBounds()
    {
        var clock = new ManualTime(Start);
        var tracker = new ReadingSourceHealthTracker(clock);

        tracker.RecordFailure(
            "internetarchive",
            ReadingSourceFailureKind.RateLimited,
            TimeSpan.FromMinutes(20));
        Assert.AreEqual(Start.AddMinutes(20), tracker.Get("internetarchive").RetryAt);

        tracker.RecordFailure(
            "internetarchive",
            ReadingSourceFailureKind.RateLimited,
            TimeSpan.FromDays(2));
        Assert.AreEqual(Start.AddHours(1), tracker.Get("internetarchive").RetryAt);

        tracker.RecordFailure(
            "internetarchive",
            ReadingSourceFailureKind.RateLimited,
            TimeSpan.FromSeconds(1));
        Assert.AreEqual(Start.AddSeconds(30), tracker.Get("internetarchive").RetryAt);

        tracker.RecordFailure("internetarchive", ReadingSourceFailureKind.RateLimited);
        Assert.AreEqual(Start.AddMinutes(5), tracker.Get("internetarchive").RetryAt);
    }

    [TestMethod]
    public void ASourceThatBlocksAutomatedAccessIsLeftAloneForAnHour()
    {
        var clock = new ManualTime(Start);
        var tracker = new ReadingSourceHealthTracker(clock);

        tracker.RecordFailure("webnovel", ReadingSourceFailureKind.Blocked);

        Assert.AreEqual(ReadingSourceHealthStatus.Blocked, tracker.Get("webnovel").Status);
        clock.Advance(TimeSpan.FromMinutes(59));
        Assert.IsFalse(tracker.CanAttempt("webnovel"));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.IsTrue(tracker.CanAttempt("webnovel"));
        // Still reported blocked until a search actually succeeds.
        Assert.AreEqual(ReadingSourceHealthStatus.Blocked, tracker.Get("webnovel").Status);
    }

    [TestMethod]
    public void SourcesAreTrackedIndependently()
    {
        var tracker = new ReadingSourceHealthTracker(new ManualTime(Start));

        tracker.RecordFailure("bookwalker");
        tracker.RecordSuccess("anilist");

        Assert.IsFalse(tracker.CanAttempt("bookwalker"));
        Assert.IsTrue(tracker.CanAttempt("anilist"));
        Assert.IsTrue(tracker.CanAttempt("syosetu"));
    }

    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private TimeSpan elapsed;

        public TimeSpan Elapsed => elapsed;

        public void Advance(TimeSpan by) => elapsed += by;

        public override DateTimeOffset GetUtcNow() => start + elapsed;
    }
}
