using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Jularr.Web.Features.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jularr.Tests;

/// <summary>
/// The unified external-provider framework (#438): rate-limit / Retry-After handling,
/// response caching with stale-while-unavailable, health/circuit transitions, and the
/// shared execution pipeline (retries + 429 gate recording).
/// </summary>
[TestClass]
public sealed class ProviderFrameworkTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    // --- Retry-After resolution -------------------------------------------------

    [TestMethod]
    public void RetryAfterPrefersHeaderThenResetThenDefaultAndClamps()
    {
        var oneMinute = TimeSpan.FromMinutes(1);
        var oneHour = TimeSpan.FromHours(1);

        using var delta = new HttpResponseMessage((HttpStatusCode)429);
        delta.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
        Assert.AreEqual(TimeSpan.FromSeconds(30), ProviderRetryAfter.Resolve(delta, Now, oneMinute, oneHour));

        using var date = new HttpResponseMessage((HttpStatusCode)429);
        date.Headers.RetryAfter = new RetryConditionHeaderValue(Now.AddMinutes(2));
        Assert.AreEqual(TimeSpan.FromMinutes(2), ProviderRetryAfter.Resolve(date, Now, oneMinute, oneHour));

        using var reset = new HttpResponseMessage((HttpStatusCode)429);
        reset.Headers.Add("X-RateLimit-Reset", Now.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        Assert.AreEqual(TimeSpan.FromSeconds(45), ProviderRetryAfter.Resolve(reset, Now, oneMinute, oneHour));

        using var none = new HttpResponseMessage((HttpStatusCode)429);
        Assert.AreEqual(oneMinute, ProviderRetryAfter.Resolve(none, Now, oneMinute, oneHour));

        using var huge = new HttpResponseMessage((HttpStatusCode)429);
        huge.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromDays(3));
        Assert.AreEqual(oneHour, ProviderRetryAfter.Resolve(huge, Now, oneMinute, oneHour));
    }

    // --- Rate-limit gate + pacing ----------------------------------------------

    [TestMethod]
    public async Task RateLimiterReturnsRemainingPauseWhileBlockedAndIsPerProvider()
    {
        var clock = new MutableClock(Now);
        var limiter = new ProviderRateLimiter();
        limiter.Block("a", Now.AddSeconds(30));

        var pause = await limiter.WaitForTurnAsync("a", TimeSpan.Zero, clock, CancellationToken.None);
        Assert.AreEqual(TimeSpan.FromSeconds(30), pause);
        Assert.AreEqual(TimeSpan.FromSeconds(30), limiter.BlockedFor("a", Now));

        // A different provider is unaffected by "a"'s block.
        var other = await limiter.WaitForTurnAsync("b", TimeSpan.Zero, clock, CancellationToken.None);
        Assert.IsNull(other);

        // Once the block elapses the provider is allowed again.
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.IsNull(await limiter.WaitForTurnAsync("a", TimeSpan.Zero, clock, CancellationToken.None));
    }

    // --- Health / circuit -------------------------------------------------------

    [TestMethod]
    public void HealthTrackerMovesThroughUnknownHealthyDegradedUnavailableAndRecovers()
    {
        var clock = new MutableClock(Now);
        var tracker = new ProviderHealthTracker(clock, failureThreshold: 3, circuitCooldown: TimeSpan.FromMinutes(5));

        Assert.AreEqual(ProviderHealthStatus.Unknown, tracker.Get("p").Status);
        Assert.IsTrue(tracker.IsAvailable("p"), "An unseen provider is available until proven otherwise.");

        tracker.RecordSuccess("p");
        Assert.AreEqual(ProviderHealthStatus.Healthy, tracker.Get("p").Status);

        tracker.RecordFailure("p", "e1");
        tracker.RecordFailure("p", "e2");
        var degraded = tracker.Get("p");
        Assert.AreEqual(ProviderHealthStatus.Degraded, degraded.Status);
        Assert.AreEqual(2, degraded.ConsecutiveFailures);
        Assert.IsTrue(tracker.IsAvailable("p"), "Below the threshold the circuit stays closed.");

        tracker.RecordFailure("p", "e3");
        var tripped = tracker.Get("p");
        Assert.AreEqual(ProviderHealthStatus.Unavailable, tripped.Status);
        Assert.AreEqual("e3", tripped.LastError);
        Assert.IsNotNull(tripped.CircuitOpenUntil);
        Assert.IsFalse(tracker.IsAvailable("p"), "The circuit is open while tripped.");

        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.IsTrue(tracker.IsAvailable("p"), "The circuit half-opens after the cooldown.");

        tracker.RecordSuccess("p");
        var recovered = tracker.Get("p");
        Assert.AreEqual(ProviderHealthStatus.Healthy, recovered.Status);
        Assert.AreEqual(0, recovered.ConsecutiveFailures);
        Assert.IsNull(recovered.CircuitOpenUntil);
    }

    // --- Response cache: stale-while-unavailable --------------------------------

    [TestMethod]
    public async Task CacheServesFreshThenStaleWhenRefreshFailsThenRefreshesWhenAvailable()
    {
        var clock = new MutableClock(Now);
        var cache = new ProviderResponseCache(clock);
        var freshFor = TimeSpan.FromMinutes(1);

        var first = await cache.GetOrFetchAsync("k", freshFor, _ => Task.FromResult("a"));
        Assert.AreEqual("a", first);

        // Within the freshness window the fetch is not even invoked.
        var fresh = await cache.GetOrFetchAsync<string>("k", freshFor, _ => throw new InvalidOperationException("should not run"));
        Assert.AreEqual("a", fresh);

        // Stale + provider unavailable: serve the last good value.
        clock.Advance(TimeSpan.FromMinutes(2));
        var stale = await cache.GetOrFetchAsync<string>("k", freshFor, _ => throw new HttpRequestException("down"));
        Assert.AreEqual("a", stale);

        // A later successful refresh replaces the cached value.
        var refreshed = await cache.GetOrFetchAsync("k", freshFor, _ => Task.FromResult("b"));
        Assert.AreEqual("b", refreshed);
    }

    [TestMethod]
    public async Task CachePropagatesFailureWhenNothingCached()
    {
        var cache = new ProviderResponseCache(new MutableClock(Now));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => cache.GetOrFetchAsync<string>("missing", TimeSpan.FromMinutes(1), _ => throw new InvalidOperationException("cold")));
    }

    // --- Executor: HTTP path ----------------------------------------------------

    [TestMethod]
    public async Task SendAsyncRecordsRetryAfterFromA429IntoTheGate()
    {
        var clock = new MutableClock(Now);
        var limiter = new ProviderRateLimiter();
        var executor = new ProviderExecutor(limiter, new ProviderHealthTracker(clock), clock, NullLogger<ProviderExecutor>.Instance);

        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)429);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        });
        using var client = new HttpClient(handler);

        var policy = new ProviderExecutionPolicy { MaxAttempts = 1, TrackHealth = false };
        using var result = await executor.SendAsync(
            "idx",
            client,
            () => new HttpRequestMessage(HttpMethod.Get, "https://provider.example/api"),
            policy,
            CancellationToken.None);

        Assert.AreEqual((HttpStatusCode)429, result.StatusCode);
        Assert.AreEqual(Now.AddSeconds(30), limiter.BlockedUntil("idx", Now));
    }

    [TestMethod]
    public async Task SendAsyncRetriesTransientNetworkFailureThenSucceeds()
    {
        var clock = new MutableClock(Now);
        var executor = new ProviderExecutor(new ProviderRateLimiter(), new ProviderHealthTracker(clock), clock, NullLogger<ProviderExecutor>.Instance);

        var calls = 0;
        var handler = new StubHandler(_ =>
        {
            calls++;
            return calls == 1 ? throw new HttpRequestException("connection reset") : new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler);

        var policy = new ProviderExecutionPolicy { MaxAttempts = 2, BaseBackoff = TimeSpan.Zero, TrackHealth = false };
        using var result = await executor.SendAsync(
            "idx",
            client,
            () => new HttpRequestMessage(HttpMethod.Get, "https://provider.example/api"),
            policy,
            CancellationToken.None);

        Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public async Task SendAsyncRetriesServerErrorOnlyWhenPolicyAllows()
    {
        var clock = new MutableClock(Now);
        var executor = new ProviderExecutor(new ProviderRateLimiter(), new ProviderHealthTracker(clock), clock, NullLogger<ProviderExecutor>.Instance);

        var calls = 0;
        var handler = new StubHandler(_ =>
        {
            calls++;
            return calls == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler);

        var retrying = new ProviderExecutionPolicy { MaxAttempts = 2, BaseBackoff = TimeSpan.Zero, RetryServerErrors = true, TrackHealth = false };
        using var ok = await executor.SendAsync("idx", client, Request, retrying, CancellationToken.None);
        Assert.AreEqual(HttpStatusCode.OK, ok.StatusCode);
        Assert.AreEqual(2, calls);

        // With server-error retries disabled the 5xx is surfaced immediately.
        calls = 0;
        var noRetry = new ProviderExecutionPolicy { MaxAttempts = 2, BaseBackoff = TimeSpan.Zero, RetryServerErrors = false, TrackHealth = false };
        using var failed = await executor.SendAsync("idx", client, Request, noRetry, CancellationToken.None);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.AreEqual(1, calls);

        static HttpRequestMessage Request() => new(HttpMethod.Get, "https://provider.example/api");
    }

    // --- Executor: generic path fail-fast --------------------------------------

    [TestMethod]
    public async Task ExecuteAsyncFailsFastWhileRateLimited()
    {
        var clock = new MutableClock(Now);
        var limiter = new ProviderRateLimiter();
        limiter.Block("p", Now.AddSeconds(60));
        var executor = new ProviderExecutor(limiter, new ProviderHealthTracker(clock), clock, NullLogger<ProviderExecutor>.Instance);

        await Assert.ThrowsExactlyAsync<ProviderRateLimitedException>(
            () => executor.ExecuteAsync("p", _ => Task.FromResult(1), ProviderExecutionPolicy.Default, CancellationToken.None));
    }

    [TestMethod]
    public async Task ExecuteAsyncShortCircuitsWhenCircuitOpen()
    {
        var clock = new MutableClock(Now);
        var health = new ProviderHealthTracker(clock, failureThreshold: 1);
        health.RecordFailure("p", "boom");
        var executor = new ProviderExecutor(new ProviderRateLimiter(), health, clock, NullLogger<ProviderExecutor>.Instance);

        await Assert.ThrowsExactlyAsync<ProviderUnavailableException>(
            () => executor.ExecuteAsync("p", _ => Task.FromResult(1), ProviderExecutionPolicy.Default, CancellationToken.None));
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan by) => current += by;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
