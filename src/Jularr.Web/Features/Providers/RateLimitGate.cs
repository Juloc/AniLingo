namespace Jularr.Web.Features.Providers;

/// <summary>
/// The "blocked until" side of rate limiting: once a provider answers with a
/// rate-limit response, every caller pauses until the server-provided time has
/// passed. Thread-safe; the latest (furthest) block always wins so a second 429
/// never shortens an existing pause. This is the reusable primitive the
/// per-provider AniList gate and the multi-provider
/// <see cref="ProviderRateLimiter"/> are both built on.
/// </summary>
public sealed class RateLimitGate
{
    private readonly Lock sync = new();
    private DateTimeOffset? blockedUntil;

    /// <summary>The instant the pause ends, or null when requests are allowed now.</summary>
    public DateTimeOffset? BlockedUntil(DateTimeOffset now)
    {
        lock (sync)
        {
            return blockedUntil > now ? blockedUntil : null;
        }
    }

    /// <summary>Blocks until <paramref name="until"/>, keeping any later existing block.</summary>
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
}
