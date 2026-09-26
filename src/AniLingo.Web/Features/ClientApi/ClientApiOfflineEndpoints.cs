using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Playback;
using AniLingo.Web.Features.Progress;
using AniLingo.Web.Features.Storage;
using Microsoft.Net.Http.Headers;

namespace AniLingo.Web.Features.ClientApi;

public static class ClientApiOfflineEndpoints
{
    public static IEndpointRouteBuilder MapClientApiOfflineV1(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup(ClientApiContract.BasePath)
            .RequireAuthorization();

        group.MapGet("/episodes/{episodeId:guid}/offline-download", async (
            Guid episodeId,
            ClientApiOfflineService service,
            CancellationToken cancellationToken) =>
        {
            var lookup = await service.GetDownloadAsync(episodeId, cancellationToken);
            return lookup.Status switch
            {
                OfflineDownloadLookupStatus.Ready => Results.Ok(lookup.Descriptor),
                OfflineDownloadLookupStatus.EpisodeNotFound => NotFound(
                    "episode_not_found",
                    "The requested episode does not exist."),
                OfflineDownloadLookupStatus.MediaNotFound => NotFound(
                    "media_not_found",
                    "This episode does not have a downloadable media file."),
                _ => Unavailable(lookup.Availability!)
            };
        });

        // Same read-only original bytes as /media/{id}/content, plus a strong ETag so
        // resumed downloads can use If-Range and never mix two versions of a file.
        group.MapGet("/offline/media/{mediaFileId:guid}/content", async (
            Guid mediaFileId,
            PlaybackService playbackService,
            MediaAvailabilityService mediaAvailability,
            CurrentAccountContext currentAccount,
            CancellationToken cancellationToken) =>
        {
            var availability = await mediaAvailability.CheckMediaAsync(
                mediaFileId,
                force: false,
                cancellationToken);

            if (availability is null)
            {
                return NotFound("media_not_found", "The requested media file does not exist.");
            }

            if (!availability.IsAvailable)
            {
                return Unavailable(ClientApiMappings.ToClientAvailability(
                    availability,
                    currentAccount.IsOwner));
            }

            var stream = await playbackService.GetOriginalContentAsync(
                mediaFileId,
                cancellationToken);
            var file = stream is null ? null : new FileInfo(stream.SourcePath);
            if (stream is null || file is not { Exists: true })
            {
                return NotFound("media_not_found", "The requested media file is unavailable.");
            }

            return Results.File(
                file.FullName,
                stream.ContentType,
                lastModified: new DateTimeOffset(file.LastWriteTimeUtc),
                entityTag: new EntityTagHeaderValue(
                    ClientApiOfflineContract.EntityTag(file.Length, file.LastWriteTimeUtc)),
                enableRangeProcessing: true);
        });

        group.MapPost("/offline/progress", async (
            ClientOfflineProgressBatch batch,
            OfflineProgressReconciler reconciler,
            CancellationToken cancellationToken) =>
        {
            var items = batch.Items ?? [];
            if (items.Count > ClientApiOfflineContract.MaxProgressBatchItems)
            {
                return BadRequest(
                    "too_many_items",
                    $"At most {ClientApiOfflineContract.MaxProgressBatchItems} offline checkpoints can be reconciled per request.");
            }

            if (items.Any(x => x.PositionMs < 0 || x.DurationMs is < 0))
            {
                return BadRequest(
                    "invalid_progress",
                    "Playback progress values must be zero or greater.");
            }

            var results = await reconciler.ReconcileAsync(
                [
                    .. items.Select(x => new OfflineProgressCheckpoint(
                        x.EpisodeId,
                        x.PositionMs,
                        x.DurationMs,
                        x.Completed))
                ],
                cancellationToken);

            return Results.Ok(new ClientOfflineProgressResponse(
            [
                .. results.Select(x => new ClientOfflineProgressResult(
                    x.EpisodeId,
                    ClientApiOfflineContract.OutcomeName(x.Outcome),
                    x.Progress is null
                        ? null
                        : ClientApiMappings.ToClientEpisodeProgress(x.Progress)))
            ]));
        });

        return endpoints;
    }

    private static IResult Unavailable(ClientMediaAvailability availability) =>
        Results.Json(
            availability,
            statusCode: availability.State == ClientApiMappings.AvailabilityStateName(
                StorageAvailabilityState.FileMissing)
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status503ServiceUnavailable);

    private static IResult NotFound(string code, string message) =>
        Results.NotFound(new ClientErrorResponse(code, message));

    private static IResult BadRequest(string code, string message) =>
        Results.BadRequest(new ClientErrorResponse(code, message));
}
