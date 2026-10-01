using Microsoft.Net.Http.Headers;

namespace Jularr.Web.Features.ClientApi;

public static class ClientApiOfflineMediaPackageEndpoints
{
    public static IEndpointRouteBuilder MapClientApiOfflineMediaPackageV1(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(ClientApiContract.BasePath).RequireAuthorization();
        group.MapGet("/offline-media/{kind}/{id:guid}/manifest", async (string kind, Guid id,
            ClientApiOfflineMediaPackageService service, CancellationToken cancellationToken) =>
        {
            var manifest = await service.GetManifestAsync(kind, id, cancellationToken);
            return manifest is null ? Results.NotFound() : Results.Ok(manifest);
        });
        group.MapGet("/offline-media/{kind}/{id:guid}/resources/{resourceId}", async (string kind, Guid id, string resourceId,
            ClientApiOfflineMediaPackageService service, CancellationToken cancellationToken) =>
        {
            var file = await service.ResolveAsync(kind, id, resourceId, cancellationToken);
            return file is null
                ? Results.NotFound()
                : Results.File(file.Path, file.ContentType, lastModified: new DateTimeOffset(file.LastWriteTimeUtc),
                    entityTag: new EntityTagHeaderValue(file.ETag), enableRangeProcessing: true);
        });
        return endpoints;
    }
}
