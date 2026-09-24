namespace AniLingo.Web.Features.ReaderThemes;

public static class ReaderThemeEndpoints
{
    public static IEndpointRouteBuilder MapReaderThemeCatalog(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/reader-themes",
            (HttpRequest request, ReaderThemeCatalog catalog) =>
            {
                var genres = request.Query["genre"]
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
                    .ToArray();

                var themes = catalog.GetAll();
                var suggested = catalog.FindBestMatch(genres);

                return Results.Ok(new
                {
                    root = $"/{ReaderThemeCatalog.AssetRoot}/",
                    suggestedId = suggested?.Id,
                    themes
                });
            })
            .WithName("GetReaderThemeCatalog");

        return endpoints;
    }
}
