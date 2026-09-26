namespace AniLingo.Web.Features.Operations;

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
    string? Details = null)
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
    string? Details = null)
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
