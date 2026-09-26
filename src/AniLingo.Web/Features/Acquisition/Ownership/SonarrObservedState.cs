namespace AniLingo.Web.Features.Acquisition.Ownership;

public enum SonarrObservationStatus
{
    // A current read-only observation of Sonarr is available.
    Observed,

    // No Sonarr connection is configured, so Sonarr cannot own anything.
    NotConfigured,

    // Sonarr is configured but could not be observed. Decisions that depend on Sonarr fail closed.
    Unavailable
}

public enum SonarrHistoryEventKind
{
    Grabbed,
    Imported,
    Renamed,
    DownloadFailed,
    FileDeleted,
    Other
}

public sealed record SonarrObservedEpisode(
    int SeasonNumber,
    int EpisodeNumber,
    int? AbsoluteEpisodeNumber);

public sealed record SonarrObservedSeries(
    int Id,
    string Title,
    string Path,
    bool Monitored);

public sealed record SonarrObservedEpisodeFile(
    int Id,
    int SeriesId,
    int SeasonNumber,
    string Path);

public sealed record SonarrObservedQueueItem(
    long Id,
    int? SeriesId,
    string Title,
    string ReleaseKey,
    string? DownloadId,
    string? OutputPath,
    string? Status,
    string? TrackedDownloadState,
    SonarrObservedEpisode? Episode);

public sealed record SonarrObservedHistoryEvent(
    long Id,
    int? SeriesId,
    SonarrHistoryEventKind Kind,
    string EventType,
    DateTimeOffset? AtUtc,
    string? SourceTitle,
    string? ReleaseKey,
    string? DownloadId,
    string? SourcePath,
    string? Path,
    SonarrObservedEpisode? Episode);

// Read-only view of what Sonarr currently manages. Observation never transfers ownership:
// it only tells AniLingo which releases, downloads and paths it must leave alone.
public sealed record SonarrObservedState(
    IReadOnlySet<string> ActiveReleaseKeys,
    IReadOnlySet<string> ActivePaths)
{
    private static readonly IReadOnlySet<string> NoValues =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static SonarrObservedState Empty { get; } = new(NoValues, NoValues);

    public static SonarrObservedState NotConfigured { get; } =
        new(NoValues, NoValues) { Status = SonarrObservationStatus.NotConfigured };

    public SonarrObservationStatus Status { get; init; } = SonarrObservationStatus.Observed;

    public string? StatusDetail { get; init; }

    public DateTimeOffset? ObservedAtUtc { get; init; }

    public IReadOnlySet<string> ActiveDownloadIds { get; init; } = NoValues;

    public IReadOnlyList<SonarrObservedSeries> Series { get; init; } = [];

    public IReadOnlyList<SonarrObservedEpisodeFile> EpisodeFiles { get; init; } = [];

    public IReadOnlyList<SonarrObservedQueueItem> Queue { get; init; } = [];

    public IReadOnlyList<SonarrObservedHistoryEvent> History { get; init; } = [];

    public static SonarrObservedState Unavailable(string detail, DateTimeOffset now) =>
        new(NoValues, NoValues)
        {
            Status = SonarrObservationStatus.Unavailable,
            StatusDetail = detail,
            ObservedAtUtc = now
        };

    public static SonarrObservedState FromObservation(
        IReadOnlyList<SonarrObservedSeries> series,
        IReadOnlyList<SonarrObservedEpisodeFile> episodeFiles,
        IReadOnlyList<SonarrObservedQueueItem> queue,
        IReadOnlyList<SonarrObservedHistoryEvent> history,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(episodeFiles);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(history);

        var releases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var downloads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in queue)
        {
            if (!string.IsNullOrWhiteSpace(item.ReleaseKey))
            {
                releases.Add(item.ReleaseKey);
            }

            if (!string.IsNullOrWhiteSpace(item.OutputPath))
            {
                paths.Add(item.OutputPath);
            }

            if (!string.IsNullOrWhiteSpace(item.DownloadId))
            {
                downloads.Add(item.DownloadId);
            }
        }

        return new(releases, paths)
        {
            Status = SonarrObservationStatus.Observed,
            ObservedAtUtc = now,
            ActiveDownloadIds = downloads,
            Series = series,
            EpisodeFiles = episodeFiles,
            Queue = queue,
            History = history
        };
    }
}
