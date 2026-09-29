using Jularr.Web.Features.Localization;
using Jularr.Web.Features.MediaCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Jularr.Web.Features.Shell;

/// <summary>
/// Page-level authorization for one media type's consumer routes (#598): a profile that cannot at
/// least browse any of <see cref="MediaTypes"/> gets 404 (the media type does not exist for that
/// profile) before the page is even constructed. Owners are unrestricted through the capability
/// policy itself. It runs as an authorization filter, so the page model and its services are
/// never resolved for a hidden type.
/// </summary>
public sealed class MediaTypeGateFilter(IReadOnlyList<WorkMediaType> mediaTypes) : IAsyncAuthorizationFilter
{
    public IReadOnlyList<WorkMediaType> MediaTypes { get; } = mediaTypes;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var shell = context.HttpContext.RequestServices.GetRequiredService<IAppShellService>();
        var access = await shell.GetMediaAccessAsync(context.HttpContext.User, context.HttpContext.RequestAborted);
        if (!access.IsAnyVisible(MediaTypes))
        {
            context.Result = new NotFoundResult();
        }
    }
}

public static class MediaTypeRouteGateExtensions
{
    /// <summary>
    /// Gates every page folder named in <see cref="UiNavigationCatalog.MediaRoutes"/> with a
    /// <see cref="MediaTypeGateFilter"/>. The navigation catalog is the one table that says which
    /// route belongs to which media type, so a route can never be hidden from the sidebar yet
    /// stay reachable, or the other way round.
    /// </summary>
    public static PageConventionCollection AddMediaTypeGates(this PageConventionCollection conventions)
    {
        foreach (var route in UiNavigationCatalog.MediaRoutes)
        {
            conventions.AddFolderApplicationModelConvention(
                route.Root,
                model => model.Filters.Add(new MediaTypeGateFilter(route.MediaTypes)));
        }

        return conventions;
    }
}
