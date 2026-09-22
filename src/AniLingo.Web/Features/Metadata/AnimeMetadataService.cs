using AniLingo.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Metadata;

public sealed class AnimeMetadataService(
    AppDbContext db,
    IEnumerable<IAnimeMetadataProvider> providers)
{
    public Task<AnimeMetadata?> GetAsync(
        Guid animeId,
        CancellationToken cancellationToken) =>
        db.AnimeMetadata
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.AnimeId == animeId, cancellationToken);

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
        metadata.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
