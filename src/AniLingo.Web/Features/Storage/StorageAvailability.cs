using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Library;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Storage;

public enum StorageAvailabilityState
{
    Unknown,
    Available,
    Starting,
    Offline,
    Unreachable,
    FileMissing
}

public sealed record LibraryRootAvailabilitySnapshot(
    Guid RootId,
    StorageAvailabilityState State,
    DateTimeOffset CheckedAtUtc,
    DateTimeOffset? LastAvailableAtUtc,
    bool WakeConfigured,
    string? DiagnosticCode = null)
{
    public bool IsAvailable => State == StorageAvailabilityState.Available;
    public bool IsRetryable =>
        State is StorageAvailabilityState.Unknown
            or StorageAvailabilityState.Starting
            or StorageAvailabilityState.Offline;
}

public sealed record MediaAvailabilitySnapshot(
    Guid MediaFileId,
    Guid RootId,
    StorageAvailabilityState State,
    bool Retryable,
    int RetryAfterMs,
    bool WakeConfigured,
    DateTimeOffset CheckedAtUtc)
{
    public bool IsAvailable => State == StorageAvailabilityState.Available;
}

public sealed record WakeOnLanResult(
    bool Accepted,
    bool AlreadyStarting,
    string Message,
    LibraryRootAvailabilitySnapshot? Availability);

public static class PlaybackAvailabilityRetry
{
    public static readonly IReadOnlyList<TimeSpan> Delays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(5)
    ];

    public static TimeSpan MaximumAutomaticRetryWindow => TimeSpan.FromSeconds(60);

    public static TimeSpan DelayForAttempt(int attempt) =>
        attempt < 0
            ? TimeSpan.Zero
            : attempt < Delays.Count
                ? Delays[attempt]
                : Delays[^1];
}

