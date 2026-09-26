namespace AniLingo.Web.Features.Acquisition.AniListAutoMonitor;

/// <summary>One owner/local-account profile's AniList list auto-monitor rule (P1 item 7).</summary>
public sealed record AniListAutoMonitorProfileSettings(bool Enabled, DateTimeOffset? EnabledAtUtc);

/// <summary>
/// The one canonical store of per-profile AniList auto-monitor settings, at
/// /data/acquisition/anilist-auto-monitor.json. Off by default for every profile: enabling it never
/// happens implicitly.
/// </summary>
public sealed record AniListAutoMonitorState(
    int Version,
    Dictionary<string, AniListAutoMonitorProfileSettings> Profiles)
{
    public static AniListAutoMonitorState Empty() =>
        new(1, new Dictionary<string, AniListAutoMonitorProfileSettings>(StringComparer.Ordinal));

    public bool IsEnabled(string profileId) =>
        Profiles.TryGetValue(profileId, out var settings) && settings.Enabled;
}

/// <summary>What one pass of automatic AniList-list monitoring did for one profile.</summary>
public sealed record AniListAutoMonitorRunResult(
    int ListEntries,
    int LocalMatches,
    int NewlyMonitored,
    IReadOnlyList<string> Notes);
