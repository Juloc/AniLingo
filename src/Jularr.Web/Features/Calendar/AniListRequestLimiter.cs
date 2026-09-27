using Jularr.Web.Features.Tracking;

namespace Jularr.Web.Features.Calendar;

/// <summary>
/// Paces Jularr's background AniList requests (release schedules and franchise relations) for the
/// whole process: one request per <see cref="Spacing"/>, and none while AniList's rate limit is
/// active. A 429 with its Retry-After is recorded in the shared <see cref="AniListRateLimitGate"/>
/// (by <see cref="AniListRateLimitHandler"/> or <see cref="RateLimited"/>), so a limit hit by one
/// job pauses every other job too.
/// </summary>
public sealed class AniListRequestLimiter(AniListRateLimitGate gate, TimeProvider clock)
{
    public static readonly TimeSpan DefaultSpacing = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim slot = new(1, 1);
    private DateTimeOffset nextSlot = DateTimeOffset.MinValue;

    public TimeSpan Spacing { get; init; } = DefaultSpacing;

    /// <summary>
    /// Waits for the next request slot. Returns null when the caller may send its request now, or
    /// how long AniList asked to pause; the caller then stops and resumes after that delay.
    /// </summary>
    public async Task<TimeSpan?> WaitAsync(CancellationToken cancellationToken)
    {
        await slot.WaitAsync(cancellationToken);
        try
        {
            if (BlockedFor() is { } blocked)
            {
                return blocked;
            }

            var wait = nextSlot - clock.GetUtcNow();
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, clock, cancellationToken);
            }

            nextSlot = clock.GetUtcNow() + Spacing;
            return null;
        }
        finally
        {
            slot.Release();
        }
    }

    /// <summary>The remaining pause after a 429, or null when requests are allowed.</summary>
    public TimeSpan? BlockedFor()
    {
        var now = clock.GetUtcNow();
        return gate.BlockedUntil(now) is { } until ? until - now : null;
    }

    public void RateLimited(TimeSpan retryAfter) => gate.Block(clock.GetUtcNow() + retryAfter);
}
