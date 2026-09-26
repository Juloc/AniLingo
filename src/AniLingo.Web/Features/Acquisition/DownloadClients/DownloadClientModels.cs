using System.Text.Json.Serialization;

namespace AniLingo.Web.Features.Acquisition.DownloadClients;

/// <summary>
/// Kind of download client connection. Jularr is usenet-only: torrent
/// clients (qBittorrent) are intentionally unsupported.
/// </summary>
public enum DownloadClientType
{
    Sabnzbd
}

/// <summary>
/// Type-specific settings. SABnzbd keeps separate Books/Anime categories
/// because it serves both features.
/// </summary>
public sealed record DownloadClientSettings(
    string BaseUrl,
    string? BooksCategory,
    string? AnimeCategory)
{
    public static DownloadClientSettings CreateDefault(string baseUrl) =>
        new(baseUrl, null, null);
}

/// <summary>
/// One entry of the canonical download client list. <see cref="Secret"/> is
/// the SABnzbd API key, protected at rest. <see cref="Priority"/> is
/// lower-is-first, and lets several SABnzbd connections fail over to each
/// other on submission failure.
/// </summary>
public sealed record DownloadClientEntry(
    Guid Id,
    string Name,
    DownloadClientType Type,
    bool Enabled,
    int Priority,
    DownloadClientSettings Settings,
    [property: JsonIgnore] string? Secret)
{
    public string? CategoryFor(bool isBooks) =>
        isBooks ? Settings.BooksCategory : Settings.AnimeCategory;
}

public sealed record DownloadClientTestResult(
    bool Success,
    string? Version = null,
    string? Error = null);

/// <summary>
/// One release submission. Exactly one of <see cref="Url"/>/<see cref="File"/> is set.
/// </summary>
public sealed record DownloadClientSubmitRequest(
    Uri? Url,
    string? Name,
    bool IsBooks = false,
    Stream? File = null,
    string? FileName = null);

public sealed record DownloadClientSubmitResult(
    bool Success,
    string? ExternalId,
    string? Error = null);

public enum DownloadClientJobState
{
    Queued,
    Downloading,
    PostProcessing,
    Completed,
    Failed,
    Unknown
}

public sealed record DownloadClientJobStatus(
    string ExternalId,
    string Name,
    DownloadClientJobState State,
    double? Percentage,
    TimeSpan? TimeLeft,
    long? SizeBytes,
    long? SizeLeftBytes,
    double? BytesPerSecond,
    string? StoragePath,
    string? FailureMessage)
{
    public bool IsCompleted => State == DownloadClientJobState.Completed;
    public bool IsFailed => State == DownloadClientJobState.Failed;
}

/// <summary>
/// One download client implementation. SABnzbd is the only implementation;
/// the pipeline and Books submission still depend only on this interface,
/// never on <c>SabnzbdDownloadClient</c> directly.
/// </summary>
public interface IDownloadClient
{
    /// <summary>Stable id stored as an Operation's ExternalProvider for jobs sent to this client.</summary>
    string ProviderId { get; }

    Task<DownloadClientTestResult> TestAsync(
        DownloadClientEntry entry,
        CancellationToken cancellationToken);

    Task<DownloadClientSubmitResult> SubmitAsync(
        DownloadClientEntry entry,
        DownloadClientSubmitRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DownloadClientJobStatus>> GetStatusAsync(
        DownloadClientEntry entry,
        IReadOnlyCollection<string> externalIds,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        DownloadClientEntry entry,
        string externalId,
        bool deleteFiles,
        CancellationToken cancellationToken);
}

public sealed class DownloadClientException(string message, Exception? innerException = null)
    : Exception(message, innerException);
