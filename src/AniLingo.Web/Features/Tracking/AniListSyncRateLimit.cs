using System.Globalization;
using System.Net;

namespace AniLingo.Web.Features.Tracking;

/// <summary>
/// Server-wide AniList rate-limit state. AniList limits requests per client
/// address, so one 429 pauses automatic sync for every profile until the
/// server-provided retry time has passed. Manual requests are never blocked.
/// </summary>
public sealed class AniListRateLimitGate
{
    public static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(1);

    private readonly Lock sync = new();
    private DateTimeOffset? blockedUntil;

    public DateTimeOffset? BlockedUntil(DateTimeOffset now)
    {
        lock (sync)
        {
            return blockedUntil > now ? blockedUntil : null;
        }
    }

    public void Block(DateTimeOffset until)
    {
        lock (sync)
        {
            if (blockedUntil is null || until > blockedUntil)
            {
                blockedUntil = until;
            }
        }
    }

    /// <summary>
    /// Resolves how long to wait after a 429: <c>Retry-After</c> (seconds or
    /// HTTP date) first, then AniList's <c>X-RateLimit-Reset</c> epoch seconds,
    /// otherwise one minute. The result is clamped to [1 s, 1 h].
    /// </summary>
    public static TimeSpan ResolveRetryAfter(
        HttpResponseMessage response,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(response);

        TimeSpan? wait = response.Headers.RetryAfter switch
        {
            { Delta: TimeSpan delta } => delta,
            { Date: DateTimeOffset date } => date - now,
            _ => null
        };

        if (wait is null &&
            response.Headers.TryGetValues("X-RateLimit-Reset", out var values) &&
            long.TryParse(
                values.FirstOrDefault(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var resetEpochSeconds))
        {
            wait = DateTimeOffset.FromUnixTimeSeconds(resetEpochSeconds) - now;
        }

        var resolved = wait ?? DefaultRetryAfter;
        return resolved < TimeSpan.FromSeconds(1)
            ? TimeSpan.FromSeconds(1)
            : resolved > MaxRetryAfter
                ? MaxRetryAfter
                : resolved;
    }
}

/// <summary>
/// Observes AniList responses on the <see cref="AniListAccountService"/>
/// client and records HTTP 429 into <see cref="AniListRateLimitGate"/>. It
/// never alters or short-circuits requests.
/// </summary>
public sealed class AniListRateLimitHandler(
    AniListRateLimitGate gate,
    TimeProvider timeProvider) : DelegatingHandler
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
        }

        return response;
    }
}
