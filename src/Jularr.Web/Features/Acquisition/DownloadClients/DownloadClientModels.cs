using System.Text.Json;
using System.Text.Json.Serialization;
using Jularr.Web.Features.Acquisition.Access;

namespace Jularr.Web.Features.Acquisition.DownloadClients;

/// <summary>
/// Kind of download client connection. Jularr is usenet-only: torrent
/// clients (qBittorrent) are intentionally unsupported.
/// </summary>
public enum DownloadClientType
{
    Sabnzbd
}

/// <summary>
/// Type-specific settings. SABnzbd keeps one category mapping for every
/// media acquisition kind it can receive.
/// </summary>
public sealed record DownloadClientSettings
{
    public DownloadClientSettings(
        string baseUrl,
        IReadOnlyDictionary<MediaAcquisitionKind, string?>? categories = null)
    {
        BaseUrl = baseUrl;
        Categories = Enum.GetValues<MediaAcquisitionKind>()
            .ToDictionary(
                kind => kind,
                kind => categories is not null && categories.TryGetValue(kind, out var category)
                    ? CleanCategory(category)
                    : null);
    }

    // Temporary source-compatibility bridge for the Books work currently
    // owned by another agent. New code must use the media-kind category map.
    public DownloadClientSettings(
        string baseUrl,
        string? booksCategory,
        string? animeCategory)
        : this(
            baseUrl,
            new Dictionary<MediaAcquisitionKind, string?>
            {
                [MediaAcquisitionKind.Book] = booksCategory,
                [MediaAcquisitionKind.Anime] = animeCategory
            })
    {
    }

    public string BaseUrl { get; init; }

    public IReadOnlyDictionary<MediaAcquisitionKind, string?> Categories { get; init; }

    // Temporary read-only aliases for the existing Books settings view.
    [JsonIgnore]
    public string? BooksCategory => CategoryFor(MediaAcquisitionKind.Book);

    [JsonIgnore]
    public string? AnimeCategory => CategoryFor(MediaAcquisitionKind.Anime);

    [JsonIgnore]
    public string? MangaCategory => CategoryFor(MediaAcquisitionKind.Manga);

    [JsonIgnore]
    public string? LightNovelCategory => CategoryFor(MediaAcquisitionKind.LightNovel);

    public string? CategoryFor(MediaAcquisitionKind kind) =>
        Categories.TryGetValue(kind, out var category) ? CleanCategory(category) : null;

    public static DownloadClientSettings CreateDefault(string baseUrl) =>
        new(
            baseUrl,
            new Dictionary<MediaAcquisitionKind, string?>
            {
                [MediaAcquisitionKind.Anime] = "anime",
                [MediaAcquisitionKind.Manga] = "manga",
                [MediaAcquisitionKind.LightNovel] = "lightnovels",
                [MediaAcquisitionKind.Book] = "books"
            });

    private static string? CleanCategory(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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
    public string? CategoryFor(MediaAcquisitionKind mediaKind) =>
        Settings.CategoryFor(mediaKind);
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
    MediaAcquisitionKind MediaKind,
    Stream? File = null,
    string? FileName = null);

/// <summary>
/// Durable routing metadata for a submitted external download. The monitor
/// uses the exact selected connection instead of whichever client currently
/// has the highest priority.
/// </summary>
public sealed record DownloadOperationDetails(
    Guid ClientEntryId,
    MediaAcquisitionKind MediaKind,
    string? Category)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(
        new PersistedDetails(
            ClientEntryId,
            AcquisitionAccessNames.Kind(MediaKind),
            string.IsNullOrWhiteSpace(Category) ? null : Category.Trim()),
        JsonOptions);

    public static bool TryParse(string? json, out DownloadOperationDetails? details)
    {
        details = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var persisted = JsonSerializer.Deserialize<PersistedDetails>(json, JsonOptions);
            if (persisted is null || persisted.ClientEntryId == Guid.Empty || string.IsNullOrWhiteSpace(persisted.MediaKind))
            {
                return false;
            }

            details = new DownloadOperationDetails(
                persisted.ClientEntryId,
                AcquisitionAccessNames.ParseKind(persisted.MediaKind),
                string.IsNullOrWhiteSpace(persisted.Category) ? null : persisted.Category.Trim());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private sealed record PersistedDetails(
        Guid ClientEntryId,
        string MediaKind,
        string? Category);
}

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
