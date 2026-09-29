using System.Text;
using System.Text.Json;

namespace Jularr.Web.Features.Pairing;

/// <summary>
/// Wire format for LAN server discovery (#489): a TV with no configured server address
/// broadcasts <see cref="ProbeMessage"/> as a UDP datagram to <see cref="Port"/>, and every
/// Jularr server on the LAN answers unicast with a small JSON <see cref="DiscoveryReply"/>
/// (<see cref="DiscoveryBeaconService"/>). The same reply shape is served over HTTP at
/// "/.well-known/jularr" (<see cref="DiscoveryEndpoints"/>) so a manually typed address can be
/// verified before pairing starts.
///
/// This is intentionally not real mDNS/DNS-SD (the `_jularr._tcp.local` service the issue
/// sketched): that needs either a new server-side dependency or a reverse-proxy-level responder
/// (for example Avahi in the container image), which is deployment-support work tracked
/// separately (see docs/ANDROID_CLIENTS.md, "Discovery"). This dependency-free protocol still
/// gives zero-typing discovery on a plain LAN today.
/// </summary>
public static class DiscoveryProtocol
{
    public const int Port = 37812;
    public const string ProbeMessage = "JULARR_DISCOVER_V1";

    private static readonly byte[] ProbeMessageBytes = Encoding.ASCII.GetBytes(ProbeMessage);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool IsProbe(ReadOnlySpan<byte> datagram) =>
        datagram.SequenceEqual(ProbeMessageBytes);

    public static byte[] BuildReply(DiscoveryReply reply) =>
        JsonSerializer.SerializeToUtf8Bytes(reply, JsonOptions);

    /// <summary>Used by clients (and tests) to read a beacon reply datagram back.</summary>
    public static DiscoveryReply? ParseReply(ReadOnlySpan<byte> datagram)
    {
        try
        {
            return JsonSerializer.Deserialize<DiscoveryReply>(datagram, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Minimal, unauthenticated self-identification: never anything that would help an attacker
/// beyond "a Jularr server answers on this address" (no account/library data).
/// </summary>
public sealed record DiscoveryReply(
    string Service,
    string Name,
    string Version,
    int Port,
    bool Https);
