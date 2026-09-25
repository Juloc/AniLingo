using System.Collections.Concurrent;

namespace AniLingo.Web.Features.PlaybackSessions;

public sealed class PlaybackSessionConnectionRegistry
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, PlaybackParticipantAccessKind>>
        sessions = new();

    public void Add(
        Guid sessionId,
        string connectionId,
        PlaybackParticipantAccessKind kind)
    {
        var connections = sessions.GetOrAdd(
            sessionId,
            static _ => new ConcurrentDictionary<string, PlaybackParticipantAccessKind>(
                StringComparer.Ordinal));
        connections[connectionId] = kind;
    }

    public void Remove(Guid sessionId, string connectionId)
    {
        if (!sessions.TryGetValue(sessionId, out var connections))
        {
            return;
        }

        connections.TryRemove(connectionId, out _);
        if (connections.IsEmpty)
        {
            sessions.TryRemove(sessionId, out _);
        }
    }

    public IReadOnlyList<string> CompanionConnections(Guid sessionId) =>
        GetConnections(sessionId, PlaybackParticipantAccessKind.Companion);

    public IReadOnlyList<string> AllConnections(Guid sessionId)
    {
        if (!sessions.TryGetValue(sessionId, out var connections))
        {
            return [];
        }

        return connections.Keys.ToArray();
    }

    private IReadOnlyList<string> GetConnections(
        Guid sessionId,
        PlaybackParticipantAccessKind kind)
    {
        if (!sessions.TryGetValue(sessionId, out var connections))
        {
            return [];
        }

        return connections
            .Where(x => x.Value == kind)
            .Select(x => x.Key)
            .ToArray();
    }
}
