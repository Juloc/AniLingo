using AniLingo.Web.Features.Acquisition.Ownership;

namespace AniLingo.Web.Features.Acquisition.Ownership.Sonarr;

public sealed record SonarrSettings(string BaseUrl);

public sealed record SonarrConnection(SonarrSettings Settings, string ApiKey);

public sealed record SonarrConnectionTestResult(
    bool Success,
    string? Version = null,
    string? Error = null);

public sealed record SonarrQueueItem(
    long Id,
    string Title,
    string? DownloadId,
    string? OutputPath,
    string? Status,
    string? TrackedDownloadStatus,
    int? SeriesId,
    int? EpisodeId,
    string ReleaseKey);

public sealed record SonarrQueueSnapshot(
    IReadOnlyList<SonarrQueueItem> Items,
    SonarrObservedState ObservedState);
