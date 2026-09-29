using System.Reflection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Jularr.Web.Features.Pairing;

/// <summary>
/// Builds the small "this is a Jularr server" payload shared by the UDP discovery beacon
/// (<see cref="DiscoveryBeaconService"/>) and the "/.well-known/jularr" HTTP endpoint
/// (<see cref="DiscoveryEndpoints"/>), so both report the same name/version/address.
/// </summary>
public static class DiscoveryReplyFactory
{
    public const string ServiceName = "jularr";
    public const string ServerDisplayName = "Jularr";

    /// <summary>Same resolution as <see cref="ClientApi.ClientApiContract.Capabilities"/>.</summary>
    public static string CurrentVersion()
    {
        var assembly = typeof(DiscoveryReplyFactory).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return string.IsNullOrWhiteSpace(informational)
            ? assembly.GetName().Version?.ToString() ?? "unknown"
            : informational.Split('+', 2)[0];
    }

    /// <summary>
    /// The first bound Kestrel address, so a discovery reply carries a real reachable port. Falls
    /// back to the documented container default (plain HTTP on 8080, see Dockerfile) when Kestrel
    /// reports no parseable address yet (for example a wildcard "http://+:8080" bind).
    /// </summary>
    public static (int Port, bool Https) ResolveAddress(IServer server)
    {
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        var first = addresses?
            .Select(address => Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri : null)
            .FirstOrDefault(uri => uri is not null);

        return first is null
            ? (8080, false)
            : (first.Port, string.Equals(first.Scheme, "https", StringComparison.OrdinalIgnoreCase));
    }

    public static DiscoveryReply Create(IServer server)
    {
        var (port, https) = ResolveAddress(server);
        return new DiscoveryReply(ServiceName, ServerDisplayName, CurrentVersion(), port, https);
    }
}