public sealed class StorageAvailabilityCoordinator
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan OnlineCache = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan OfflineCache = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan WakeStartingWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan WakeDebounce = TimeSpan.FromSeconds(8);

    private readonly ConcurrentDictionary<Guid, RootRuntimeState> roots = new();

    public async Task<LibraryRootAvailabilitySnapshot> ProbeAsync(
        Guid rootId,
        string path,
        bool wakeConfigured,
        bool force,
        CancellationToken cancellationToken,
        bool expectedNonEmpty = false)
    {
        var runtime = roots.GetOrAdd(rootId, _ => new RootRuntimeState());
        var now = DateTimeOffset.UtcNow;
        Task<RootProbeResult> probeTask;

        lock (runtime.Gate)
        {
            if (!force &&
                runtime.Last is { } cached &&
                now < runtime.CacheUntilUtc)
            {
                return DecorateStarting(runtime, cached, wakeConfigured, now);
            }

            if (runtime.ProbeTask is null || runtime.ProbeTask.IsCompleted)
            {
                runtime.ProbeTask = Task.Run(() => ProbePath(path, expectedNonEmpty));
            }

            probeTask = runtime.ProbeTask;
        }

        RootProbeResult result;
        try
        {
            result = await probeTask.WaitAsync(ProbeTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            lock (runtime.Gate)
            {
                var state = runtime.StartingUntilUtc is { } startingUntil && startingUntil > now
                    ? StorageAvailabilityState.Starting
                    : StorageAvailabilityState.Offline;

                var timedOut = new LibraryRootAvailabilitySnapshot(
                    rootId,
                    state,
                    now,
                    runtime.LastAvailableAtUtc,
                    wakeConfigured,
                    "probe_timeout");

                runtime.Last = timedOut;
                runtime.CacheUntilUtc = now + OfflineCache;
                return timedOut;
            }
        }

        lock (runtime.Gate)
        {
            now = DateTimeOffset.UtcNow;
            var state = result.State;

            if (state == StorageAvailabilityState.Available)
            {
                runtime.LastAvailableAtUtc = now;
                runtime.StartingUntilUtc = null;
            }
            else if (runtime.StartingUntilUtc is { } startingUntil && startingUntil > now)
            {
                state = StorageAvailabilityState.Starting;
            }

            var snapshot = new LibraryRootAvailabilitySnapshot(
                rootId,
                state,
                now,
                runtime.LastAvailableAtUtc,
                wakeConfigured,
                result.DiagnosticCode);

            runtime.Last = snapshot;
            runtime.CacheUntilUtc = now +
                (state == StorageAvailabilityState.Available ? OnlineCache : OfflineCache);

            return snapshot;
        }
    }

    public bool TryMarkWakeStarting(Guid rootId)
    {
        var runtime = roots.GetOrAdd(rootId, _ => new RootRuntimeState());
        var now = DateTimeOffset.UtcNow;

        lock (runtime.Gate)
        {
            if (runtime.LastWakeAtUtc is { } lastWake &&
                now - lastWake < WakeDebounce)
            {
                return false;
            }

            runtime.LastWakeAtUtc = now;
            runtime.StartingUntilUtc = now + WakeStartingWindow;
            runtime.CacheUntilUtc = DateTimeOffset.MinValue;
            return true;
        }
    }

    public void MarkWakeFailed(Guid rootId)
    {
        if (!roots.TryGetValue(rootId, out var runtime))
        {
            return;
        }

        lock (runtime.Gate)
        {
            runtime.StartingUntilUtc = null;
            runtime.CacheUntilUtc = DateTimeOffset.MinValue;
        }
    }

    public LibraryRootAvailabilitySnapshot? GetCached(Guid rootId, bool wakeConfigured)
    {
        if (!roots.TryGetValue(rootId, out var runtime))
        {
            return null;
        }

        lock (runtime.Gate)
        {
            if (runtime.Last is null)
            {
                return null;
            }

            return DecorateStarting(
                runtime,
                runtime.Last,
                wakeConfigured,
                DateTimeOffset.UtcNow);
        }
    }

    private static LibraryRootAvailabilitySnapshot DecorateStarting(
        RootRuntimeState runtime,
        LibraryRootAvailabilitySnapshot snapshot,
        bool wakeConfigured,
        DateTimeOffset now)
    {
        if (snapshot.State != StorageAvailabilityState.Available &&
            runtime.StartingUntilUtc is { } startingUntil &&
            startingUntil > now)
        {
            return snapshot with
            {
                State = StorageAvailabilityState.Starting,
                WakeConfigured = wakeConfigured
            };
        }

        return snapshot with { WakeConfigured = wakeConfigured };
    }

    private static RootProbeResult ProbePath(
        string path,
        bool expectedNonEmpty)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath))
            {
                return new RootProbeResult(
                    StorageAvailabilityState.Offline,
                    "root_not_found");
            }

            using var enumerator = Directory
                .EnumerateFileSystemEntries(fullPath, "*", SearchOption.TopDirectoryOnly)
                .GetEnumerator();
            var hasEntries = enumerator.MoveNext();

            if (!hasEntries && expectedNonEmpty)
            {
                return new RootProbeResult(
                    StorageAvailabilityState.Offline,
                    "unexpectedly_empty_root");
            }

            return new RootProbeResult(
                StorageAvailabilityState.Available,
                hasEntries ? null : "root_empty");
        }
        catch (UnauthorizedAccessException)
        {
            return new RootProbeResult(
                StorageAvailabilityState.Unreachable,
                "permission_denied");
        }
        catch (IOException)
        {
            return new RootProbeResult(
                StorageAvailabilityState.Offline,
                "io_unavailable");
        }
        catch (Exception) when (
            !System.Diagnostics.Debugger.IsAttached)
        {
            return new RootProbeResult(
                StorageAvailabilityState.Unreachable,
                "probe_failed");
        }
    }

    private sealed record RootProbeResult(
        StorageAvailabilityState State,
        string? DiagnosticCode);

    private sealed class RootRuntimeState
    {
        public object Gate { get; } = new();
        public Task<RootProbeResult>? ProbeTask { get; set; }
        public LibraryRootAvailabilitySnapshot? Last { get; set; }
        public DateTimeOffset CacheUntilUtc { get; set; }
        public DateTimeOffset? LastAvailableAtUtc { get; set; }
        public DateTimeOffset? StartingUntilUtc { get; set; }
        public DateTimeOffset? LastWakeAtUtc { get; set; }
    }
}

