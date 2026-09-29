using System.Collections.Concurrent;

namespace Jularr.Web.Features.Providers;

/// <summary>
/// Per-provider rate limiting for callers that go through <see cref="ProviderExecutor"/>
/// (the migrated indexers and future provider families). It composes one
/// <see cref="RateLimitGate"/> and one <see cref="RequestPacer"/> per provider key,
/// so a 429 from one provider never pauses an unrelated one. Registered as a
/// singleton; state is in-memory runtime state (never persisted).
/// </summary>
public sealed class ProviderRateLimiter
{
    private readonly ConcurrentDictionary<string, State> states =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Records a rate-limit pause for <paramref name="key"/> until <paramref name="until"/>.</summary>
    public void Block(string key, DateTimeOffset until) =>
        StateFor(key).Gate.Block(until);

    /// <summary>When the pause for <paramref name="key"/> ends, or null when requests are allowed.</summary>
    public DateTimeOffset? BlockedUntil(string key, DateTimeOffset now) =>
        StateFor(key).Gate.BlockedUntil(now);

    /// <summary>The remaining pause for <paramref name="key"/>, or null when requests are allowed.</summary>
    public TimeSpan? BlockedFor(string key, DateTimeOffset now) =>
        BlockedUntil(key, now) is { } until ? until - now : null;

    /// <summary>
    /// Waits for the next request slot for <paramref name="key"/>. Returns null when the
    /// caller may proceed, or the remaining pause when the provider is rate limited.
    /// </summary>
    public Task<TimeSpan?> WaitForTurnAsync(
        string key,
        TimeSpan spacing,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var state = StateFor(key);
        return state.Pacer.WaitForTurnAsync(spacing, state.Gate.BlockedUntil, clock, cancellationToken);
    }

    private State StateFor(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Provider key is required.", nameof(key));
        }

        return states.GetOrAdd(key, _ => new State());
    }

    private sealed class State
    {
        public RateLimitGate Gate { get; } = new();

        public RequestPacer Pacer { get; } = new();
    }
}
