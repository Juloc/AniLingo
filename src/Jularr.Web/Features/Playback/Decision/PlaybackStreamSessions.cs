using System.Collections.Concurrent;

namespace Jularr.Web.Features.Playback.Decision;

/// <summary>
/// Bounds concurrent server encodes. A lease is held for the lifetime of one live transcode
/// (a progressive response or an HLS session) and released when it ends.
/// </summary>
public sealed class PlaybackTranscodeSlots(int capacity = PlaybackTranscodeSlots.DefaultCapacity)
{
    public const int DefaultCapacity = 2;

    private int active;

    public int Capacity { get; } = Math.Max(1, capacity);

    public int Active => Volatile.Read(ref active);

    public int Available => Math.Max(0, Capacity - Active);

    public IDisposable? TryAcquire()
    {
        while (true)
        {
            var current = Volatile.Read(ref active);
            if (current >= Capacity)
            {
                return null;
            }

            if (Interlocked.CompareExchange(ref active, current + 1, current) == current)
            {
                return new Lease(this);
            }
        }
    }

    private void Release() => Interlocked.Decrement(ref active);

    private sealed class Lease(PlaybackTranscodeSlots owner) : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                owner.Release();
            }
        }
    }
}

/// <summary>
/// One playback session: who plays what, with which selections, under which resolved plan.
/// The source path is resolved by the server when the plan is made; clients only ever hold
/// the session id. Position and diagnostics are the live view; durable progress stays in the
/// canonical progress store.
/// </summary>
public sealed class PlaybackStreamSession(
    Guid id,
    string profileId,
    Guid episodeId,
    Guid mediaFileId,
    string sourcePath,
    double? durationSeconds,
    PlaybackPlan plan,
    PlaybackStreamSelections selections,
    DateTimeOffset createdAtUtc)
{
    private readonly object gate = new();

    public Guid Id { get; } = id;
    public string ProfileId { get; } = profileId;
    public Guid EpisodeId { get; } = episodeId;
    public Guid MediaFileId { get; } = mediaFileId;
    public string SourcePath { get; } = sourcePath;
    public double? DurationSeconds { get; } = durationSeconds;
    public PlaybackPlan Plan { get; } = plan;
    public PlaybackStreamSelections Selections { get; } = selections;
    public DateTimeOffset CreatedAtUtc { get; } = createdAtUtc;
    public DateTimeOffset LastSeenUtc { get; private set; } = createdAtUtc;
    public Guid? HlsSessionId { get; private set; }

    public void Touch(DateTimeOffset now)
    {
        lock (gate)
        {
            LastSeenUtc = now;
        }
    }

    public Guid? ReplaceHlsSession(Guid? hlsSessionId)
    {
        lock (gate)
        {
            var previous = HlsSessionId;
            HlsSessionId = hlsSessionId;
            return previous;
        }
    }
}

/// <summary>The session's selections, kept so a re-plan (fallback, quality change) starts from them.</summary>
public sealed record PlaybackStreamSelections(
    int? AudioStreamIndex,
    int? SubtitleStreamIndex,
    bool BurnInSubtitle,
    PlaybackQualityPreset Quality,
    PlaybackModePreference ModePreference,
    string ClientKind);

public sealed class PlaybackStreamSessionStore(TimeProvider time)
{
    public const int MaxSessions = 64;
    public const int MaxSessionsPerProfile = 6;
    public static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<Guid, PlaybackStreamSession> sessions = new();
    private readonly object gate = new();

    /// <summary>Invoked with a removed session so its delivery (an HLS session) can be stopped.</summary>
    public event Action<PlaybackStreamSession>? Removed;

    public int Count => sessions.Count;

    public PlaybackStreamSession Create(
        string profileId,
        Guid episodeId,
        Guid mediaFileId,
        string sourcePath,
        double? durationSeconds,
        PlaybackPlan plan,
        PlaybackStreamSelections selections,
        Guid? replaces = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var now = time.GetUtcNow();
        var removed = new List<PlaybackStreamSession>();
        PlaybackStreamSession session;
        lock (gate)
        {
            if (replaces is { } previousId &&
                sessions.TryGetValue(previousId, out var previous) &&
                previous.ProfileId == profileId &&
                sessions.TryRemove(previousId, out _))
            {
                removed.Add(previous);
            }

            removed.AddRange(RemoveExpired(now));
            while (sessions.Values.Count(x => x.ProfileId == profileId) >= MaxSessionsPerProfile &&
                   RemoveOldest(x => x.ProfileId == profileId) is { } oldestOfProfile)
            {
                removed.Add(oldestOfProfile);
            }

            while (sessions.Count >= MaxSessions && RemoveOldest(_ => true) is { } oldest)
            {
                removed.Add(oldest);
            }

            session = new PlaybackStreamSession(
                Guid.NewGuid(),
                profileId,
                episodeId,
                mediaFileId,
                sourcePath,
                durationSeconds,
                plan,
                selections,
                now);
            sessions[session.Id] = session;
        }

        foreach (var item in removed)
        {
            Removed?.Invoke(item);
        }

        return session;
    }

    /// <summary>Returns the session only to the profile that created it.</summary>
    public PlaybackStreamSession? Get(Guid sessionId, string profileId)
    {
        if (!sessions.TryGetValue(sessionId, out var session) ||
            !string.Equals(session.ProfileId, profileId, StringComparison.Ordinal))
        {
            return null;
        }

        var now = time.GetUtcNow();
        if (now - session.LastSeenUtc > IdleLifetime)
        {
            Remove(sessionId, profileId);
            return null;
        }

        session.Touch(now);
        return session;
    }

    public bool Remove(Guid sessionId, string profileId)
    {
        if (!sessions.TryGetValue(sessionId, out var session) ||
            !string.Equals(session.ProfileId, profileId, StringComparison.Ordinal) ||
            !sessions.TryRemove(sessionId, out _))
        {
            return false;
        }

        Removed?.Invoke(session);
        return true;
    }

    public void CleanupExpired()
    {
        List<PlaybackStreamSession> removed;
        lock (gate)
        {
            removed = RemoveExpired(time.GetUtcNow());
        }

        foreach (var item in removed)
        {
            Removed?.Invoke(item);
        }
    }

    private List<PlaybackStreamSession> RemoveExpired(DateTimeOffset now)
    {
        var removed = new List<PlaybackStreamSession>();
        foreach (var session in sessions.Values)
        {
            if (now - session.LastSeenUtc > IdleLifetime && sessions.TryRemove(session.Id, out _))
            {
                removed.Add(session);
            }
        }

        return removed;
    }

    private PlaybackStreamSession? RemoveOldest(Func<PlaybackStreamSession, bool> filter)
    {
        var oldest = sessions.Values.Where(filter).OrderBy(x => x.LastSeenUtc).FirstOrDefault();
        return oldest is not null && sessions.TryRemove(oldest.Id, out _) ? oldest : null;
    }
}