public sealed class LibraryRootAvailabilityService(
    AppDbContext db,
    StorageAvailabilityCoordinator coordinator)
{
    public async Task<LibraryRootAvailabilitySnapshot?> CheckAsync(
        Guid rootId,
        bool force,
        CancellationToken cancellationToken)
    {
        var root = await db.LibraryRoots
            .AsNoTracking()
            .Where(x => x.Id == rootId)
            .Select(x => new
            {
                x.Id,
                x.Path,
                x.WakeOnLanEnabled,
                x.WakeMacAddress
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (root is null)
        {
            return null;
        }

        var wakeConfigured =
            root.WakeOnLanEnabled &&
            WakeOnLanService.TryNormalizeMacAddress(
                root.WakeMacAddress,
                out _);

        var expectedNonEmpty = await db.MediaFiles
            .AsNoTracking()
            .AnyAsync(
                x => x.LibraryRootId == root.Id,
                cancellationToken);

        return await coordinator.ProbeAsync(
            root.Id,
            root.Path,
            wakeConfigured,
            force,
            cancellationToken,
            expectedNonEmpty);
    }

    public LibraryRootAvailabilitySnapshot? GetCached(
        LibraryRoot root) =>
        coordinator.GetCached(
            root.Id,
            root.WakeOnLanEnabled &&
            WakeOnLanService.TryNormalizeMacAddress(
                root.WakeMacAddress,
                out _));
}

public sealed class MediaAvailabilityService(
    AppDbContext db,
    LibraryRootAvailabilityService roots)
{
    public async Task<MediaAvailabilitySnapshot?> CheckMediaAsync(
        Guid mediaFileId,
        bool force,
        CancellationToken cancellationToken)
    {
        var media = await db.MediaFiles
            .AsNoTracking()
            .Where(x => x.Id == mediaFileId)
            .Select(x => new
            {
                x.Id,
                x.LibraryRootId,
                x.Path
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (media is null)
        {
            return null;
        }

        var root = await roots.CheckAsync(
            media.LibraryRootId,
            force,
            cancellationToken);

        if (root is null)
        {
            return new MediaAvailabilitySnapshot(
                media.Id,
                media.LibraryRootId,
                StorageAvailabilityState.Unreachable,
                false,
                0,
                false,
                DateTimeOffset.UtcNow);
        }

        if (!root.IsAvailable)
        {
            return new MediaAvailabilitySnapshot(
                media.Id,
                media.LibraryRootId,
                root.State,
                root.IsRetryable,
                root.State == StorageAvailabilityState.Starting ? 1000 : 2000,
                root.WakeConfigured,
                root.CheckedAtUtc);
        }

        var exists = false;
        try
        {
            exists = File.Exists(media.Path);
        }
        catch
        {
        }

        return new MediaAvailabilitySnapshot(
            media.Id,
            media.LibraryRootId,
            exists
                ? StorageAvailabilityState.Available
                : StorageAvailabilityState.FileMissing,
            false,
            0,
            root.WakeConfigured,
            DateTimeOffset.UtcNow);
    }

    public async Task<MediaAvailabilitySnapshot?> CheckEpisodeAsync(
        Guid episodeId,
        bool force,
        CancellationToken cancellationToken)
    {
        var mediaFileId = await db.MediaFiles
            .AsNoTracking()
            .Where(x => x.EpisodeId == episodeId)
            .OrderBy(x => x.Path)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return mediaFileId is null
            ? null
            : await CheckMediaAsync(
                mediaFileId.Value,
                force,
                cancellationToken);
    }
}

public sealed class WakeOnLanService(
    AppDbContext db,
    StorageAvailabilityCoordinator coordinator,
    LibraryRootAvailabilityService availability,
    ILogger<WakeOnLanService> logger)
{
    public async Task<WakeOnLanResult> WakeAsync(
        Guid rootId,
        CancellationToken cancellationToken)
    {
        var root = await db.LibraryRoots
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == rootId, cancellationToken);

        if (root is null)
        {
            return new WakeOnLanResult(
                false,
                false,
                "Library root was not found.",
                null);
        }

        if (!root.WakeOnLanEnabled ||
            !TryNormalizeMacAddress(root.WakeMacAddress, out var mac))
        {
            return new WakeOnLanResult(
                false,
                false,
                "Wake-on-LAN is not configured for this library root.",
                await availability.CheckAsync(rootId, false, cancellationToken));
        }

        if (!TryResolveBroadcastAddress(
                root.WakeBroadcastAddress,
                out var broadcast))
        {
            return new WakeOnLanResult(
                false,
                false,
                "The configured Wake-on-LAN broadcast address is invalid.",
                await availability.CheckAsync(rootId, false, cancellationToken));
        }

        if (!coordinator.TryMarkWakeStarting(rootId))
        {
            return new WakeOnLanResult(
                true,
                true,
                "Wake-on-LAN was already requested recently.",
                await availability.CheckAsync(rootId, false, cancellationToken));
        }

        var packet = BuildMagicPacket(mac!);

        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork)
            {
                EnableBroadcast = true
            };

            await udp.SendAsync(
                packet,
                packet.Length,
                new IPEndPoint(broadcast!, 9));
        }
        catch (SocketException exception)
        {
            coordinator.MarkWakeFailed(rootId);
            logger.LogWarning(
                exception,
                "Wake-on-LAN packet could not be sent for library root {RootId}.",
                rootId);

            return new WakeOnLanResult(
                false,
                false,
                "Wake-on-LAN could not be sent from the AniLingo container. Check the configured LAN broadcast address and Docker networking.",
                await availability.CheckAsync(rootId, true, cancellationToken));
        }

        logger.LogInformation(
            "Wake-on-LAN packet sent for library root {RootId}.",
            rootId);

        var current = await availability.CheckAsync(
            rootId,
            true,
            cancellationToken);

        return new WakeOnLanResult(
            true,
            false,
            "Wake-on-LAN sent. Waiting for media storage.",
            current);
    }

    public static bool TryNormalizeMacAddress(
        string? value,
        out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var compact = new string(
            value.Where(Uri.IsHexDigit).ToArray());

        if (compact.Length != 12)
        {
            return false;
        }

        try
        {
            var physical = PhysicalAddress.Parse(compact);
            var bytes = physical.GetAddressBytes();
            if (bytes.Length != 6)
            {
                return false;
            }

            normalized = string.Join(
                ":",
                bytes.Select(x => x.ToString("X2")));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool TryResolveBroadcastAddress(
        string? value,
        out IPAddress? address)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            address = IPAddress.Broadcast;
            return true;
        }

        if (IPAddress.TryParse(value.Trim(), out var parsed) &&
            parsed.AddressFamily == AddressFamily.InterNetwork)
        {
            address = parsed;
            return true;
        }

        address = null;
        return false;
    }

    public static byte[] BuildMagicPacket(string normalizedMacAddress)
    {
        if (!TryNormalizeMacAddress(
                normalizedMacAddress,
                out var normalized))
        {
            throw new ArgumentException(
                "A valid 6-byte MAC address is required.",
                nameof(normalizedMacAddress));
        }

        var mac = PhysicalAddress
            .Parse(normalized!.Replace(":", "", StringComparison.Ordinal))
            .GetAddressBytes();

        var packet = new byte[6 + (16 * mac.Length)];
        Array.Fill(packet, (byte)0xFF, 0, 6);

        for (var i = 0; i < 16; i++)
        {
            Buffer.BlockCopy(
                mac,
                0,
                packet,
                6 + (i * mac.Length),
                mac.Length);
        }

        return packet;
    }
}
