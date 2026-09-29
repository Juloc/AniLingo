using Jularr.Web.Features.Auth;
using Microsoft.AspNetCore.Antiforgery;

namespace Jularr.Web.Features.Storage.FolderBrowse;

/// <summary>
/// JSON endpoints behind the shared folder picker (<c>wwwroot/js/folder-browser.js</c>). They are
/// Owner-only, like every storage setting, and creating a folder additionally needs the page's
/// antiforgery request token. Errors are machine codes (<see cref="PathProblem"/> and friends); the
/// client turns them into localized sentences.
/// </summary>
public static class FolderBrowseEndpoints
{
    public const string BasePath = "/api/storage/folders";

    public static IServiceCollection AddFolderBrowse(this IServiceCollection services, string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        services.AddSingleton<IMountTable>(_ => new SystemMountTable());
        services.AddSingleton<IDirectoryAccess, DirectoryAccess>();
        services.AddSingleton(new FolderBrowseOptions(dataRoot));
        services.AddSingleton<FolderBrowseService>();
        return services;
    }

    public static IEndpointRouteBuilder MapFolderBrowse(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup(BasePath)
            .RequireAuthorization(JularrPolicies.AdminSystem);

        group.MapGet("/roots", async (
            FolderBrowseService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            NoStore(context);
            return Json(new { roots = await service.GetRootsAsync(cancellationToken) });
        });

        group.MapGet("/list", async (
            string? path,
            FolderBrowseService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            NoStore(context);
            var result = await service.BrowseAsync(path, cancellationToken);
            return result.Succeeded
                ? Json(result.Listing)
                : Json(new { problem = result.Problem }, StatusFor(result.Problem));
        });

        group.MapGet("/check", async (
            string? path,
            string? other,
            FolderBrowseService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            NoStore(context);
            return Json(await service.CheckAsync(path, directoryOnly: true, other, cancellationToken));
        });

        group.MapPost("/create", async (
            CreateFolderRequest request,
            FolderBrowseService service,
            IAntiforgery antiforgery,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            NoStore(context);
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return Json(new { error = "The request token is invalid. Reload the page." }, StatusCodes.Status400BadRequest);
            }

            var result = await service.CreateFolderAsync(request.Parent, request.Name, cancellationToken);
            return Json(result, StatusFor(result));
        });

        return endpoints;
    }

    private static void NoStore(HttpContext context) =>
        context.Response.Headers.CacheControl = "no-store";

    private static IResult Json<T>(T value, int statusCode = StatusCodes.Status200OK) =>
        Results.Json(value, FolderBrowseJson.Options, statusCode: statusCode);

    private static int StatusFor(PathProblem problem) => problem switch
    {
        PathProblem.None => StatusCodes.Status200OK,
        PathProblem.Invalid or PathProblem.NotAbsolute or PathProblem.Traversal => StatusCodes.Status400BadRequest,
        PathProblem.OutsideStorage or PathProblem.NotReadable => StatusCodes.Status403Forbidden,
        PathProblem.NotFound => StatusCodes.Status404NotFound,
        PathProblem.NotADirectory => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status503ServiceUnavailable
    };

    private static int StatusFor(CreateFolderResult result) => result.Outcome switch
    {
        CreateFolderOutcome.Created => StatusCodes.Status201Created,
        CreateFolderOutcome.InvalidName => StatusCodes.Status400BadRequest,
        CreateFolderOutcome.InvalidParent => StatusFor(result.ParentProblem),
        CreateFolderOutcome.ReadOnly or CreateFolderOutcome.NotWritable => StatusCodes.Status403Forbidden,
        CreateFolderOutcome.AlreadyExists => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError
    };
}
