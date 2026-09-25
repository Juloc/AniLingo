using AniLingo.Web.Features.Metadata;

namespace AniLingo.Web.Features.MediaMapping;

public sealed record ResolvedAnimeEpisodeNumbering(
    int SeasonNumber,
    int EpisodeNumber,
    int? AbsoluteEpisodeNumber,
    ResolvedAnimeEpisodeMetadata? Remote);

public static class AnimeEpisodeNumberingResolver
{
    public static ResolvedAnimeEpisodeNumbering Resolve(
        int seasonNumber,
        int episodeNumber,
        IReadOnlyList<LocalEpisodeCoordinate> localEpisodes,
        IReadOnlyList<AnimeEpisodeMetadataMapping> mappings)
    {
        if (seasonNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seasonNumber));
        }

        if (episodeNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(episodeNumber));
        }

        ArgumentNullException.ThrowIfNull(localEpisodes);
        ArgumentNullException.ThrowIfNull(mappings);

        var absolute = ResolveAbsoluteEpisode(
            seasonNumber,
            episodeNumber,
            localEpisodes);

        var matching = mappings
            .Where(mapping => mapping.Contains(seasonNumber, episodeNumber))
            .ToArray();

        if (matching.Length > 1)
        {
            throw new InvalidOperationException(
                $"Episode S{seasonNumber:00}E{episodeNumber:00} has overlapping metadata mappings.");
        }

        ResolvedAnimeEpisodeMetadata? remote = null;
        if (matching.Length == 1)
        {
            var mapping = matching[0];
            remote = new ResolvedAnimeEpisodeMetadata(
                mapping.Provider,
                mapping.ExternalId,
                mapping.PreferredTitle,
                mapping.ResolveRemoteEpisode(episodeNumber),
                mapping.EpisodeCount,
                IsExplicitRange: true);
        }

        return new ResolvedAnimeEpisodeNumbering(
            seasonNumber,
            episodeNumber,
            absolute,
            remote);
    }

    public static int? ResolveAbsoluteEpisode(
        int seasonNumber,
        int episodeNumber,
        IReadOnlyList<LocalEpisodeCoordinate> localEpisodes)
    {
        ArgumentNullException.ThrowIfNull(localEpisodes);

        // Season 0 is intentionally excluded. Specials/OVA/ONA use their own
        // provider mappings and do not consume the main-series absolute index.
        if (seasonNumber == 0)
        {
            return null;
        }

        var ordered = localEpisodes
            .Where(episode => episode.SeasonNumber > 0)
            .Distinct()
            .OrderBy(episode => episode.SeasonNumber)
            .ThenBy(episode => episode.EpisodeNumber)
            .ToArray();

        var index = Array.FindIndex(
            ordered,
            episode =>
                episode.SeasonNumber == seasonNumber &&
                episode.EpisodeNumber == episodeNumber);

        return index >= 0 ? index + 1 : null;
    }

    public static LocalEpisodeCoordinate? ResolveLocalEpisode(
        int absoluteEpisodeNumber,
        IReadOnlyList<LocalEpisodeCoordinate> localEpisodes)
    {
        if (absoluteEpisodeNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(absoluteEpisodeNumber));
        }

        ArgumentNullException.ThrowIfNull(localEpisodes);

        var ordered = localEpisodes
            .Where(episode => episode.SeasonNumber > 0)
            .Distinct()
            .OrderBy(episode => episode.SeasonNumber)
            .ThenBy(episode => episode.EpisodeNumber)
            .ToArray();

        return absoluteEpisodeNumber <= ordered.Length
            ? ordered[absoluteEpisodeNumber - 1]
            : null;
    }
}
