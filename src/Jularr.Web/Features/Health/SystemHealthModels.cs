namespace Jularr.Web.Features.Health;

/// <summary>
/// Three-state status for one item on Admin &gt; Health (#528, part of epic #510): a clear
/// OK/warning/error, never a raw log. Each row on the page pairs a state with the page that
/// resolves it.
/// </summary>
public enum HealthState
{
    Ok,
    Warning,
    Error
}

public sealed record DatabaseHealth(
    HealthState State,
    bool Reachable,
    long? FileSizeBytes,
    string? Error);

public sealed record DataVolumeHealth(
    HealthState State,
    long? FreeBytes,
    long? TotalBytes);

public sealed record StorageRootHealthRow(
    Guid RootId,
    string Name,
    string Path,
    bool Online,
    bool Writable)
{
    public HealthState State => !Online ? HealthState.Error : Writable ? HealthState.Ok : HealthState.Warning;
}

public sealed record ExternalToolStatus(
    string Name,
    bool IsAvailable,
    string? Version,
    string? Error)
{
    public HealthState State => IsAvailable ? HealthState.Ok : HealthState.Error;
}

public sealed record JobQueueHealth(
    int Running,
    int Queued,
    int Blocked)
{
    public HealthState State => Blocked > 0 ? HealthState.Warning : HealthState.Ok;
}

public sealed record AcquisitionDependencySummary(
    int HealthyIndexers,
    int TotalIndexers,
    int HealthyDownloadClients,
    int TotalDownloadClients)
{
    public bool IsConfigured => TotalIndexers > 0 || TotalDownloadClients > 0;

    public HealthState State =>
        !IsConfigured
            ? HealthState.Warning
            : HealthyIndexers == TotalIndexers && HealthyDownloadClients == TotalDownloadClients
                ? HealthState.Ok
                : HealthState.Error;
}

/// <summary>Everything Admin &gt; Health shows except AI reachability (read directly from
/// <c>CodexCliProvider</c> by the page, per #528: "reuse existing AI status, don't re-implement").</summary>
public sealed record SystemHealthSnapshot(
    DatabaseHealth Database,
    DataVolumeHealth DataVolume,
    IReadOnlyList<StorageRootHealthRow> StorageRoots,
    ExternalToolStatus Ffmpeg,
    ExternalToolStatus Ffprobe,
    JobQueueHealth JobQueue,
    AcquisitionDependencySummary Acquisition);
