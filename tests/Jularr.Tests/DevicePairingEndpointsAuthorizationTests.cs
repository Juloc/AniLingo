using Jularr.Web.Features.Auth;
using Jularr.Web.Features.ClientApi;
using Jularr.Web.Features.Pairing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jularr.Tests;

/// <summary>
/// Verifies the device-pairing route's routing/authorization wiring without a live host (this
/// repository has no WebApplicationFactory harness, see
/// <c>ClientApiWatchlistEndpointTests</c>/<c>OfflineLibraryReaderIntegrationTests</c>): a fresh TV
/// with no session must be able to reach start/poll, while approve — which signs the caller's own
/// account into the pairing — must never be reachable anonymously.
/// </summary>
[TestClass]
public sealed class DevicePairingEndpointsAuthorizationTests
{
    [TestMethod]
    public void StartAndPollAllowAnonymousAccess()
    {
        var app = BuildApp();

        var start = FindEndpoint(app, $"{ClientApiContract.BasePath}/pairing/start");
        var poll = FindEndpoint(app, $"{ClientApiContract.BasePath}/pairing/poll");

        Assert.IsNotNull(
            start.Metadata.GetMetadata<IAllowAnonymous>(),
            "POST /pairing/start must allow anonymous access: a fresh TV has no session yet.");
        Assert.IsNotNull(
            poll.Metadata.GetMetadata<IAllowAnonymous>(),
            "POST /pairing/poll must allow anonymous access: the TV is not signed in while it polls.");
    }

    [TestMethod]
    public void ApproveRequiresAuthenticationAndIsNeverAnonymous()
    {
        var app = BuildApp();

        var approve = FindEndpoint(app, $"{ClientApiContract.BasePath}/pairing/approve");

        Assert.IsNull(
            approve.Metadata.GetMetadata<IAllowAnonymous>(),
            "POST /pairing/approve must not allow anonymous access: it signs the caller's own account into the pairing.");
        Assert.IsTrue(
            approve.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0,
            "POST /pairing/approve must require authorization.");
    }

    [TestMethod]
    public void CapabilitiesAdvertiseDevicePairing()
    {
        Assert.IsTrue(ClientApiContract.Capabilities().Features.DevicePairing);
    }

    private static WebApplication BuildApp()
    {
        var builder = WebApplication.CreateSlimBuilder();
        // Only needed so minimal APIs recognize these as DI service parameters (not an inferred
        // request body) when building endpoint metadata; no handler runs in this test.
        builder.Services.AddSingleton<DevicePairingStore>(_ => null!);
        builder.Services.AddSingleton<CurrentAccountContext>(_ => null!);
        builder.Services.AddSingleton<OwnerAuthService>(_ => null!);
        var app = builder.Build();
        app.MapPairingApiV1();
        return app;
    }

    private static RouteEndpoint FindEndpoint(WebApplication app, string rawRouteText) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == rawRouteText);
}
