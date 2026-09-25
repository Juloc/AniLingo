using AniLingo.Web.Features.Auth;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AniLingo.Web.Features.Manga;

/// <summary>
/// Raises the request body and multipart limits to the <see cref="MangaUploadService"/>
/// contract for one owner-only upload handler of a Razor Page.
/// </summary>
/// <remarks>
/// Razor Pages ignore filters on handler methods (MVC1001), and a page-wide
/// RequestSizeLimit would also let non-owners stream multi-GB bodies before the
/// handler rejects them. This runs as an authorization filter ahead of antiforgery
/// validation, which is the first component that reads the form.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class MangaUploadRequestLimitsAttribute(string handler)
    : Attribute, IAuthorizationFilter, IOrderedFilter
{
    // Headroom for multipart boundaries, section headers and the small text fields.
    public const long MultipartEnvelopeBytes = 16L * 1024 * 1024;
    public const long MaximumRequestBodyBytes =
        MangaUploadService.MaximumTotalBytes + MultipartEnvelopeBytes;

    public string Handler { get; } = handler;

    // Antiforgery validation runs at order 1000 and reads the form.
    public int Order => 900;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var httpContext = context.HttpContext;
        if (!HttpMethods.IsPost(httpContext.Request.Method)
            || !IsTargetHandler(context)
            || !httpContext.User.IsInRole(AccountRoles.Owner))
        {
            return;
        }

        var bodySize = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySize is { IsReadOnly: false })
        {
            bodySize.MaxRequestBodySize = MaximumRequestBodyBytes;
        }

        if (httpContext.Features.Get<IFormFeature>()?.Form is null)
        {
            httpContext.Features.Set<IFormFeature>(new FormFeature(
                httpContext.Request,
                new FormOptions
                {
                    MultipartBodyLengthLimit = MangaUploadService.MaximumFileBytes
                }));
        }
    }

    private bool IsTargetHandler(AuthorizationFilterContext context)
    {
        var requested = context.RouteData.Values["handler"]?.ToString();
        if (string.IsNullOrEmpty(requested))
        {
            requested = context.HttpContext.Request.Query["handler"].ToString();
        }

        return string.Equals(requested, Handler, StringComparison.OrdinalIgnoreCase);
    }
}
