using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;

namespace Jularr.Web.Features.Admin;

/// <summary>The 1, 5 and 15 minute load average of the host.</summary>
public sealed record HostLoadAverage(double One, double Five, double Fifteen);

/// <summary>
/// One reading of the machine Jularr runs on. Every value that the platform cannot report is null and
/// is left out of the dashboard rather than guessed: host CPU, host memory use and the load average
/// come from <c>/proc</c> on Linux; the process values and the network counters work everywhere.
/// </summary>
public sealed record HostTelemetrySample(
    DateTimeOffset AtUtc,
    double? HostCpuPercent,
    double? ProcessCpuPercent,
    long ProcessWorkingSetBytes,
    long? HostMemoryUsedBytes,
    long? HostMemoryTotalBytes,
    double? ReceiveBytesPerSecond,
    double? SendBytesPerSecond,
    HostLoadAverage? Load);

/// <summary>The most recent samples, oldest first.</summary>
public sealed record HostTelemetrySnapshot(IReadOnlyList<HostTelemetrySample> History)
{
    public static readonly HostTelemetrySnapshot Empty = new([]);

    public HostTelemetrySample? Current => History.Count == 0 ? null : History[^1];
}

/// <summary>Read side of the host telemetry, so the dashboard does not depend on the sampler itself.</summary>
public interface IHostTelemetry
{
    HostTelemetrySnapshot GetSnapshot();
}

/// <summary>Pure parsers for the Linux <c>/proc</c> files the sampler reads.</summary>
public static class HostMetricsParser
{
    /// <summary>The busy and total CPU ticks from the first ("cpu") line of <c>/proc/stat</c>.</summary>
    public static (long Busy, long Total)? ParseCpuTicks(string? procStat)
    {
        if (string.IsNullOrWhiteSpace(procStat))
        {
            return null;
        }

        var line = procStat.Split('\n', 2)[0];
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // user nice system idle iowait irq softirq steal; guest time is already part of user time.
        if (parts.Length < 5 || parts[0] != "cpu")
        {
            return null;
        }

        long total = 0;
        long idle = 0;
        for (var index = 1; index < parts.Length && index <= 8; index++)
        {
            if (!long.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
            {
                return null;
            }

            total += ticks;
            if (index is 4 or 5)
            {
                idle += ticks;
            }
        }

        return (total - idle, total);
    }

    /// <summary>The share of busy ticks between two readings, or null when no time has passed.</summary>
    public static double? CpuPercent((long Busy, long Total) previous, (long Busy, long Total) current)
    {
        var total = current.Total - previous.Total;
        var busy = current.Busy - previous.Busy;
        return total <= 0 || busy < 0 ? null : Math.Clamp(busy * 100d / total, 0, 100);
    }

    /// <summary>Total and available memory in bytes from <c>/proc/meminfo</c> (which reports kibibytes).</summary>
    public static (long TotalBytes, long AvailableBytes)? ParseMemInfo(string? memInfo)
    {
        if (string.IsNullOrWhiteSpace(memInfo))
        {
            return null;
        }

        long? total = null;
        long? available = null;
        foreach (var line in memInfo.Split('\n'))
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
            {
                total = ParseKibibytes(line);
            }
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
            {
                available = ParseKibibytes(line);
            }
        }

        return total is > 0 && available is >= 0 && available <= total
            ? (total.Value, available.Value)
            : null;
    }

    /// <summary>The three load averages from <c>/proc/loadavg</c>.</summary>
    public static HostLoadAverage? ParseLoadAverage(string? loadAvg)
    {
        if (string.IsNullOrWhiteSpace(loadAvg))
        {
            return null;
        }

        var parts = loadAvg.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var one)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var five)
            || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var fifteen))
        {
            return null;
        }

        return new HostLoadAverage(one, five, fifteen);
    }

    /// <summary>Bytes per second between two counter readings; null when a counter restarted or no time passed.</summary>
    public static double? Rate(long previous, long current, TimeSpan elapsed) =>
        elapsed > TimeSpan.Zero && current >= previous ? (current - previous) / elapsed.TotalSeconds : null;

    private static long? ParseKibibytes(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var kibibytes)
            ? kibibytes * 1024
            : null;
    }
}

