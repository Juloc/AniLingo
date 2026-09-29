namespace Jularr.Web.Features.Providers;

/// <summary>
/// Paces background requests to one provider: at most one request per
/// <c>spacing</c> interval, and none while a <see cref="RateLimitGate"/> is
/// active. This is the reusable primitive behind the AniList request limiter and
/// the multi-provider <see cref="ProviderRateLimiter"/>.
/// </summary>
public sealed class RequestPacer
{
    private readonly SemaphoreSlim slot = new(1, 1);
    private DateTimeOffset nextSlot = DateTimeOffset.MinValue;

    /// <summary>
    /// Waits for the next request slot. Returns null when the caller may send its
    /// request now, or the remaining pause when the gate is active; in the latter
    /// case no slot is consumed, so the caller stops and resumes after the delay.
    /// <paramref name="blockedUntil"/> reports the current gate state (for example
    /// <see cref="RateLimitGate.BlockedUntil"/>).
    /// </summary>
    public async Task<TimeSpan?> WaitForTurnAsync(
        TimeSpan spacing,
        Func<DateTimeOffset, DateTimeOffset?> blockedUntil,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(blockedUntil);
        ArgumentNullException.ThrowIfNull(clock);

        await slot.WaitAsync(cancellationToken);
        try
        {
            var now = clock.GetUtcNow();
            if (blockedUntil(now) is { } until)
            {
                return until - now;
            }

            var wait = nextSlot - now;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, clock, cancellationToken);
            }

            nextSlot = clock.GetUtcNow() + spacing;
            return null;
        }
        finally
        {
            slot.Release();
        }
    }
}
