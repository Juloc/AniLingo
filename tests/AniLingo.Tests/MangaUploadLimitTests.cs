using System.Reflection;
using System.Security.Claims;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Manga;
using AniLingo.Web.Pages.Discover;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using MangaIndexModel = AniLingo.Web.Pages.Manga.IndexModel;

namespace AniLingo.Tests;

[TestClass]
public sealed class MangaUploadLimitTests
{
    [TestMethod]
    public void OwnerUploadRaisesBodyAndMultipartLimits()
    {
        var context = CreateContext("POST", "Upload", AccountRoles.Owner);
        var bodySize = (TestBodySizeFeature)context.HttpContext.Features
            .Get<IHttpMaxRequestBodySizeFeature>()!;

        new MangaUploadRequestLimitsAttribute("Upload").OnAuthorization(context);

        Assert.AreEqual(
            MangaUploadService.MaximumTotalBytes + MangaUploadRequestLimitsAttribute.MultipartEnvelopeBytes,
            bodySize.MaxRequestBodySize);
        Assert.IsInstanceOfType<FormFeature>(context.HttpContext.Features.Get<IFormFeature>());
    }

    [TestMethod]
    public void HandlerFromRouteValueIsMatchedCaseInsensitively()
    {
        var context = CreateContext("POST", handler: null, AccountRoles.Owner);
        context.RouteData.Values["handler"] = "upload";

        new MangaUploadRequestLimitsAttribute("Upload").OnAuthorization(context);

        Assert.AreEqual(
            MangaUploadRequestLimitsAttribute.MaximumRequestBodyBytes,
            context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>()!.MaxRequestBodySize);
    }

    [TestMethod]
    [DataRow("POST", "Upload", AccountRoles.User)]
    [DataRow("POST", "Upload", null)]
    [DataRow("POST", "Import", AccountRoles.Owner)]
    [DataRow("POST", null, AccountRoles.Owner)]
    [DataRow("GET", "Upload", AccountRoles.Owner)]
    public void OtherRequestsKeepDefaultLimits(string method, string? handler, string? role)
    {
        var context = CreateContext(method, handler, role);

        new MangaUploadRequestLimitsAttribute("Upload").OnAuthorization(context);

        Assert.AreEqual(
            TestBodySizeFeature.DefaultLimit,
            context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>()!.MaxRequestBodySize);
        Assert.IsNull(context.HttpContext.Features.Get<IFormFeature>());
    }

    [TestMethod]
    public void ReadOnlyBodySizeFeatureIsLeftUntouched()
    {
        var context = CreateContext("POST", "Upload", AccountRoles.Owner);
        var bodySize = (TestBodySizeFeature)context.HttpContext.Features
            .Get<IHttpMaxRequestBodySizeFeature>()!;
        bodySize.IsReadOnly = true;

        new MangaUploadRequestLimitsAttribute("Upload").OnAuthorization(context);

        Assert.AreEqual(TestBodySizeFeature.DefaultLimit, bodySize.MaxRequestBodySize);
    }

    [TestMethod]
    public void LimitsApplyBeforeAntiforgeryReadsTheForm()
    {
        var antiforgery = new AutoValidateAntiforgeryTokenAttribute();

        Assert.IsTrue(new MangaUploadRequestLimitsAttribute("Upload").Order < antiforgery.Order);
    }

    [TestMethod]
    [DataRow(typeof(MangaIndexModel))]
    [DataRow(typeof(MangaImportModel))]
    public void UploadPagesDeclareLimitsAtThePageModelBoundary(Type pageModel)
    {
        var attribute = pageModel.GetCustomAttribute<MangaUploadRequestLimitsAttribute>();

        Assert.IsNotNull(attribute);
        Assert.IsNotNull(pageModel.GetMethod($"OnPost{attribute.Handler}Async"));

        foreach (var method in pageModel.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            Assert.IsNull(method.GetCustomAttribute<RequestSizeLimitAttribute>(), method.Name);
            Assert.IsNull(method.GetCustomAttribute<RequestFormLimitsAttribute>(), method.Name);
        }
    }

    private static AuthorizationFilterContext CreateContext(
        string method,
        string? handler,
        string? role)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = method;
        if (handler is not null)
        {
            httpContext.Request.QueryString = QueryString.Create("handler", handler);
        }

        httpContext.Features.Set<IHttpMaxRequestBodySizeFeature>(new TestBodySizeFeature());
        httpContext.User = role is null
            ? new ClaimsPrincipal(new ClaimsIdentity())
            : new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "account"), new Claim(ClaimTypes.Role, role)],
                "test"));

        return new AuthorizationFilterContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>());
    }

    private sealed class TestBodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public const long DefaultLimit = 30_000_000;

        public bool IsReadOnly { get; set; }

        public long? MaxRequestBodySize { get; set; } = DefaultLimit;
    }
}
