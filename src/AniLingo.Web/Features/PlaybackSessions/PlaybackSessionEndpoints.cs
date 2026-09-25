using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.ClientApi;
using Microsoft.AspNetCore.Authorization;

namespace AniLingo.Web.Features.PlaybackSessions;

public static class PlaybackSessionEndpoints
{
    public const string CompanionTokenHeader = "X-AniLingo-Companion-Token";

    public static IEndpointRouteBuilder MapPlaybackSessionApiV1(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(
            $"{ClientApiContract.BasePath}/playback-sessions");

        group.MapPost("/", async (
            PlaybackSessionCreateRequest request,
            PlaybackSessionStore sessions,
            ClientApiService clientApi,
            CurrentAccountContext account,
            CancellationToken cancellationToken) =>
        {
            if (request.EpisodeId == Guid.Empty)
            {
                return BadRequest(
                    "invalid_episode",
                    "A valid episodeId is required.");
            }

            if (await clientApi.GetEpisodeAsync(
                    request.EpisodeId,
                    cancellationToken) is null)
            {
                return NotFound(
                    "episode_not_found",
                    "The requested episode does not exist.");
            }

            try
            {
                var state = sessions.Create(
                    account.ProfileId,
                    request.EpisodeId,
                    request.State);

                return Results.Ok(new PlaybackSessionOwnerResponse(
                    PlaybackSessionMappings.ToPublic(state),
                    OwnerHubUrl(state.SessionId)));
            }
            catch (ArgumentOutOfRangeException exception)
            {
                return BadRequest(
                    "invalid_playback_state",
                    exception.Message);
            }
        })
        .RequireAuthorization();

        group.MapGet("/{sessionId:guid}", (
            Guid sessionId,
            PlaybackSessionStore sessions,
            CurrentAccountContext account) =>
        {
            var state = sessions.Get(sessionId);
            if (state is null)
            {
                return NotFound(
                    "playback_session_not_found",
                    "The playback session does not exist.");
            }

            return Owns(state, account)
                ? Results.Ok(new PlaybackSessionOwnerResponse(
                    PlaybackSessionMappings.ToPublic(state),
                    OwnerHubUrl(state.SessionId)))
                : Results.Forbid();
        })
        .RequireAuthorization();

        group.MapPut("/{sessionId:guid}/state", async (
            Guid sessionId,
            PlaybackSessionStateUpdateRequest request,
            PlaybackSessionStore sessions,
            PlaybackSessionCoordinator coordinator,
            CurrentAccountContext account,
            CancellationToken cancellationToken) =>
        {
            var current = sessions.Get(sessionId);
            if (current is null)
            {
                return NotFound(
                    "playback_session_not_found",
                    "The playback session does not exist.");
            }

            if (!Owns(current, account))
            {
                return Results.Forbid();
            }

            if (current.Revision != request.ExpectedRevision)
            {
                return Results.Conflict(PlaybackSessionMappings.ToPublic(current));
            }

            try
            {
                var updated = await coordinator.UpdateAsync(
                    sessionId,
                    account.ProfileId,
                    request.ExpectedRevision,
                    request.State,
                    cancellationToken);

                return updated is null
                    ? Results.Conflict(sessions.Get(sessionId) is { } latest ? PlaybackSessionMappings.ToPublic(latest) : null)
                    : Results.Ok(PlaybackSessionMappings.ToPublic(updated));
            }
            catch (ArgumentOutOfRangeException exception)
            {
                return BadRequest(
                    "invalid_playback_state",
                    exception.Message);
            }
        })
        .RequireAuthorization();

        group.MapPost("/{sessionId:guid}/pairing", (
            Guid sessionId,
            PlaybackSessionStore sessions,
            CurrentAccountContext account) =>
        {
            var current = sessions.Get(sessionId);
            if (current is null)
            {
                return NotFound(
                    "playback_session_not_found",
                    "The playback session does not exist.");
            }

            if (!Owns(current, account))
            {
                return Results.Forbid();
            }

            var pairing = sessions.CreatePairing(
                sessionId,
                account.ProfileId);
            if (pairing is null)
            {
                return Results.Conflict(new ClientErrorResponse(
                    "pairing_unavailable",
                    "Pairing could not be created for this session."));
            }

            var relativeCompanionUrl =
                $"/Companion?token={Uri.EscapeDataString(pairing.Token)}";

            return Results.Ok(new PlaybackPairingResponse(
                pairing.SessionId,
                pairing.Code,
                pairing.Token,
                relativeCompanionUrl,
                pairing.ExpiresAtUtc));
        })
        .RequireAuthorization();

        group.MapPost("/pair", (
            PlaybackPairRequest request,
            PlaybackSessionStore sessions,
            HttpContext httpContext) =>
        {
            PlaybackPairingResult result;
            if (!string.IsNullOrWhiteSpace(request.Token))
            {
                result = sessions.PairWithToken(request.Token);
            }
            else if (!string.IsNullOrWhiteSpace(request.Code))
            {
                result = sessions.PairWithCode(
                    request.Code,
                    AttemptKey(httpContext));
            }
            else
            {
                return BadRequest(
                    "pairing_credential_required",
                    "A pairing token or six-digit code is required.");
            }

            if (!result.Success)
            {
                return result.Failure == PlaybackPairingFailure.RateLimited
                    ? Results.Json(
                        new ClientErrorResponse(
                            "pairing_rate_limited",
                            "Too many manual pairing attempts. Try again later."),
                        statusCode: StatusCodes.Status429TooManyRequests)
                    : Results.Json(
                        new ClientErrorResponse(
                            "pairing_invalid",
                            "The pairing credential is invalid or expired."),
                        statusCode: StatusCodes.Status404NotFound);
            }

            var grant = result.Grant!;
            return Results.Ok(new PlaybackParticipantResponse(
                grant.SessionId,
                grant.AccessToken,
                PlaybackSessionMappings.ToPublic(grant.State),
                ParticipantHubUrl(
                    grant.SessionId,
                    grant.AccessToken)));
        })
        .AllowAnonymous();

        group.MapGet("/{sessionId:guid}/participant-state", (
            Guid sessionId,
            PlaybackSessionStore sessions,
            HttpContext httpContext) =>
        {
            var token = CompanionToken(httpContext);
            if (string.IsNullOrWhiteSpace(token))
            {
                return Results.Unauthorized();
            }

            var state = sessions.GetForParticipant(sessionId, token);
            return state is null
                ? Results.Unauthorized()
                : Results.Ok(PlaybackSessionMappings.ToPublic(state));
        })
        .AllowAnonymous();

        group.MapPost("/{sessionId:guid}/commands", async (
            Guid sessionId,
            PlaybackCommandRequest request,
            PlaybackSessionCoordinator coordinator,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var token = CompanionToken(httpContext);
            if (string.IsNullOrWhiteSpace(token))
            {
                return Results.Unauthorized();
            }

            if (request.CommandId == Guid.Empty ||
                !PlaybackSessionProtocol.IsSupportedCommand(request.Type))
            {
                return BadRequest(
                    "invalid_command",
                    "The playback command is invalid or unsupported.");
            }

            var command = new PlaybackSessionCommand(
                request.CommandId,
                sessionId,
                request.ExpectedRevision,
                request.Type,
                request.Payload
                    ?? new Dictionary<string, string>(
                        StringComparer.Ordinal),
                DateTimeOffset.UtcNow);

            var result = await coordinator.AcceptCommandAsync(
                command,
                token,
                cancellationToken);

            return result.Acceptance switch
            {
                PlaybackCommandAcceptance.Accepted =>
                    Results.Ok(ToApiResult(result)),
                PlaybackCommandAcceptance.Duplicate =>
                    Results.Ok(ToApiResult(result)),
                PlaybackCommandAcceptance.StaleRevision =>
                    Results.Conflict(ToApiResult(result)),
                PlaybackCommandAcceptance.SessionNotFound =>
                    NotFound(
                        "playback_session_not_found",
                        "The playback session does not exist."),
                _ => Results.Unauthorized()
            };
        })
        .AllowAnonymous();

        group.MapPost("/{sessionId:guid}/revoke", async (
            Guid sessionId,
            PlaybackSessionStore sessions,
            PlaybackSessionCoordinator coordinator,
            CurrentAccountContext account,
            CancellationToken cancellationToken) =>
        {
            var current = sessions.Get(sessionId);
            if (current is null)
            {
                return NotFound(
                    "playback_session_not_found",
                    "The playback session does not exist.");
            }

            if (!Owns(current, account))
            {
                return Results.Forbid();
            }

            return await coordinator.RevokeParticipantsAsync(
                    sessionId,
                    account.ProfileId,
                    cancellationToken)
                ? Results.NoContent()
                : Results.Conflict(new ClientErrorResponse(
                    "revoke_failed",
                    "Companion access could not be revoked."));
        })
        .RequireAuthorization();

        group.MapDelete("/{sessionId:guid}", async (
            Guid sessionId,
            PlaybackSessionStore sessions,
            PlaybackSessionCoordinator coordinator,
            CurrentAccountContext account,
            CancellationToken cancellationToken) =>
        {
            var current = sessions.Get(sessionId);
            if (current is null)
            {
                return Results.NoContent();
            }

            if (!Owns(current, account))
            {
                return Results.Forbid();
            }

            return await coordinator.EndAsync(
                    sessionId,
                    account.ProfileId,
                    cancellationToken)
                ? Results.NoContent()
                : Results.Conflict(new ClientErrorResponse(
                    "session_end_failed",
                    "The playback session could not be ended."));
        })
        .RequireAuthorization();

        return endpoints;
    }

