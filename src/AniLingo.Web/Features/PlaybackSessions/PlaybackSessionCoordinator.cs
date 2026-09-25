using Microsoft.AspNetCore.SignalR;

namespace AniLingo.Web.Features.PlaybackSessions;

public sealed class PlaybackSessionCoordinator(
    PlaybackSessionStore sessions,
    PlaybackSessionConnectionRegistry connections,
    IHubContext<PlaybackSessionHub> hub)
{
    public async Task<PlaybackSessionState?> UpdateAsync(
        Guid sessionId,
        string ownerProfileId,
        long expectedRevision,
        PlaybackSessionUpdate update,
        CancellationToken cancellationToken)
    {
        var state = sessions.Update(
            sessionId,
            ownerProfileId,
            expectedRevision,
            update);

        if (state is not null)
        {
            await hub.Clients
                .Group(PlaybackSessionHub.GroupName(sessionId))
                .SendAsync("SessionState", PlaybackSessionMappings.ToPublic(state), cancellationToken);
        }

        return state;
    }

    public async Task<PlaybackCommandResult> AcceptCommandAsync(
        PlaybackSessionCommand command,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var result = sessions.AcceptCommand(command, accessToken);
        if (result.Accepted && result.Command is not null)
        {
            await hub.Clients
                .Group(PlaybackSessionHub.GroupName(command.SessionId))
                .SendAsync("Command", result.Command, cancellationToken);
        }

        return result;
    }

    public async Task<bool> RevokeParticipantsAsync(
        Guid sessionId,
        string ownerProfileId,
        CancellationToken cancellationToken)
    {
        var participantConnections =
            connections.CompanionConnections(sessionId);

        if (!sessions.RevokeParticipants(sessionId, ownerProfileId))
        {
            return false;
        }

        foreach (var connectionId in participantConnections)
        {
            await hub.Clients
                .Client(connectionId)
                .SendAsync("AccessRevoked", sessionId, cancellationToken);
            await hub.Groups.RemoveFromGroupAsync(
                connectionId,
                PlaybackSessionHub.GroupName(sessionId),
                cancellationToken);
            connections.Remove(sessionId, connectionId);
        }

        return true;
    }

    public async Task<bool> EndAsync(
        Guid sessionId,
        string ownerProfileId,
        CancellationToken cancellationToken)
    {
        var allConnections = connections.AllConnections(sessionId);
        if (!sessions.End(sessionId, ownerProfileId))
        {
            return false;
        }

        await hub.Clients
            .Group(PlaybackSessionHub.GroupName(sessionId))
            .SendAsync("SessionEnded", sessionId, cancellationToken);

        foreach (var connectionId in allConnections)
        {
            await hub.Groups.RemoveFromGroupAsync(
                connectionId,
                PlaybackSessionHub.GroupName(sessionId),
                cancellationToken);
            connections.Remove(sessionId, connectionId);
        }

        return true;
    }
}

public static class PlaybackSessionProtocol
{
    private static readonly HashSet<string> SupportedCommands =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "playPause",
            "seekBack10",
            "seekForward10",
            "seekTo",
            "selectAudioTrack",
            "selectSubtitleTrack",
            "repeatCurrentCue",
            "learnCurrentCue",
            "openWord",
            "markKnown",
            "addToLearning",
            "openCompanion",
            "closeOverlay",
            "exitPlayer"
        };

    public static bool IsSupportedCommand(string? command) =>
        !string.IsNullOrWhiteSpace(command) &&
        SupportedCommands.Contains(command);
}
