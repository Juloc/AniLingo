using System.Text;
using Jularr.Web.Features.Pairing;

namespace Jularr.Tests;

/// <summary>
/// Covers the pure wire-format helpers behind the #489 LAN discovery beacon
/// (<see cref="DiscoveryBeaconService"/>); the UDP socket loop itself needs a real network stack
/// and is exercised manually/by the pending device verification noted in the PR.
/// </summary>
[TestClass]
public sealed class DiscoveryProtocolTests
{
    [TestMethod]
    public void RecognizesOnlyTheExactProbeMessage()
    {
        Assert.IsTrue(DiscoveryProtocol.IsProbe(Encoding.ASCII.GetBytes(DiscoveryProtocol.ProbeMessage)));
        Assert.IsFalse(DiscoveryProtocol.IsProbe(Encoding.ASCII.GetBytes("something else")));
        Assert.IsFalse(DiscoveryProtocol.IsProbe([]));
    }

    [TestMethod]
    public void ReplyRoundTripsThroughJson()
    {
        var reply = new DiscoveryReply("jularr", "Jularr", "0.1.0-alpha.53", 8080, false);

        var parsed = DiscoveryProtocol.ParseReply(DiscoveryProtocol.BuildReply(reply));

        Assert.AreEqual(reply, parsed);
    }

    [TestMethod]
    public void ParseRejectsGarbageWithoutThrowing()
    {
        var parsed = DiscoveryProtocol.ParseReply(Encoding.UTF8.GetBytes("not json"));

        Assert.IsNull(parsed);
    }
}
