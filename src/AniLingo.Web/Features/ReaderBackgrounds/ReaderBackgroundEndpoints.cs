namespace AniLingo.Web.Features.ReaderBackgrounds;

public static class ReaderBackgroundEndpoints
{
    public static IEndpointRouteBuilder MapReaderBackgroundCatalog(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/reader-backgrounds",
            (HttpRequest request, ReaderBackgroundCatalog catalog) =>
            {
                var genres = request.Query["genre"]
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Cast<string>()
                    .ToArray();
                var items = catalog.GetAll();
                var suggested = catalog.FindBestMatch(genres);

                return Results.Ok(new
                {
                    root = $"/{ReaderBackgroundCatalog.AssetRoot}/",
                    suggestedId = suggested?.Id,
                    items
                });
            })
            .WithName("GetReaderBackgroundCatalog");

        return endpoints;
    }
}
