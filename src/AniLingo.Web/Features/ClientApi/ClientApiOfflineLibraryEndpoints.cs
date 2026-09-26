using AniLingo.Web.Features.Novels;
using AniLingo.Web.Features.OfflineLibrary;

namespace AniLingo.Web.Features.ClientApi;

public static class ClientApiOfflineLibraryEndpoints
{
    public static IEndpointRouteBuilder MapClientApiOfflineLibraryV1(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup(ClientApiContract.BasePath)
            .RequireAuthorization();

        group.MapGet("/offline-library/works/{workId:guid}/manifest", async (
            Guid workId,
            ClientApiOfflineLibraryService service,
            CancellationToken cancellationToken) =>
        {
            var manifest = await service.GetManifestAsync(workId, cancellationToken);
            return manifest is null
                ? NotFound("work_not_found", "The requested work does not exist.")
                : Results.Ok(manifest);
        });

        group.MapGet("/offline-library/chapters/{chapterId:guid}", async (
            Guid chapterId,
            ClientApiOfflineLibraryService service,
            CancellationToken cancellationToken) =>
        {
            var chapter = await service.GetChapterAsync(chapterId, cancellationToken);
            return chapter is null
                ? NotFound("chapter_not_found", "The requested chapter does not exist.")
                : Results.Ok(chapter);
        });

        // Content-addressed volume assets (covers/illustrations) only: the file
        // name must match NovelVolumeAssetStore's hash.ext pattern, so a request
        // can never escape the volume's asset directory (no host paths, ever).
        group.MapGet("/offline-library/assets/{volumeId:guid}/{asset}", (
            Guid volumeId,
            string asset,
            ClientApiOfflineLibraryService service) =>
        {
            if (!NovelVolumeAssetStore.IsAssetName(asset))
            {
                return NotFound("asset_not_found", "The requested asset does not exist.");
            }

            var path = service.ResolveAssetPath(volumeId, asset);
            return path is null
                ? NotFound("asset_not_found", "The requested asset does not exist.")
                : Results.File(path, NovelVolumeAssetStore.ContentType(asset));
        });

        group.MapPost("/offline-library/sync", async (
            ClientOfflineLibrarySyncBatch batch,
            ClientApiOfflineLibraryService service,
            CancellationToken cancellationToken) =>
        {
            var progressCount = batch.Progress?.Count ?? 0;
            var bookmarkCount = batch.Bookmarks?.Count ?? 0;
            if (progressCount > OfflineLibraryContract.MaxSyncBatchItems ||
                bookmarkCount > OfflineLibraryContract.MaxSyncBatchItems)
            {
                return BadRequest(
                    "too_many_items",
                    $"At most {OfflineLibraryContract.MaxSyncBatchItems} events per list can be reconciled per request.");
            }

            var result = await service.SyncAsync(batch, cancellationToken);
            return Results.Ok(result);
        });

        return endpoints;
    }

    private static IResult NotFound(string code, string message) =>
        Results.NotFound(new ClientErrorResponse(code, message));

    private static IResult BadRequest(string code, string message) =>
        Results.BadRequest(new ClientErrorResponse(code, message));
}
