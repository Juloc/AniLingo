using System.Text.Json.Serialization;

namespace AniLingo.Web.Features.Acquisition.Sabnzbd;

public sealed record SabnzbdSettings(
    string BaseUrl,
    string Category,
    int QueuePageSize,
    int HistoryPageSize)
{
    public static SabnzbdSettings CreateDefault(string baseUrl) =>
        new(
            baseUrl,
            Category: "anilingo",
            QueuePageSize: 200,
            HistoryPageSize: 200);
}

public sealed record SabnzbdConnection(
    SabnzbdSettings Settings,
    [property: JsonIgnore] string ApiKey);

public sealed record SabnzbdConnectionTestResult(
    bool Success,
    string? Version = null,
    string? Error = null);

public sealed record SabnzbdGrabRequest(
    Uri NzbUrl,
    string? NzbName = null,
    string? Category = null,
    int? Priority = null);

public sealed record SabnzbdGrabResult(
    bool Success,
    IReadOnlyList<string> NzoIds,
    string? Error = null);

public enum SabnzbdFailureKind
{
    None,
    Download,
    Unpack,
    Verification,
    Password,
    Script,
    Unknown
}

public sealed record SabnzbdQueueJob(
    string NzoId,
    string Name,
    string? Status,
    string? Category,
    double? Percentage,
    string? TimeLeft,
    long? SizeBytes,
    long? SizeLeftBytes);

public sealed record SabnzbdHistoryJob(
    string NzoId,
    string Name,
    string? Status,
    string? Category,
    string? StoragePath,
    string? FailureMessage,
    SabnzbdFailureKind FailureKind,
    DateTimeOffset? CompletedAt);

public sealed record SabnzbdQueueSnapshot(
    bool Paused,
    string? Speed,
    string? TimeLeft,
    IReadOnlyList<SabnzbdQueueJob> Jobs);

public sealed record SabnzbdHistorySnapshot(
    IReadOnlyList<SabnzbdHistoryJob> Jobs);

public sealed record SabnzbdActionResult(
    bool Success,
    string? NewNzoId = null,
    string? Error = null);

public sealed class SabnzbdException(string message, Exception? innerException = null)
    : Exception(message, innerException);
