using AniLingo.Web.Features.Acquisition.Indexers;

namespace AniLingo.Web.Features.Acquisition.Prowlarr;

/// <summary>
/// Prowlarr-shaped façade over the one canonical query builder,
/// <see cref="IndexerSearchPlanner"/>. Kept so existing Prowlarr call sites
/// and tests do not need to change; behavior is not duplicated.
/// </summary>
public static class ProwlarrSearchPlanner
{
    public static IReadOnlyList<ProwlarrSearchQuery> Build(
        ProwlarrAnimeSearchTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var genericTarget = new IndexerAnimeSearchTarget(
            target.CanonicalTitle,
            target.Aliases,
            (IndexerAnimeSearchMode)target.Mode,
            target.SeasonNumber,
            target.EpisodeNumber,
            target.AbsoluteEpisodeNumber);

        return IndexerSearchPlanner.Build(genericTarget)
            .Select(query => new ProwlarrSearchQuery(query.Query))
            .ToArray();
    }
}
