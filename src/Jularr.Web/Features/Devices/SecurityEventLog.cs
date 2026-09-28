namespace Jularr.Web.Features.Devices;

/// <summary>Kind of a recorded <see cref="SecurityEvent"/>.</summary>
public enum SecurityEventKind
{
    LoginSucceeded,
    LoginFailed
}

/// <summary>
/// One authentication event for the Admin &gt; Devices &amp; security overview (#527). Never
/// carries a password, password hash, cookie or token: only who was involved, what happened,
/// when, and where from.
/// </summary>
public sealed record SecurityEvent(
    DateTime OccurredAtUtc,
    SecurityEventKind Kind,
    string? AccountId,
    string UserName,
    string? RemoteAddress);

/// <summary>
/// Bounded in-memory ring of recent sign-in events, for the "Remote access &amp; security"
/// overview asked for by #510/#527. Intentionally not persisted: a first implementation, without
/// a retention/schema design yet (docs/INFORMATION_ARCHITECTURE.md §5.1). Restarting the app
/// clears it; nothing here is a substitute for a real audit log if one is needed later.
/// </summary>
public sealed class SecurityEventLog(TimeProvider time)
{
    public const int Capacity = 200;

    private readonly object gate = new();
    private readonly LinkedList<SecurityEvent> events = new();

    public void Record(SecurityEventKind kind, string? accountId, string userName, string? remoteAddress)
    {
        var entry = new SecurityEvent(
            time.GetUtcNow().UtcDateTime,
            kind,
            accountId,
            string.IsNullOrWhiteSpace(userName) ? "?" : userName.Trim(),
            string.IsNullOrWhiteSpace(remoteAddress) ? null : remoteAddress.Trim());

        lock (gate)
        {
            events.AddFirst(entry);
            while (events.Count > Capacity)
            {
                events.RemoveLast();
            }
        }
    }

    /// <summary>Most recent first.</summary>
    public IReadOnlyList<SecurityEvent> Recent(int limit = 50)
    {
        lock (gate)
        {
            return events.Take(limit).ToArray();
        }
    }
}
