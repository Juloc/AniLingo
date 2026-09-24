using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Playback;

namespace AniLingo.Web.Features.ClientApi;

public static class ClientApiEndpoints
{
    public static IEndpointRouteBuilder MapClientApiV1(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup(ClientApiContract.BasePath)
            .RequireAuthorization();

        group.MapGet("/capabilities", () =>
                Results.Ok(ClientApiContract.Capabilities()))
            .AllowAnonymous();

        group.MapGet("/me", (
            HttpContext httpContext,
            CurrentAccountContext account) =>
        {
            var response = new ClientAccountResponse(
                account.ProfileId,
                httpContext.User.Identity?.Name,
                account.IsOwner ? "owner" : "user");
            return Results.Ok(response);
        });

        group.MapGet("/library", async (
            ClientApiService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetLibraryAsync(cancellationToken)));

        group.MapGet("/anime/{animeId:guid}", async (
            Guid animeId,
            ClientApiService service,
            CancellationToken cancellationToken) =>
        {
            var anime = await service.GetAnimeAsync(animeId, cancellationToken);
            return anime is null
                ? NotFound("anime_not_found", "The requested anime does not exist.")
                : Results.Ok(anime);
        });

        group.MapGet("/episodes/{episodeId:guid}", async (
            Guid episodeId,
            ClientApiService service,
            CancellationToken cancellationToken) =>
        {
            var episode = await service.GetEpisodeAsync(
                episodeId,
                cancellationToken);
            return episode is null
                ? NotFound("episode_not_found", "The requested episode does not exist.")
                : Results.Ok(episode);
        });

        group.MapGet("/episodes/{episodeId:guid}/player", async (
            Guid episodeId,
            ClientApiService service,
            CancellationToken cancellationToken) =>
        {
            var player = await service.GetPlayerAsync(
                episodeId,
                cancellationToken);
            return player is null
                ? NotFound("episode_not_found", "The requested episode does not exist.")
                : Results.Ok(player);
        });

        group.MapGet("/episodes/{episodeId:guid}/cues", async (
            Guid episodeId,
            int? fromMs,
            int? toMs,
            ClientApiService service,
            CancellationToken cancellationToken) =>
        {
            if (fromMs is < 0 || toMs is < 0)
            {
                return BadRequest(
                    "invalid_time_range",
                    "fromMs and toMs must be zero or greater.");
            }

            if (fromMs.HasValue &&
                toMs.HasValue &&
                fromMs.Value > toMs.Value)
            {
                return BadRequest(
                    "invalid_time_range",
                    "fromMs must be less than or equal to toMs.");
            }

            var cues = await service.GetCuesAsync(
                episodeId,
                fromMs,
                toMs,
                cancellationToken);

            return cues is null
                ? NotFound("episode_not_found", "The requested episode does not exist.")
                : Results.Ok(cues);
        });

        group.MapGet("/media/{mediaFileId:guid}/content", async (
            Guid mediaFileId,
            PlaybackService playbackService,
            CancellationToken cancellationToken) =>
        {
            var stream = await playbackService.GetOriginalContentAsync(
                mediaFileId,
                cancellationToken);

            if (stream is null)
            {
                return NotFound(
                    "media_not_found",
                    "The requested media file is unavailable.");
            }

            return Results.File(
                stream.SourcePath,
                stream.ContentType,
                lastModified: stream.LastModified,
                enableRangeProcessing: true);
        });

        group.MapGet("/episodes/{episodeId:guid}/fallback", async (
            Guid episodeId,
            string? mode,
            double? startSeconds,
            PlaybackService playbackService,
            CancellationToken cancellationToken) =>
        {
            if (startSeconds.HasValue &&
                (!double.IsFinite(startSeconds.Value) || startSeconds.Value < 0))
            {
                return BadRequest(
                    "invalid_start_position",
                    "startSeconds must be a finite value greater than or equal to zero.");
            }

            var requestedMode = string.Equals(
                mode,
                "device",
                StringComparison.OrdinalIgnoreCase)
                ? PlaybackRequestedMode.Device
                : PlaybackRequestedMode.Server;

            var stream = await playbackService.GetStreamAsync(
                episodeId,
                requestedMode,
                cancellationToken);

            if (stream is null || !File.Exists(stream.SourcePath))
            {
                return NotFound(
                    "playback_unavailable",
                    "No compatible playback stream is available for this episode.");
            }

            if (!stream.IsLive)
            {
                return Results.File(
                    stream.SourcePath,
                    stream.ContentType,
                    lastModified: stream.LastModified,
                    enableRangeProcessing: true);
            }

            try
            {
                var start = NormalizeStart(
                    startSeconds,
                    stream.DurationSeconds);
                var live = LivePlaybackStream.Start(
                    stream.SourcePath,
                    stream.LivePlan!,
                    start);

                return Results.File(
                    live,
                    stream.ContentType,
                    enableRangeProcessing: false);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or
                System.ComponentModel.Win32Exception)
            {
                return Results.Json(
                    new ClientErrorResponse(
                        "playback_start_failed",
                        "The server could not start the compatibility stream."),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        group.MapGet("/terms/{termId:guid}", async (
            Guid termId,
            ClientApiService service,
            CancellationToken cancellationToken) =>
        {
            var term = await service.GetTermAsync(termId, cancellationToken);
            return term is null
                ? NotFound("term_not_found", "The requested term does not exist.")
                : Results.Ok(term);
        });

        group.MapPut("/terms/{termId:guid}/state", async (
            Guid termId,
            ClientTermStateUpdate update,
            ClientApiService service,
            CancellationToken cancellationToken) =>
        {
            UserTermState state;
            if (string.Equals(update.State, "known", StringComparison.OrdinalIgnoreCase))
            {
                state = UserTermState.Known;
            }
            else if (string.Equals(update.State, "learning", StringComparison.OrdinalIgnoreCase))
            {
                state = UserTermState.Learning;
            }
            else
            {
                return BadRequest(
                    "invalid_learning_state",
                    "state must be either 'known' or 'learning'.");
            }

            var result = await service.SetTermStateAsync(
                termId,
                state,
                cancellationToken);

            return result is null
                ? NotFound("term_not_found", "The requested term does not exist.")
                : Results.Ok(result);
        });

        return endpoints;
    }

    private static IResult NotFound(string code, string message) =>
        Results.NotFound(new ClientErrorResponse(code, message));

    private static IResult BadRequest(string code, string message) =>
        Results.BadRequest(new ClientErrorResponse(code, message));

    private static double NormalizeStart(
        double? requested,
        double? durationSeconds)
    {
        if (requested is null || requested.Value <= 0)
        {
            return 0;
        }

        if (durationSeconds is > 0 && double.IsFinite(durationSeconds.Value))
        {
            return Math.Min(
                requested.Value,
                Math.Max(0, durationSeconds.Value - 0.05));
        }

        return requested.Value;
    }
}
