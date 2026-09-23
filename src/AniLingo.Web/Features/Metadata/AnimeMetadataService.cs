using AniLingo.Web.Data;
using AniLingo.Web.Features.Tracking;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Metadata;

public sealed class AnimeMetadataService(
    AppDbContext db,
    IEnumerable<IAnimeMetadataProvider> providers,
    AniListAccountStore aniListStore)
{
    public Task<AnimeMetadata?> GetAsync(
        Guid animeId,
        CancellationToken cancellationToken) =>
        db.AnimeMetadata
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.AnimeId == animeId, cancellationToken);


    public Task<IReadOnlyList<AnimeEpisodeMetadataMapping>> GetEpisodeMappingsAsync(
        Guid animeId,
        CancellationToken cancellationToken) =>
        aniListStore.LoadEpisodeMappingsAsync(animeId, cancellationToken);

    public async Task<AnimeMetadataMatchResult> MatchEpisodeRangeAsync(
        Guid animeId,
        int seasonNumber,
        int localEpisodeStart,
        int? localEpisodeEnd,
        int remoteEpisodeStart,
        string providerKey,
        string externalId,
        CancellationToken cancellationToken)
    {
        if (seasonNumber < 0)
        {
            return new AnimeMetadataMatchResult(false, "Local season must be zero or greater.");
        }

        if (localEpisodeStart <= 0)
        {
            return new AnimeMetadataMatchResult(false, "Local episode start must be greater than zero.");
        }

        if (remoteEpisodeStart <= 0)
        {
            return new AnimeMetadataMatchResult(false, "AniList episode start must be greater than zero.");
        }

        var localNumbers = await db.Episodes
            .AsNoTracking()
            .Where(x => x.AnimeId == animeId && x.SeasonNumber == seasonNumber)
            .OrderBy(x => x.Number)
            .Select(x => x.Number)
            .ToListAsync(cancellationToken);

        if (localNumbers.Count == 0)
        {
            return new AnimeMetadataMatchResult(false, $"Local season {seasonNumber} was not found.");
        }

        if (!localNumbers.Contains(localEpisodeStart))
        {
            return new AnimeMetadataMatchResult(
                false,
                $"Local episode S{seasonNumber:00}E{localEpisodeStart:00} was not found.");
        }

        var provider = GetProvider(providerKey);
        var candidate = await provider.GetAsync(externalId, cancellationToken);
        if (candidate is null)
        {
            return new AnimeMetadataMatchResult(false, "The AniList entry was not found.");
        }

        var localSeasonMaximum = localNumbers.Max();
        var resolvedEnd = localEpisodeEnd ?? AnimeEpisodeMetadataRules.ResolveAutomaticLocalEnd(
            localEpisodeStart,
            localSeasonMaximum,
            remoteEpisodeStart,
            candidate.EpisodeCount);

        if (resolvedEnd < localEpisodeStart || resolvedEnd > localSeasonMaximum)
        {
            return new AnimeMetadataMatchResult(
                false,
                $"Local episode end must be between {localEpisodeStart} and {localSeasonMaximum}.");
        }

        var remoteEpisodeEnd =
            remoteEpisodeStart + (resolvedEnd - localEpisodeStart);

        if (candidate.EpisodeCount is > 0 &&
            remoteEpisodeEnd > candidate.EpisodeCount.Value)
        {
            return new AnimeMetadataMatchResult(
                false,
                $"The mapping would reach AniList episode {remoteEpisodeEnd}, above the known episode count ({candidate.EpisodeCount}).");
        }

        var mapping = new AnimeEpisodeMetadataMapping(
            Guid.NewGuid(),
            animeId,
            seasonNumber,
            localEpisodeStart,
            resolvedEnd,
            remoteEpisodeStart,
            candidate.Provider,
            candidate.ExternalId,
            candidate.PreferredTitle,
            candidate.EpisodeCount,
            DateTimeOffset.UtcNow);

        try
        {
            var added = await aniListStore.TryAddEpisodeMappingAsync(
                mapping,
                cancellationToken);

            return added
                ? new AnimeMetadataMatchResult(true)
                : new AnimeMetadataMatchResult(
                    false,
                    "This local episode range overlaps an existing AniList mapping.");
        }
        catch (AniListAccountException exception)
        {
            return new AnimeMetadataMatchResult(false, exception.Message);
        }
    }

    public Task<bool> RemoveEpisodeMappingAsync(
        Guid animeId,
        Guid mappingId,
        CancellationToken cancellationToken) =>
        aniListStore.RemoveEpisodeMappingAsync(
            animeId,
            mappingId,
            cancellationToken);

    public async Task<ResolvedAnimeEpisodeMetadata?> ResolveEpisodeAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var episode = await db.Episodes
            .AsNoTracking()
            .Where(x => x.Id == episodeId)
            .Select(x => new
            {
                x.AnimeId,
                x.SeasonNumber,
                x.Number
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (episode is null || episode.Number <= 0)
        {
            return null;
        }

        var mappings = await aniListStore.LoadEpisodeMappingsAsync(
            episode.AnimeId,
            cancellationToken);

        var explicitMapping = mappings.SingleOrDefault(
            x => x.Contains(episode.SeasonNumber, episode.Number));

        if (explicitMapping is not null)
        {
            return new ResolvedAnimeEpisodeMetadata(
                explicitMapping.Provider,
                explicitMapping.ExternalId,
                explicitMapping.PreferredTitle,
                explicitMapping.ResolveRemoteEpisode(episode.Number),
                explicitMapping.EpisodeCount,
                IsExplicitRange: true);
        }

        // Once explicit ranges exist, never guess for uncovered episodes.
        // This prevents a split cour/part from falling back to the display match.
        if (mappings.Count > 0)
        {
            return null;
        }

        var seasonCount = await db.Episodes
            .AsNoTracking()
            .Where(x => x.AnimeId == episode.AnimeId)
            .Select(x => x.SeasonNumber)
            .Distinct()
            .Take(2)
            .CountAsync(cancellationToken);

        if (seasonCount != 1)
        {
            return null;
        }

        var metadata = await GetAsync(episode.AnimeId, cancellationToken);
        if (metadata is null)
        {
            return null;
        }

        return new ResolvedAnimeEpisodeMetadata(
            metadata.Provider,
            metadata.ExternalId,
            metadata.PreferredTitle,
            episode.Number,
            metadata.EpisodeCount,
            IsExplicitRange: false);
    }

    public async Task<IReadOnlyList<AnimeMetadataCandidate>> SearchAsync(
        string providerKey,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var provider = GetProvider(providerKey);
        return await provider.SearchAsync(query, limit, cancellationToken);
    }

    public async Task<AnimeMetadataMatchResult> MatchAsync(
        Guid animeId,
        string providerKey,
        string externalId,
        CancellationToken cancellationToken)
    {
        var animeExists = await db.Anime
            .AsNoTracking()
            .AnyAsync(x => x.Id == animeId, cancellationToken);

        if (!animeExists)
        {
            return new AnimeMetadataMatchResult(false, "Local anime was not found.");
        }

        var provider = GetProvider(providerKey);
        var candidate = await provider.GetAsync(externalId, cancellationToken);
        if (candidate is null)
        {
            return new AnimeMetadataMatchResult(false, "The metadata entry was not found.");
        }

        var usedByOtherAnime = await db.AnimeMetadata
            .AsNoTracking()
            .AnyAsync(
                x => x.Provider == provider.Key &&
                    x.ExternalId == candidate.ExternalId &&
                    x.AnimeId != animeId,
                cancellationToken);

        if (usedByOtherAnime)
        {
            return new AnimeMetadataMatchResult(
                false,
                "This metadata entry is already matched to another local anime.");
        }

        var metadata = await db.AnimeMetadata
            .SingleOrDefaultAsync(x => x.AnimeId == animeId, cancellationToken);

        if (metadata is null)
        {
            metadata = new AnimeMetadata { AnimeId = animeId };
            db.AnimeMetadata.Add(metadata);
        }

        Apply(metadata, candidate);
        await db.SaveChangesAsync(cancellationToken);

        return new AnimeMetadataMatchResult(true);
    }

    public async Task<bool> RefreshAsync(
        Guid animeId,
        CancellationToken cancellationToken)
    {
        var metadata = await db.AnimeMetadata
            .SingleOrDefaultAsync(x => x.AnimeId == animeId, cancellationToken);

        if (metadata is null)
        {
            return false;
        }

        var provider = GetProvider(metadata.Provider);
        var candidate = await provider.GetAsync(metadata.ExternalId, cancellationToken);
        if (candidate is null)
        {
            return false;
        }

        Apply(metadata, candidate);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<int> RemoveAsync(
        Guid animeId,
        CancellationToken cancellationToken) =>
        db.AnimeMetadata
            .Where(x => x.AnimeId == animeId)
            .ExecuteDeleteAsync(cancellationToken);

    private IAnimeMetadataProvider GetProvider(string providerKey) =>
        providers.FirstOrDefault(
            provider => provider.Key.Equals(providerKey, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException(
            $"Metadata provider '{providerKey}' is not registered.");

    private static void Apply(
        AnimeMetadata metadata,
        AnimeMetadataCandidate candidate)
    {
        metadata.Provider = candidate.Provider;
        metadata.ExternalId = candidate.ExternalId;
        metadata.PreferredTitle = candidate.PreferredTitle;
        metadata.RomajiTitle = candidate.RomajiTitle;
        metadata.EnglishTitle = candidate.EnglishTitle;
        metadata.NativeTitle = candidate.NativeTitle;
        metadata.Description = candidate.Description;
        metadata.CoverImageUrl = candidate.CoverImageUrl;
        metadata.BannerImageUrl = candidate.BannerImageUrl;
        metadata.Format = candidate.Format;
        metadata.Status = candidate.Status;
        metadata.Season = candidate.Season;
        metadata.SeasonYear = candidate.SeasonYear;
        metadata.EpisodeCount = candidate.EpisodeCount;
        metadata.EpisodeDurationMinutes = candidate.EpisodeDurationMinutes;
        metadata.UpdatedAt = DateTime.UtcNow;
    }
}
