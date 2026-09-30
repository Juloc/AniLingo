namespace Jularr.Web.Features.Operations;

public enum OperationStatus
{
    Queued = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
    Interrupted = 6
}

public enum OperationLane
{
    Interactive = 1,
    Normal = 2,
    Maintenance = 3
}

/// <summary>
/// How soon queued work should run. The worker of a queue takes the highest priority first and the
/// oldest first among equals; work that is already running is never interrupted by it.
/// </summary>
public enum OperationPriority
{
    Low = 1,
    Normal = 2,
    High = 3
}

public enum OperationLogLevel
{
    Information = 1,
    Warning = 2,
    Error = 3
}

public sealed record OperationDescriptor(
    string Kind,
    string Category,
    string Title,
    string? Subject = null,
    string? ProfileId = null,
    OperationLane Lane = OperationLane.Normal,
    bool IsDownload = false,
    bool Retryable = true,
    long? BytesTotal = null,
    string? ExternalProvider = null,
    string? ExternalId = null,
    string? Details = null,
    string? ActorProfileId = null,
    OperationPriority Priority = OperationPriority.Normal)
{
    public static OperationDescriptor Background(string title = "Background task") =>
        new("background", "Task", title, Retryable: true);

    public static OperationDescriptor Playback(string title = "Playback preparation") =>
        new(
            "playback-preparation",
            "Playback",
            title,
            Lane: OperationLane.Interactive,
            Retryable: true);
}

public sealed record OperationSnapshot(
    Guid Id,
    string Kind,
    string Category,
    OperationLane Lane,
    OperationStatus Status,
    string? ProfileId,
    string Title,
    string? Subject,
    int? ProgressPercent,
    string? Message,
    string? Error,
    bool IsDownload,
    long? BytesTotal,
    long? BytesCompleted,
    double? BytesPerSecond,
    DateTime? EtaUtc,
    int Attempt,
    bool Retryable,
    string? ExternalProvider,
    string? ExternalId,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc,
    DateTime UpdatedAtUtc,
    string? Details = null,
    string? ActorProfileId = null,
    OperationPriority Priority = OperationPriority.Normal)
{
    public bool IsActive =>
        Status is OperationStatus.Queued or OperationStatus.Running;

    public bool CanCancel =>
        IsActive && string.IsNullOrWhiteSpace(ExternalProvider);

    public string StatusLabel => Status switch
    {
        OperationStatus.Queued => "Queued",
        OperationStatus.Running => "Running",
        OperationStatus.Succeeded => "Completed",
        OperationStatus.Failed => "Failed",
        OperationStatus.Cancelled => "Cancelled",
        OperationStatus.Interrupted => "Interrupted",
        _ => Status.ToString()
    };
}

public sealed record OperationLogEntry(
    long Id,
    Guid OperationId,
    DateTime CreatedAtUtc,
    OperationLogLevel Level,
    string Module,
    string Message);

public sealed record OperationSummary(
    int Running,
    int Queued,
    int Failed,
    int Interrupted,
    int ActiveDownloads,
    int CompletedToday);

public sealed record OperationListFilter(
    string? View = null,
    OperationStatus? Status = null,
    string? Category = null,
    string? Search = null,
    int Limit = 100,
    string? Kind = null);

public sealed record OperationLogFilter(
    OperationLogLevel? Level = null,
    Guid? OperationId = null,
    string? Module = null,
    string? Search = null,
    int Limit = 200);

/// <summary>The kind and category an operation was created with; together they decide its history category.</summary>
public sealed record OperationKindKey(string Kind, string Category);

/// <summary>How many finished operations one <see cref="OperationKindKey"/> has under a history filter.</summary>
public sealed record OperationKindCount(OperationKindKey Key, int Count);

/// <summary>
/// Narrows the finished operations (Admin → History). <see cref="FromUtc"/> is inclusive and
/// <see cref="ToUtc"/> exclusive, both against the time the operation finished.
/// </summary>
public sealed record OperationHistoryFilter(
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    IReadOnlyCollection<OperationKindKey>? Kinds = null,
    IReadOnlyCollection<OperationStatus>? Statuses = null,
    string? Search = null,
    IReadOnlyCollection<string>? SearchActorIds = null,
    bool NewestFirst = true,
    int Offset = 0,
    int Limit = 20);

public sealed record OperationHistoryPage(IReadOnlyList<OperationSnapshot> Items, int Total);

/// <summary>Narrows the work queue (Admin → Activity) to some statuses, kinds and a text; a null list means no limit.</summary>
public sealed record OperationActivityFilter(
    IReadOnlyCollection<OperationStatus>? Statuses = null,
    IReadOnlyCollection<OperationKindKey>? Kinds = null,
    string? Search = null,
    int Offset = 0,
    int Limit = 20,
    OperationPriority? Priority = null);

public sealed record OperationActivityPage(IReadOnlyList<OperationSnapshot> Items, int Total);

/// <summary>How many operations one <see cref="OperationKindKey"/> has in one status and at one priority.</summary>
public sealed record OperationActivityCount(
    OperationKindKey Key,
    OperationStatus Status,
    int Count,
    OperationPriority Priority = OperationPriority.Normal);

/// <summary>Names, address values and the run order of <see cref="OperationPriority"/>.</summary>
public static class OperationPriorities
{
    public static string Name(OperationPriority priority) => priority.ToString().ToLowerInvariant();

    /// <summary>Reads a priority from the address or a form; an unknown value means none.</summary>
    public static OperationPriority? TryParse(string? value)
    {
        foreach (var priority in Enum.GetValues<OperationPriority>())
        {
            if (string.Equals(Name(priority), value?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return priority;
            }
        }

        return null;
    }

    /// <summary>The index of the work to run next: the highest priority, the earliest among equals; -1 when nothing waits.</summary>
    public static int PickNext(IReadOnlyList<OperationPriority> waiting)
    {
        ArgumentNullException.ThrowIfNull(waiting);
        var best = -1;
        for (var index = 0; index < waiting.Count; index++)
        {
            if (best < 0 || waiting[index] > waiting[best])
            {
                best = index;
            }
        }

        return best;
    }
}
