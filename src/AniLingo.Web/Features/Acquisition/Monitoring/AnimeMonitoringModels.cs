using AniLingo.Web.Features.Acquisition.Quality;

namespace AniLingo.Web.Features.Acquisition.Monitoring;

public enum AnimeWantedReason
{
    Missing,
    CutoffUnmet
}

public enum AnimeSearchTrigger
{
    SearchOnAdd,
    PeriodicMissing,
    Rss,
    Manual
}

public enum AnimeAcquisitionAttemptStatus
{
    None,
    Pending,
    Grabbed,
    Failed
}

public sealed record AnimeEpisodeKey(
    string AnimeKey,
    int SeasonNumber,
    int EpisodeNumber,
    int? AbsoluteEpisodeNumber = null)
{
    public override string ToString() => $"{AnimeKey}:S{SeasonNumber:00}E{EpisodeNumber:00}";
}

// IndexerIds restricts automatic and interactive Prowlarr searches for this anime to the given
// Prowlarr indexer ids; null or empty uses the global Prowlarr indexer selection. TagIds are the
// anime's assigned acquisition tags (AcquisitionPolicyStore is the tag catalog); delay profiles and
// indexer restrictions can target them. TargetRootId is the library root new imports go to when the
// anime has no folder yet; null defaults to the anime's current root (or the first enabled root).
// Both TagIds and TargetRootId are anime-keyed data that already rides along whenever
// AnimeMonitoringEngine.RekeyAnime moves this record to a new anime key after a series-folder rename.
public sealed record AnimeMonitorSettings(
    string AnimeKey,
    bool Monitored,
    bool SearchOnAdd,
    Dictionary<int, bool> SeasonOverrides,
    Dictionary<string, bool> EpisodeOverrides,
    int[]? IndexerIds = null,
    string[]? TagIds = null,
    Guid? TargetRootId = null);

// The one scheduler setting for periodic monitoring runs.
public sealed record AnimeMonitoringSchedule(
    bool Enabled,
    int IntervalMinutes)
{
    public const int MinimumIntervalMinutes = 5;
    public const int MaximumIntervalMinutes = 24 * 60;

    public static AnimeMonitoringSchedule Default { get; } = new(true, 30);

    public TimeSpan Interval =>
        TimeSpan.FromMinutes(Math.Clamp(IntervalMinutes, MinimumIntervalMinutes, MaximumIntervalMinutes));
}

public sealed record AnimeEpisodeInventory(
    AnimeEpisodeKey Key,
    DateTimeOffset? AirsAtUtc,
    bool HasFile,
    AnimeReleaseScoreResult? CurrentFile);

public sealed record AnimeWantedEpisode(
    AnimeEpisodeKey Key,
    AnimeWantedReason Reason,
    DateTimeOffset BecameWantedAtUtc);

public sealed record AnimeSearchRequest(
    AnimeEpisodeKey Key,
    AnimeWantedReason Reason,
    AnimeSearchTrigger Trigger);

// DelayedUntilUtc is set only when a delay profile (P1 item 4) is holding back an otherwise
// accepted candidate; it is null for every ordinary accept/reject decision.
public sealed record AnimeAutoGrabDecision(
    bool Grab,
    string Reason,
    AnimeReleaseScoreResult Candidate,
    DateTimeOffset? DelayedUntilUtc = null);

public sealed record AnimeAcquisitionAttempt(
    AnimeEpisodeKey Key,
    AnimeAcquisitionAttemptStatus Status,
    string? ReleaseKey,
    int FailureCount,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? NextRetryAtUtc);

public sealed record AnimeMonitoringHistoryEntry(
    DateTimeOffset AtUtc,
    AnimeEpisodeKey Key,
    string Event,
    string Reason);

public sealed record AnimeMonitoringState(
    int Version,
    Dictionary<string, AnimeMonitorSettings> Anime,
    Dictionary<string, AnimeWantedEpisode> Wanted,
    Dictionary<string, AnimeAcquisitionAttempt> Attempts,
    List<AnimeMonitoringHistoryEntry> History)
{
    public AnimeMonitoringSchedule Schedule { get; init; } = AnimeMonitoringSchedule.Default;

    public static AnimeMonitoringState Empty() =>
        new(
            1,
            new Dictionary<string, AnimeMonitorSettings>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, AnimeWantedEpisode>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, AnimeAcquisitionAttempt>(StringComparer.OrdinalIgnoreCase),
            []);
}