/// <summary>
/// Samples the machine every few seconds into a small in-memory ring buffer, so the Admin dashboard
/// can show CPU, memory, network and load together with a short history. Nothing is persisted.
/// </summary>
public sealed class HostTelemetrySampler(TimeProvider clock, ILogger<HostTelemetrySampler> logger)
    : BackgroundService, IHostTelemetry
{
    /// <summary>Five minutes of history at <see cref="Interval"/>.</summary>
    public const int Capacity = 60;

    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly Lock gate = new();
    private readonly Queue<HostTelemetrySample> samples = new(Capacity);

    private DateTimeOffset? previousAt;
    private TimeSpan previousProcessCpu;
    private (long Busy, long Total)? previousHostTicks;
    private (long Received, long Sent)? previousNetwork;

    public HostTelemetrySnapshot GetSnapshot()
    {
        lock (gate)
        {
            return new HostTelemetrySnapshot(samples.ToArray());
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                try
                {
                    Record(Sample());
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                    or InvalidOperationException or NetworkInformationException or PlatformNotSupportedException
                    or FormatException)
                {
                    logger.LogWarning(exception, "Host telemetry could not be sampled.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Takes one reading now; rates are relative to the previous reading.</summary>
    public HostTelemetrySample Sample()
    {
        var now = clock.GetUtcNow();
        using var process = Process.GetCurrentProcess();
        var processCpu = process.TotalProcessorTime;
        var workingSet = process.WorkingSet64;
        var hostTicks = OperatingSystem.IsLinux() ? HostMetricsParser.ParseCpuTicks(ReadProc("/proc/stat")) : null;
        var network = ReadNetworkTotals();
        var (memoryUsed, memoryTotal) = ReadMemory();
        var load = OperatingSystem.IsLinux() ? HostMetricsParser.ParseLoadAverage(ReadProc("/proc/loadavg")) : null;

        double? hostCpu = null;
        double? processPercent = null;
        double? receive = null;
        double? send = null;
        if (previousAt is { } before)
        {
            var elapsed = now - before;
            if (elapsed > TimeSpan.Zero)
            {
                processPercent = Math.Clamp(
                    (processCpu - previousProcessCpu).TotalMilliseconds
                        / (elapsed.TotalMilliseconds * Math.Max(Environment.ProcessorCount, 1)) * 100,
                    0,
                    100);
                if (hostTicks is { } currentTicks && previousHostTicks is { } lastTicks)
                {
                    hostCpu = HostMetricsParser.CpuPercent(lastTicks, currentTicks);
                }

                if (network is { } currentNetwork && previousNetwork is { } lastNetwork)
                {
                    receive = HostMetricsParser.Rate(lastNetwork.Received, currentNetwork.Received, elapsed);
                    send = HostMetricsParser.Rate(lastNetwork.Sent, currentNetwork.Sent, elapsed);
                }
            }
        }

        previousAt = now;
        previousProcessCpu = processCpu;
        previousHostTicks = hostTicks;
        previousNetwork = network;

        return new HostTelemetrySample(
            now,
            hostCpu,
            processPercent,
            workingSet,
            memoryUsed,
            memoryTotal,
            receive,
            send,
            load);
    }

    private void Record(HostTelemetrySample sample)
    {
        lock (gate)
        {
            while (samples.Count >= Capacity)
            {
                samples.Dequeue();
            }

            samples.Enqueue(sample);
        }
    }

    private static string? ReadProc(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static (long? Used, long? Total) ReadMemory()
    {
        if (OperatingSystem.IsLinux() && HostMetricsParser.ParseMemInfo(ReadProc("/proc/meminfo")) is { } memory)
        {
            return (memory.TotalBytes - memory.AvailableBytes, memory.TotalBytes);
        }

        // Elsewhere only the total is known cheaply; without a used figure the dashboard shows Jularr's own memory.
        var total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return (null, total > 0 ? total : null);
    }

    // Cumulative bytes over the physical interfaces; container bridges are skipped so their traffic is not counted twice.
    private static (long Received, long Sent)? ReadNetworkTotals()
    {
        long received = 0;
        long sent = 0;
        var any = false;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel
                || nic.Name.StartsWith("docker", StringComparison.OrdinalIgnoreCase)
                || nic.Name.StartsWith("veth", StringComparison.OrdinalIgnoreCase)
                || nic.Name.StartsWith("br-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var statistics = nic.GetIPStatistics();
            received += statistics.BytesReceived;
            sent += statistics.BytesSent;
            any = true;
        }

        return any ? (received, sent) : null;
    }
}
