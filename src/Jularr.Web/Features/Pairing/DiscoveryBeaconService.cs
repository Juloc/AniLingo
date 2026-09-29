using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting.Server;

namespace Jularr.Web.Features.Pairing;

/// <summary>
/// Best-effort LAN discovery responder for #489 (wire format in <see cref="DiscoveryProtocol"/>).
/// Listens for UDP broadcast probes and answers unicast with <see cref="DiscoveryReplyFactory"/>'s
/// payload. Any failure to bind the socket (restricted container network, firewall, port already
/// taken, no broadcast-capable interface) just disables the beacon instead of crashing server
/// startup: a TV that never sees a reply still works through "/.well-known/jularr" plus manual
/// setup (<see cref="DiscoveryEndpoints"/>, docs/ANDROID_CLIENTS.md "Discovery").
/// </summary>
public sealed class DiscoveryBeaconService(
    IServer server,
    ILogger<DiscoveryBeaconService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        UdpClient client;
        try
        {
            client = new UdpClient(AddressFamily.InterNetwork);
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryProtocol.Port));
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            logger.LogWarning(
                exception,
                "Jularr LAN discovery beacon could not bind UDP port {Port}; TV clients fall back to manual setup.",
                DiscoveryProtocol.Port);
            return;
        }

        using (client)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                UdpReceiveResult received;
                try
                {
                    received = await client.ReceiveAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException exception)
                {
                    logger.LogDebug(
                        exception,
                        "Jularr LAN discovery beacon receive failed; continuing.");
                    continue;
                }

                if (!DiscoveryProtocol.IsProbe(received.Buffer))
                {
                    continue;
                }

                try
                {
                    var reply = DiscoveryProtocol.BuildReply(DiscoveryReplyFactory.Create(server));
                    await client.SendAsync(reply, received.RemoteEndPoint, stoppingToken);
                }
                catch (SocketException exception)
                {
                    logger.LogDebug(
                        exception,
                        "Jularr LAN discovery beacon reply to {RemoteEndPoint} failed; continuing.",
                        received.RemoteEndPoint);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
