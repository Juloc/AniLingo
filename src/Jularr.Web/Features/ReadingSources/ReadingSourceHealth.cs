using System.Collections.Concurrent;

namespace Jularr.Web.Features.ReadingSources;

/// <summary>Why a source could not answer; decides how long it is left alone.</summary>
public enum ReadingSourceFailureKind
{
    /// <summary>Timeout, network error, server error or an unreadable response.</summary>
    Unavailable,
    /// <summary>The source asked us to slow down (HTTP 429 or 503 with Retry-After).</summary>
    RateLimited,
    /// <summary>The source refuses automated requests (HTTP 401/403 or a bot challenge).</summary>
    Blocked
}

public enum ReadingSourceHealthStatus
{
    Unknown,
    Healthy,
    Unavailable,
    Blocked
}

/// <summary>A source could not be reached in a way that should back it off, not fail the search.</summary>
public sealed class ReadingSourceUnavailableException(
    string message,
    ReadingSourceFailureKind kind,
    TimeSpan? retryAfter = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public ReadingSourceFailureKind Kind { get; } = kind;

    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public sealed record ReadingSourceHealthSnapshot(
    ReadingSourceHealthStatus Status,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    DateTimeOffset? RetryAt)
{
    public string UiKey => Status switch
    {
        ReadingSourceHealthStatus.Healthy => "readingSources.health.healthy",
        ReadingSourceHealthStatus.Unavailable => "readingSources.health.unavailable",
        ReadingSourceHealthStatus.Blocked => "readingSources.health.blocked",
        _ => "readingSources.health.unknown"
    };
}

/// <summary>
/// In-memory health of every reading source. It is runtime state, not configuration: it is
/// never persisted and starts empty after a restart. A failing source is left alone for a
/// growing cooldown so an unavailable, rate-limited or blocking site is not asked again on
/// every keystroke; a source that blocks automated access is only retried after an hour and
/// is never worked around.
/// </summary>
public sealed class ReadingSourceHealthTracker(TimeProvider time)
{
    internal static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan MaximumBackoff = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan BlockedBackoff = TimeSpan.FromHours(1);
    internal static readonly TimeSpan DefaultRateLimitBackoff = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan MaximumRetryAfter = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, SourceState> states =
        new(StringComparer.OrdinalIgnoreCase);

    public bool CanAttempt(string provider)
    {
        if (!states.TryGetValue(provider, out var state))
        {
            return true;
        }

        lock (state)
        {
            return state.RetryAt is not { } retryAt ||
                   time.GetUtcNow() >= retryAt;
        }
    }

    public void RecordSuccess(string provider)
    {
        var state = states.GetOrAdd(provider, _ => new SourceState());
        lock (state)
        {
            state.LastSuccessAt = time.GetUtcNow();
            state.ConsecutiveFailures = 0;
            state.RetryAt = null;
        }
    }

    public void RecordFailure(
        string provider,
        ReadingSourceFailureKind kind = ReadingSourceFailureKind.Unavailable,
        TimeSpan? retryAfter = null)
    {
        var state = states.GetOrAdd(provider, _ => new SourceState());
        lock (state)
        {
            var now = time.GetUtcNow();
            state.ConsecutiveFailures++;
            state.LastFailureAt = now;
            state.LastFailureKind = kind;
            state.RetryAt = now + Cooldown(
                kind,
                state.ConsecutiveFailures,
                retryAfter);
        }
    }

    public ReadingSourceHealthSnapshot Get(string provider)
    {
        if (!states.TryGetValue(provider, out var state))
        {
            return new ReadingSourceHealthSnapshot(
                ReadingSourceHealthStatus.Unknown,
                null,
                null,
                null);
        }

        lock (state)
        {
            var status = state.ConsecutiveFailures == 0
                ? state.LastSuccessAt is null
                    ? ReadingSourceHealthStatus.Unknown
                    : ReadingSourceHealthStatus.Healthy
                : state.LastFailureKind == ReadingSourceFailureKind.Blocked
                    ? ReadingSourceHealthStatus.Blocked
                    : ReadingSourceHealthStatus.Unavailable;

            return new ReadingSourceHealthSnapshot(
                status,
                state.LastSuccessAt,
                state.LastFailureAt,
                state.RetryAt);
        }
    }

    internal static TimeSpan Cooldown(
        ReadingSourceFailureKind kind,
        int consecutiveFailures,
        TimeSpan? retryAfter)
    {
        switch (kind)
        {
            case ReadingSourceFailureKind.Blocked:
                return BlockedBackoff;
            case ReadingSourceFailureKind.RateLimited:
                var requested = retryAfter ?? DefaultRateLimitBackoff;
                return requested < BaseBackoff
                    ? BaseBackoff
                    : requested > MaximumRetryAfter
                        ? MaximumRetryAfter
                        : requested;
            default:
                var doublings = Math.Clamp(consecutiveFailures - 1, 0, 10);
                var backoff = TimeSpan.FromTicks(
                    BaseBackoff.Ticks * (1L << doublings));
                return backoff > MaximumBackoff
                    ? MaximumBackoff
                    : backoff;
        }
    }

    private sealed class SourceState
    {
        public int ConsecutiveFailures { get; set; }
        public DateTimeOffset? LastSuccessAt { get; set; }
        public DateTimeOffset? LastFailureAt { get; set; }
        public ReadingSourceFailureKind LastFailureKind { get; set; }
        public DateTimeOffset? RetryAt { get; set; }
    }
}