    private static bool Owns(
        PlaybackSessionState state,
        CurrentAccountContext account) =>
        string.Equals(
            state.OwnerProfileId,
            account.ProfileId,
            StringComparison.Ordinal);

    private static string? CompanionToken(HttpContext httpContext)
    {
        var value = httpContext.Request.Headers[CompanionTokenHeader]
            .ToString();
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string AttemptKey(HttpContext httpContext)
    {
        var address = httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
        var agent = httpContext.Request.Headers.UserAgent.ToString();
        return $"{address}|{agent}";
    }

    private static string OwnerHubUrl(Guid sessionId) =>
        $"{PlaybackSessionHub.Route}?sessionId={sessionId:D}";

    private static string ParticipantHubUrl(
        Guid sessionId,
        string token) =>
        $"{PlaybackSessionHub.Route}?sessionId={sessionId:D}&token={Uri.EscapeDataString(token)}";

    private static PlaybackCommandApiResult ToApiResult(
        PlaybackCommandResult result) =>
        new(
            result.Acceptance.ToString().ToLowerInvariant(),
            result.State is null
                ? null
                : PlaybackSessionMappings.ToPublic(result.State),
            result.Command);

    private static IResult NotFound(string code, string message) =>
        Results.NotFound(new ClientErrorResponse(code, message));

    private static IResult BadRequest(string code, string message) =>
        Results.BadRequest(new ClientErrorResponse(code, message));
}
