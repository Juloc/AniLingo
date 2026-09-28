using System.Net;
using System.Net.Sockets;

namespace Jularr.Web.Features.Storage;

public interface IWakeOnLanPacketSender
{
    Task SendAsync(
        string normalizedMacAddress,
        IPEndPoint broadcastEndpoint,
        CancellationToken cancellationToken);
}

// Sends the magic packet to whatever address/port the owner configured (see
// WakeOnLanService.TryResolveBroadcastEndpoint). In a Docker bridge network the limited
// broadcast address 255.255.255.255 never leaves the container's bridge subnet, so reaching a
// NAS on the physical LAN requires a directed subnet broadcast (e.g. 192.168.1.255) that the
// Docker host's own routing/NAT forwards onward; see docs/ADMIN_OPERATIONS.md.
public sealed class UdpWakeOnLanPacketSender : IWakeOnLanPacketSender
{
    public async Task SendAsync(
        string normalizedMacAddress,
        IPEndPoint broadcastEndpoint,
        CancellationToken cancellationToken)
    {
        var packet = WakeOnLanService.BuildMagicPacket(normalizedMacAddress);
        using var udp = new UdpClient(AddressFamily.InterNetwork)
        {
            EnableBroadcast = true
        };

        await udp.SendAsync(
            packet,
            broadcastEndpoint,
            cancellationToken);
    }
}

public sealed class StorageWakeOptions
{
    // How long one start attempt waits for the storage after the packet; a NAS that is
    // powered off typically needs one to two minutes, a disk spin-up seconds.
    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(3);
}

public sealed record StorageWakeTarget(
    Guid RootId,
    string Path,
    string MacAddress,
    IPEndPoint BroadcastEndpoint,
    bool ExpectedNonEmpty);

// One start attempt per root at a time: the first media request sends one Wake-on-LAN packet
// and polls the storage until it is readable or the bounded start timeout ends; concurrent
// requests (several players, an import, the owner's Wake button) join that attempt instead of
// sending more packets. The attempt does not belong to any caller, so a cancelled request
// never cancels the wait of the others. A timed-out or unsendable attempt leaves the root in
// the Error state (StorageDiagnosticCodes) until it is readable again or a new attempt starts.
public sealed class StorageWakeCoordinator(
    StorageAvailabilityCoordinator availability,
    IWakeOnLanPacketSender sender,
    StorageWakeOptions options,
    ILogger<StorageWakeCoordinator> logger)
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, Task<LibraryRootAvailabilitySnapshot>> attempts = new();

    public bool IsStarting(Guid rootId)
    {
        lock (gate)
        {
            return attempts.TryGetValue(rootId, out var attempt) && !attempt.IsCompleted;
        }
    }

    public Task<LibraryRootAvailabilitySnapshot> StartAsync(StorageWakeTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        lock (gate)
        {
            if (attempts.TryGetValue(target.RootId, out var running) && !running.IsCompleted)
            {
                return running;
            }

            // Marked before the attempt runs so every caller reads Starting at once. False means
            // a packet went out moments ago; this attempt then only waits for it.
            var sendPacket = availability.TryMarkWakeStarting(
                target.RootId,
                options.StartTimeout + options.PollInterval);
            var attempt = Task.Run(() => RunAsync(target, sendPacket));
            attempts[target.RootId] = attempt;
            return attempt;
        }
    }

    private async Task<LibraryRootAvailabilitySnapshot> RunAsync(
        StorageWakeTarget target,
        bool sendPacket)
    {
        var current = await ProbeAsync(target);
        if (current.IsAvailable)
        {
            return current;
        }

        if (sendPacket)
        {
            try
            {
                await sender.SendAsync(
                    target.MacAddress,
                    target.BroadcastEndpoint,
                    CancellationToken.None);
                logger.LogInformation(
                    "Wake-on-LAN packet sent for library root {RootId} ({MacAddress}) to {BroadcastEndpoint}; waiting up to {Seconds} s for its storage.",
                    target.RootId,
                    target.MacAddress,
                    target.BroadcastEndpoint,
                    (int)options.StartTimeout.TotalSeconds);
            }
            catch (Exception exception) when (
                exception is SocketException or IOException or InvalidOperationException or ArgumentException)
            {
                logger.LogWarning(
                    exception,
                    "Wake-on-LAN packet could not be sent for library root {RootId} ({MacAddress}) to {BroadcastEndpoint}.",
                    target.RootId,
                    target.MacAddress,
                    target.BroadcastEndpoint);
                availability.MarkWakeFailed(target.RootId, StorageDiagnosticCodes.WakeSendFailed);
                return await ProbeAsync(target);
            }
        }

        var deadline = DateTimeOffset.UtcNow + options.StartTimeout;
        while (true)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(remaining < options.PollInterval ? remaining : options.PollInterval);
            current = await ProbeAsync(target);
            if (current.IsAvailable)
            {
                logger.LogInformation(
                    "Storage of library root {RootId} is online after Wake-on-LAN.",
                    target.RootId);
                return current;
            }
        }

        logger.LogWarning(
            "Storage of library root {RootId} did not come online within {Seconds} s after Wake-on-LAN.",
            target.RootId,
            (int)options.StartTimeout.TotalSeconds);
        availability.MarkWakeFailed(target.RootId, StorageDiagnosticCodes.WakeTimeout);
        return availability.GetCached(target.RootId, wakeConfigured: true) ?? current;
    }

    private Task<LibraryRootAvailabilitySnapshot> ProbeAsync(StorageWakeTarget target) =>
        availability.ProbeAsync(
            target.RootId,
            target.Path,
            wakeConfigured: true,
            force: true,
            CancellationToken.None,
            target.ExpectedNonEmpty);
}
