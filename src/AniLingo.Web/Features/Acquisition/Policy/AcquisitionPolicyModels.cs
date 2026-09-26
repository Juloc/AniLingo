namespace AniLingo.Web.Features.Acquisition.Policy;

/// <summary>A simple owner-defined acquisition tag. Anime carry tag ids in AnimeMonitorSettings.TagIds.</summary>
public sealed record AcquisitionTag(string Id, string Name);

/// <summary>
/// Delays grabbing a release for N minutes after an episode becomes wanted, so a slower but
/// preferred release has a chance to appear before AniLingo settles for a lesser one. Scoped by
/// quality profile id and/or tag ids; the most specific matching profile wins (see
/// <see cref="AcquisitionDelayEngine.SelectProfile"/>). A profile with no scope at all is the
/// fallback default when IsDefault is set.
/// </summary>
public sealed record AnimeDelayProfile(
    string Id,
    string Name,
    int DelayMinutes,
    string? QualityProfileId,
    string[] TagIds,
    bool IsDefault);

/// <summary>
/// Restricts automatic and interactive searches for anime carrying any of TagIds to the given
/// Prowlarr indexer ids. Multiple restrictions may apply to the same anime; the allowed set is
/// their intersection (see <see cref="AcquisitionDelayEngine.RestrictedIndexerIds"/>).
/// </summary>
public sealed record AnimeIndexerRestriction(
    string Id,
    string Name,
    string[] TagIds,
    int[] AllowedIndexerIds);

/// <summary>
/// The one canonical store for tags, delay profiles and indexer restrictions. None of this is
/// anime-keyed (anime carry only tag ids in AnimeMonitorSettings, which already rekeys with the
/// anime on a series-folder rename), so this store needs no rekey hook of its own.
/// </summary>
public sealed record AcquisitionPolicyState(
    int Version,
    List<AcquisitionTag> Tags,
    List<AnimeDelayProfile> DelayProfiles,
    List<AnimeIndexerRestriction> IndexerRestrictions)
{
    public static AcquisitionPolicyState Empty() => new(1, [], [], []);
}
