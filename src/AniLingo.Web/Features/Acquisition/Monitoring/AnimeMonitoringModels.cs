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

public sealed record AnimeMonitorSettings(
    string AnimeKey,
    bool Monitored,
    bool SearchOnAdd,
    Dictionary<int, bool> SeasonOverrides,
    Dictionary<string, bool> EpisodeOverrides);

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

public sealed record AnimeAutoGrabDecision(
    bool Grab,
    string Reason,
    AnimeReleaseScoreResult Candidate);

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
    public static AnimeMonitoringState Empty() =>
        new(
            1,
            new Dictionary<string, AnimeMonitorSettings>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, AnimeWantedEpisode>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, AnimeAcquisitionAttempt>(StringComparer.OrdinalIgnoreCase),
            []);
}
