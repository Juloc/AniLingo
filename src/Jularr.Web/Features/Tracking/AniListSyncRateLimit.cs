using System.Net;
using Jularr.Web.Features.Providers;

namespace Jularr.Web.Features.Tracking;

/// <summary>
/// Server-wide AniList rate-limit state. AniList limits requests per client
/// address, so one 429 pauses automatic sync for every profile until the
/// server-provided retry time has passed. Manual requests are never blocked.
/// This is AniList's named gate over the shared provider-framework
/// <see cref="RateLimitGate"/> primitive (#438); the retry-after resolution is
/// the framework's shared implementation.
/// </summary>
public sealed class AniListRateLimitGate
{
    public static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(1);

    private readonly RateLimitGate gate = new();

    public DateTimeOffset? BlockedUntil(DateTimeOffset now) => gate.BlockedUntil(now);

    public void Block(DateTimeOffset until) => gate.Block(until);

    /// <summary>
    /// Resolves how long to wait after a 429: <c>Retry-After</c> (seconds or
    /// HTTP date) first, then AniList's <c>X-RateLimit-Reset</c> epoch seconds,
    /// otherwise one minute. The result is clamped to [1 s, 1 h].
    /// </summary>
    public static TimeSpan ResolveRetryAfter(
        HttpResponseMessage response,
        DateTimeOffset now) =>
        ProviderRetryAfter.Resolve(response, now, DefaultRetryAfter, MaxRetryAfter);
}

/// <summary>
/// Observes AniList responses on the <see cref="AniListAccountService"/>
/// client and records HTTP 429 into <see cref="AniListRateLimitGate"/>. When a
/// <see cref="ProviderHealthTracker"/> is available it also records AniList's
/// provider-level health (success on 2xx, failure on 429/5xx) so admin sees the
/// integration status; it never alters or short-circuits requests.
/// </summary>
public sealed class AniListRateLimitHandler(
    AniListRateLimitGate gate,
    TimeProvider timeProvider,
    ProviderHealthTracker? health = null) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var now = timeProvider.GetUtcNow();
            gate.Block(now + AniListRateLimitGate.ResolveRetryAfter(response, now));
            health?.RecordFailure(ProviderKeys.AniList, "HTTP 429");
        }
        else if ((int)response.StatusCode >= 500)
        {
            health?.RecordFailure(ProviderKeys.AniList, $"HTTP {(int)response.StatusCode}");
        }
        else if (response.IsSuccessStatusCode)
        {
            health?.RecordSuccess(ProviderKeys.AniList);
        }

        return response;
    }
}
