using System.Text.Json.Serialization;

namespace AniLingo.Web.Features.Acquisition.DownloadClients;

public enum DownloadClientType
{
    Sabnzbd,
    QBittorrent
}

public enum DownloadProtocol
{
    Usenet,
    Torrent
}

/// <summary>
/// Type-specific settings. Only the fields relevant to <see cref="DownloadClientEntry.Type"/>
/// are used; the rest stay null. SABnzbd keeps separate Books/Anime categories because it
/// serves both features; qBittorrent (anime only, for now) uses <see cref="AnimeCategory"/>
/// as its one category.
/// </summary>
public sealed record DownloadClientSettings(
    string BaseUrl,
    string? Username,
    string? BooksCategory,
    string? AnimeCategory,
    string? SavePath)
{
    public static DownloadClientSettings CreateDefault(string baseUrl) =>
        new(baseUrl, null, null, null, null);
}

/// <summary>
/// One entry of the canonical download client list. <see cref="Secret"/> is
/// the API key (SABnzbd) or password (qBittorrent), protected at rest.
/// <see cref="Priority"/> is lower-is-first.
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
    public DownloadProtocol Protocol => Type switch
    {
        DownloadClientType.Sabnzbd => DownloadProtocol.Usenet,
        DownloadClientType.QBittorrent => DownloadProtocol.Torrent,
        _ => throw new NotSupportedException($"Unknown download client type '{Type}'.")
    };

    public string? CategoryFor(bool isBooks) =>
        Type == DownloadClientType.Sabnzbd
            ? (isBooks ? Settings.BooksCategory : Settings.AnimeCategory)
            : Settings.AnimeCategory;
}

public sealed record DownloadClientTestResult(
    bool Success,
    string? Version = null,
    string? Error = null);

/// <summary>
/// One release submission. Exactly one of <see cref="Url"/>/<see cref="MagnetUri"/>/<see cref="File"/> is set.
/// </summary>
public sealed record DownloadClientSubmitRequest(
    DownloadProtocol Protocol,
    Uri? Url,
    string? MagnetUri,
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
/// One download client implementation. SABnzbd (usenet) and qBittorrent
/// (torrent) each implement this once; the pipeline and Books submission
/// only depend on this interface, never on a specific client.
/// </summary>
public interface IDownloadClient
{
    DownloadClientType Type { get; }

    DownloadProtocol Protocol { get; }

    /// <summary>Stable id stored as an Operation's ExternalProvider for jobs sent to this client type.</summary>
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
