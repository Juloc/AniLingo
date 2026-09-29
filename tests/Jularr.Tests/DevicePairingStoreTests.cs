using Jularr.Web.Features.Pairing;

namespace Jularr.Tests;

[TestClass]
public sealed class DevicePairingStoreTests
{
    [TestMethod]
    public void StartCreatesDistinctCodesWithExpiryAndInterval()
    {
        var store = new DevicePairingStore();

        var first = store.Start();
        var second = store.Start();

        Assert.AreNotEqual(first.DeviceCode, second.DeviceCode);
        Assert.AreNotEqual(first.UserCode, second.UserCode);
        Assert.AreEqual((int)DevicePairingStore.PairingLifetime.TotalSeconds, first.ExpiresInSeconds);
        Assert.AreEqual(DevicePairingStore.PollIntervalSeconds, first.IntervalSeconds);
        StringAssert.Matches(first.UserCode, new System.Text.RegularExpressions.Regex("^[A-Z2-9]{4}-[A-Z2-9]{4}$"));
    }

    [TestMethod]
    public void PollBeforeApprovalIsPending()
    {
        var store = new DevicePairingStore();
        var start = store.Start();

        var result = store.Poll(start.DeviceCode);

        Assert.AreEqual(DevicePairingPollOutcome.Pending, result.Outcome);
        Assert.IsNull(result.AccountId);
    }

    [TestMethod]
    public void ApprovingWithAnUnknownCodeFails()
    {
        var store = new DevicePairingStore();

        var outcome = store.Approve("ZZZZ-ZZZZ", "account-1", "account-1");

        Assert.AreEqual(DevicePairingApproveOutcome.InvalidOrExpired, outcome);
    }

    [TestMethod]
    public void ApprovedPairingCanBeExchangedExactlyOnce()
    {
        var store = new DevicePairingStore();
        var start = store.Start();

        var approved = store.Approve(start.UserCode, "account-1", "account-1");
        var first = store.Poll(start.DeviceCode);
        var second = store.Poll(start.DeviceCode);

        Assert.AreEqual(DevicePairingApproveOutcome.Approved, approved);
        Assert.AreEqual(DevicePairingPollOutcome.Approved, first.Outcome);
        Assert.AreEqual("account-1", first.AccountId);
        Assert.AreEqual(DevicePairingPollOutcome.InvalidOrExpired, second.Outcome);
    }

    [TestMethod]
    public void ApproveAcceptsTheCodeWithoutTheSeparatorOrCasing()
    {
        var store = new DevicePairingStore();
        var start = store.Start();
        var loose = start.UserCode.Replace("-", "").ToLowerInvariant();

        var outcome = store.Approve(loose, "account-1", "account-1");

        Assert.AreEqual(DevicePairingApproveOutcome.Approved, outcome);
    }

    [TestMethod]
    public void PairingExpiresAfterFiveMinutes()
    {
        var time = new TestTimeProvider(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));
        var store = new DevicePairingStore(time);
        var start = store.Start();

        time.Advance(DevicePairingStore.PairingLifetime);

        var approveOutcome = store.Approve(start.UserCode, "account-1", "account-1");
        var pollResult = store.Poll(start.DeviceCode);

        Assert.AreEqual(DevicePairingApproveOutcome.InvalidOrExpired, approveOutcome);
        Assert.AreEqual(DevicePairingPollOutcome.InvalidOrExpired, pollResult.Outcome);
    }

    [TestMethod]
    public void ExpiredApprovedPairingIsNotExchangeable()
    {
        var time = new TestTimeProvider(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));
        var store = new DevicePairingStore(time);
        var start = store.Start();
        store.Approve(start.UserCode, "account-1", "account-1");

        time.Advance(DevicePairingStore.PairingLifetime);

        var result = store.Poll(start.DeviceCode);

        Assert.AreEqual(DevicePairingPollOutcome.InvalidOrExpired, result.Outcome);
    }

    [TestMethod]
    public void ApproveAttemptsAreBoundedPerAttemptKey()
    {
        var store = new DevicePairingStore();

        for (var index = 0; index < DevicePairingStore.MaxApproveAttemptsPerWindow; index++)
        {
            var attempt = store.Approve("ZZZZ-ZZZZ", "account-1", "account-1");
            Assert.AreEqual(DevicePairingApproveOutcome.InvalidOrExpired, attempt);
        }

        var limited = store.Approve("ZZZZ-ZZZZ", "account-1", "account-1");
        Assert.AreEqual(DevicePairingApproveOutcome.RateLimited, limited);

        var anotherAccount = store.Approve("ZZZZ-ZZZZ", "account-2", "account-2");
        Assert.AreEqual(DevicePairingApproveOutcome.InvalidOrExpired, anotherAccount);
    }

    [TestMethod]
    public void PollWithBlankOrUnknownDeviceCodeIsInvalid()
    {
        var store = new DevicePairingStore();

        Assert.AreEqual(
            DevicePairingPollOutcome.InvalidOrExpired,
            store.Poll("").Outcome);
        Assert.AreEqual(
            DevicePairingPollOutcome.InvalidOrExpired,
            store.Poll("not-a-real-device-code").Outcome);
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan value)
        {
            current += value;
        }
    }
}
