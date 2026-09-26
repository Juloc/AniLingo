using AniLingo.Web.Features.Acquisition.Api;
using AniLingo.Web.Features.Auth;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AniLingo.Tests;

/// <summary>
/// Verifies the acquisition automation API's routing/authorization wiring without a live host:
/// every mapped endpoint must require the owner role via both the cookie scheme and the API-key
/// scheme, and none may be anonymous. This is what turns "non-owner session" into a 403 and an
/// unauthenticated request into a 401 once ASP.NET Core's authorization middleware evaluates it.
/// </summary>
[TestClass]
public sealed class AcquisitionApiEndpointsTests
{
    private static readonly string[] ExpectedPaths =
    [
        "/api/acquisition/v1/monitored",
        "/api/acquisition/v1/anime/{animeId}/monitoring", // GET
        "/api/acquisition/v1/anime/{animeId}/monitoring", // PUT
        "/api/acquisition/v1/wanted",
        "/api/acquisition/v1/search",
        "/api/acquisition/v1/anime/{animeId}/search",
        "/api/acquisition/v1/operations",
        "/api/acquisition/v1/operations/{operationId}",
        "/api/acquisition/v1/history",
        "/api/acquisition/v1/imports",
        "/api/acquisition/v1/imports/{recordId}/resolve",
        "/api/acquisition/v1/imports/{recordId}/dismiss",
        "/api/acquisition/v1/health"
    ];

    [TestMethod]
    public void EveryEndpointRequiresTheOwnerRoleViaCookieOrApiKeyAndIsNeverAnonymous()
    {
        var builder = WebApplication.CreateSlimBuilder();
        // Only needed so minimal APIs recognize AcquisitionApiService as a DI service parameter
        // (not an inferred request body) when building endpoint metadata; no handler runs here.
        builder.Services.AddSingleton<AcquisitionApiService>(_ => null!);
        var app = builder.Build();
        app.MapAcquisitionApiV1();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText!.StartsWith(AcquisitionApiRoutes.BasePath, StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(ExpectedPaths.Length, endpoints.Length, string.Join(
            ", ",
            endpoints.Select(endpoint => endpoint.RoutePattern.RawText)));

        foreach (var endpoint in endpoints)
        {
            Assert.IsNull(
                endpoint.Metadata.GetMetadata<IAllowAnonymous>(),
                $"{endpoint.RoutePattern.RawText} must not allow anonymous access.");

            var policy = endpoint.Metadata.GetMetadata<AuthorizationPolicy>();
            Assert.IsNotNull(policy, $"{endpoint.RoutePattern.RawText} must have an authorization policy.");

            Assert.IsTrue(
                policy.AuthenticationSchemes.Contains(CookieAuthenticationDefaults.AuthenticationScheme),
                $"{endpoint.RoutePattern.RawText} must accept a cookie-authenticated owner session.");
            Assert.IsTrue(
                policy.AuthenticationSchemes.Contains(AcquisitionApiKeyAuthenticationHandler.SchemeName),
                $"{endpoint.RoutePattern.RawText} must accept the X-Api-Key scheme.");
            Assert.IsTrue(
                policy.Requirements.OfType<RolesAuthorizationRequirement>().Any(requirement => requirement.AllowedRoles.Contains(AccountRoles.Owner)),
                $"{endpoint.RoutePattern.RawText} must require the owner role (non-owner sessions get 403).");
        }
    }
}
