using Jularr.Web.Features.Devices;

namespace Jularr.Tests;

/// <summary>
/// The bounded in-memory sign-in activity ring behind Admin &gt; Devices &amp; security (#527,
/// part of epic #510).
/// </summary>
[TestClass]
public sealed class SecurityEventLogTests
{
    [TestMethod]
    public void RecentReturnsNewestFirst()
    {
        var time = new TestTimeProvider(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));
        var log = new SecurityEventLog(time);

        log.Record(SecurityEventKind.LoginSucceeded, "owner", "Owner", "127.0.0.1");
        time.Advance(TimeSpan.FromSeconds(1));
        log.Record(SecurityEventKind.LoginFailed, null, "unknown-user", "10.0.0.5");

        var recent = log.Recent();

        Assert.AreEqual(2, recent.Count);
        Assert.AreEqual(SecurityEventKind.LoginFailed, recent[0].Kind, "The newest event must be first.");
        Assert.AreEqual("unknown-user", recent[0].UserName);
        Assert.IsNull(recent[0].AccountId, "A failed sign-in has no resolved account.");
        Assert.AreEqual(SecurityEventKind.LoginSucceeded, recent[1].Kind);
        Assert.AreEqual("owner", recent[1].AccountId);
    }

    [TestMethod]
    public void TheRingNeverGrowsPastItsCapacity()
    {
        var log = new SecurityEventLog(TimeProvider.System);

        for (var index = 0; index < SecurityEventLog.Capacity + 25; index++)
        {
            log.Record(SecurityEventKind.LoginFailed, null, $"user-{index}", null);
        }

        var recent = log.Recent(limit: SecurityEventLog.Capacity + 25);

        Assert.AreEqual(SecurityEventLog.Capacity, recent.Count);
        Assert.AreEqual($"user-{SecurityEventLog.Capacity + 24}", recent[0].UserName, "The most recent event must survive.");
    }

    [TestMethod]
    public void RecordNeverCarriesAPasswordOrTokenField()
    {
        // Defense in depth (#527): the event shape itself has no field that could ever hold a
        // password, hash or token, so a future call site cannot accidentally log one.
        var properties = typeof(SecurityEvent).GetProperties().Select(x => x.Name).ToArray();

        foreach (var forbidden in new[] { "Password", "Hash", "Token", "Cookie", "Secret" })
        {
            Assert.IsFalse(
                properties.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)),
                $"SecurityEvent must never carry a {forbidden}-like field.");
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan value) => current += value;
    }
}
