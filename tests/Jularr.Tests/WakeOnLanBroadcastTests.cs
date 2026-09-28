using System.Net;
using Jularr.Web.Features.Storage;

namespace Jularr.Tests;

// #571: Wake-on-LAN magic packets sent to the default limited broadcast (255.255.255.255) never
// leave a Docker bridge network, so the NAS on the physical LAN never receives them. The fix
// resolves the owner's WakeBroadcastAddress setting to an IPEndPoint (address + optional port)
// so a directed LAN broadcast such as 192.168.1.255 - which the Docker host's own routing/NAT
// forwards onto the physical LAN - can be configured without requiring host networking. These
// tests cover only the parsing/resolution logic; no real socket is used (see StorageHealthTests
// for the fake-sender coverage of the coordinator itself).
[TestClass]
public sealed class WakeOnLanBroadcastTests
{
    [TestMethod]
    public void MissingValueDefaultsToLimitedBroadcastOnStandardPort()
    {
        Assert.IsTrue(WakeOnLanService.TryResolveBroadcastEndpoint(null, out var endpoint));
        Assert.AreEqual(IPAddress.Broadcast, endpoint!.Address);
        Assert.AreEqual(WakeOnLanService.DefaultBroadcastPort, endpoint.Port);

        Assert.IsTrue(WakeOnLanService.TryResolveBroadcastEndpoint("   ", out var blank));
        Assert.AreEqual(IPAddress.Broadcast, blank!.Address);
    }

    [TestMethod]
    public void ExplicitDirectedBroadcastReachesThePhysicalLanFromADockerBridge()
    {
        // The scenario from #571: Jularr in the default Docker bridge network, owner configures
        // the LAN's own directed broadcast instead of the (bridge-only) limited broadcast.
        Assert.IsTrue(WakeOnLanService.TryResolveBroadcastEndpoint(
            "192.168.178.255",
            out var endpoint));
        Assert.AreEqual(IPAddress.Parse("192.168.178.255"), endpoint!.Address);
        Assert.AreEqual(WakeOnLanService.DefaultBroadcastPort, endpoint.Port);
    }

    [TestMethod]
    public void ExplicitPortOverridesTheStandardWakeOnLanPort()
    {
        Assert.IsTrue(WakeOnLanService.TryResolveBroadcastEndpoint(
            "192.168.178.255:7",
            out var endpoint));
        Assert.AreEqual(IPAddress.Parse("192.168.178.255"), endpoint!.Address);
        Assert.AreEqual(7, endpoint.Port);
    }

    [TestMethod]
    public void SurroundingWhitespaceIsTrimmed()
    {
        Assert.IsTrue(WakeOnLanService.TryResolveBroadcastEndpoint(
            "  192.168.178.255:7  ",
            out var endpoint));
        Assert.AreEqual(IPAddress.Parse("192.168.178.255"), endpoint!.Address);
        Assert.AreEqual(7, endpoint.Port);
    }

    // A Docker bridge subnet address (e.g. the default docker0/compose bridge range) parses as a
    // syntactically valid endpoint like any other IPv4 address: resolution intentionally does not
    // try to guess which configured address is "the Docker network" versus "the real LAN", since
    // that is ambiguous on multi-NIC/VLAN hosts (see docs/ADMIN_OPERATIONS.md). Never selecting the
    // bridge subnet automatically is achieved by not doing interface auto-detection at all and
    // instead requiring/using this explicit owner-configured value.
    [TestMethod]
    public void DockerBridgeLookingAddressParsesButIsNotPreferredOrRejectedBySyntax()
    {
        Assert.IsTrue(WakeOnLanService.TryResolveBroadcastEndpoint(
            "172.17.0.255:9",
            out var endpoint));
        Assert.AreEqual(IPAddress.Parse("172.17.0.255"), endpoint!.Address);
    }

    [TestMethod]
    public void EachLibraryRootResolvesItsOwnBroadcastEndpointIndependently()
    {
        // Multiple NICs/VLANs on the host can mean different NAS roots need different broadcast
        // targets; resolution must not share or cache state between calls.
        Assert.IsTrue(WakeOnLanService.TryResolveBroadcastEndpoint("192.168.1.255", out var nas1));
        Assert.IsTrue(WakeOnLanService.TryResolveBroadcastEndpoint("192.168.2.255:7", out var nas2));

        Assert.AreEqual(IPAddress.Parse("192.168.1.255"), nas1!.Address);
        Assert.AreEqual(WakeOnLanService.DefaultBroadcastPort, nas1.Port);
        Assert.AreEqual(IPAddress.Parse("192.168.2.255"), nas2!.Address);
        Assert.AreEqual(7, nas2.Port);
    }

    [TestMethod]
    [DataRow("not-an-ip")]
    [DataRow("192.168.1.255:")]
    [DataRow("192.168.1.255:not-a-port")]
    [DataRow("192.168.1.255:0")]
    [DataRow("192.168.1.255:70000")]
    [DataRow("192.168.1.255:-1")]
    [DataRow("::1")]
    [DataRow("2001:db8::1:9")]
    public void InvalidConfigurationIsRejected(string value)
    {
        Assert.IsFalse(WakeOnLanService.TryResolveBroadcastEndpoint(value, out var endpoint));
        Assert.IsNull(endpoint);
    }
}
