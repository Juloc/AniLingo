using AniLingo.Web.Features.Auth;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AniLingo.Web.Features.Novels;

/// <summary>
/// Raises the request body and multipart limits for the owner-only EPUB
/// volume upload handler of a Razor Page: up to
/// <see cref="MaximumFiles"/> files of at most 100 MB each.
/// </summary>
/// <remarks>
/// Razor Pages ignore filters on handler methods, and a page-wide size limit
/// would let non-owners stream large bodies. This runs as an authorization
/// filter before antiforgery validation, which is the first form reader.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class NovelEpubUploadRequestLimitsAttribute(string handler)
    : Attribute, IAuthorizationFilter, IOrderedFilter
{
    public const int MaximumFiles = 20;
    public const long MaximumFileBytes = 100L * 1024 * 1024;
    public const long MaximumRequestBodyBytes =
        MaximumFiles * MaximumFileBytes + 16L * 1024 * 1024;

    public string Handler { get; } = handler;

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
                    MultipartBodyLengthLimit = MaximumFileBytes
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
