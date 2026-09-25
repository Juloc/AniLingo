using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace AniLingo.Web.Features.PlaybackSessions;

public sealed class PlaybackSessionHub(
    PlaybackSessionStore sessions,
    PlaybackSessionConnectionRegistry connections) : Hub
{
    public const string Route = "/hubs/playback-session";

    public static string GroupName(Guid sessionId) =>
        $"playback-session:{sessionId:D}";

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext()
            ?? throw new HubException("HTTP context is unavailable.");
        var rawSessionId = http.Request.Query["sessionId"].ToString();
        if (!Guid.TryParse(rawSessionId, out var sessionId))
        {
            throw new HubException("A valid sessionId is required.");
        }

        var state = sessions.Get(sessionId)
            ?? throw new HubException("Playback session does not exist.");

        var profileId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var accessKind = PlaybackParticipantAccessKind.Companion;

        if (!string.IsNullOrWhiteSpace(profileId) &&
            string.Equals(
                state.OwnerProfileId,
                profileId,
                StringComparison.Ordinal))
        {
            accessKind = PlaybackParticipantAccessKind.Owner;
        }
        else
        {
            var token = http.Request.Query["token"].ToString();
            if (sessions.GetForParticipant(sessionId, token) is null)
            {
                throw new HubException("Playback session access is invalid.");
            }
        }

        connections.Add(sessionId, Context.ConnectionId, accessKind);
        Context.Items["sessionId"] = sessionId;

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            GroupName(sessionId));
        await Clients.Caller.SendAsync("SessionState", PlaybackSessionMappings.ToPublic(state));

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue("sessionId", out var raw) &&
            raw is Guid sessionId)
        {
            connections.Remove(sessionId, Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
