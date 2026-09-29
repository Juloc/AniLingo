using Jularr.Web.Features.Providers;
using Jularr.Web.Features.Tracking;

namespace Jularr.Web.Features.Calendar;

/// <summary>
/// Paces Jularr's background AniList requests (release schedules and franchise relations) for the
/// whole process: one request per <see cref="Spacing"/>, and none while AniList's rate limit is
/// active. A 429 with its Retry-After is recorded in the shared <see cref="AniListRateLimitGate"/>
/// (by <see cref="AniListRateLimitHandler"/> or <see cref="RateLimited"/>), so a limit hit by one
/// job pauses every other job too. The pacing and gate-check are the provider-framework
/// <see cref="RequestPacer"/> primitive (#438).
/// </summary>
public sealed class AniListRequestLimiter(AniListRateLimitGate gate, TimeProvider clock)
{
    public static readonly TimeSpan DefaultSpacing = TimeSpan.FromSeconds(2);

    private readonly RequestPacer pacer = new();

    public TimeSpan Spacing { get; init; } = DefaultSpacing;

    /// <summary>
    /// Waits for the next request slot. Returns null when the caller may send its request now, or
    /// how long AniList asked to pause; the caller then stops and resumes after that delay.
    /// </summary>
    public Task<TimeSpan?> WaitAsync(CancellationToken cancellationToken) =>
        pacer.WaitForTurnAsync(Spacing, gate.BlockedUntil, clock, cancellationToken);

    /// <summary>The remaining pause after a 429, or null when requests are allowed.</summary>
    public TimeSpan? BlockedFor()
    {
        var now = clock.GetUtcNow();
        return gate.BlockedUntil(now) is { } until ? until - now : null;
    }

    public void RateLimited(TimeSpan retryAfter) => gate.Block(clock.GetUtcNow() + retryAfter);
}
