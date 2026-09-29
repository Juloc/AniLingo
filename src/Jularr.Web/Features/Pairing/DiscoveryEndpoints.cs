using Microsoft.AspNetCore.Hosting.Server;

namespace Jularr.Web.Features.Pairing;

/// <summary>
/// Unauthenticated HTTP counterpart of the UDP discovery beacon (#489,
/// <see cref="DiscoveryBeaconService"/>): lets a manually typed address (or one found by any
/// other means) be confirmed as a real Jularr server before the TV offers to connect to it or
/// start pairing.
/// </summary>
public static class DiscoveryEndpoints
{
    public const string Route = "/.well-known/jularr";

    public static IEndpointRouteBuilder MapDiscoveryWellKnown(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Route, (IServer server) =>
                Results.Ok(DiscoveryReplyFactory.Create(server)))
            .AllowAnonymous();

        return endpoints;
    }
}
