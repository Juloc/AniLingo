using Jularr.Web.Features.Metadata;
using Jularr.Web.Features.Watchlist;

namespace Jularr.Web.Features.Franchises;

public sealed class FranchiseService(
    FranchiseStore franchises,
    MediaRelationStore relations,
    AniListMetadataProvider aniList,
    ILogger<FranchiseService> logger)
{
    private const int MaxMembersPerRefresh = 60;

    private static readonly HashSet<string> StrongRelationTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ADAPTATION",
        "SOURCE",
        "PREQUEL",
        "SEQUEL",
        "PARENT",
        "SIDE_STORY",
        "SPIN_OFF",
        "ALTERNATIVE",
        "SUMMARY",
        "COMPILATION",
        "CONTAINS"
    };

    public async Task<Guid> FollowFromSeedAsync(
        string profileId,
        WatchlistDraft seed,
        CancellationToken cancellationToken)
    {
        var franchiseId = await franchises.GetOrCreateBySeedAsync(seed, cancellationToken);
        await franchises.FollowAsync(profileId, franchiseId, cancellationToken);

        try
        {
            await RefreshAsync(franchiseId, cancellationToken);
        }
        catch (MetadataProviderException exception)
        {
            logger.LogInformation(
                exception,
                "Franchise {FranchiseId} remains followed; provider relations could not be refreshed.",
                franchiseId);
        }

        return franchiseId;
    }

    public Task UnfollowAsync(
        string profileId,
        Guid franchiseId,
        CancellationToken cancellationToken) =>
        franchises.UnfollowAsync(profileId, franchiseId, cancellationToken);

    public async Task RefreshAsync(Guid franchiseId, CancellationToken cancellationToken)
    {
        var members = (await franchises.GetMembersAsync(franchiseId, cancellationToken)).ToList();
        var queue = new Queue<FranchiseMember>(members.Where(IsAniListAnime));
        var seen = members
            .Select(member => member.Media.Identity.Key)
            .ToHashSet(StringComparer.Ordinal);

        while (queue.Count > 0 && seen.Count < MaxMembersPerRefresh)
        {
            var current = queue.Dequeue();
            var relations = await aniList.GetRelatedAnimeAsync(
                current.Media.Identity.ExternalKey,
                cancellationToken);

            foreach (var relation in relations)
            {
                if (!StrongRelationTypes.Contains(relation.RelationType))
                {
                    continue;
                }

                var candidate = relation.Candidate;
                var media = new WatchlistDraft(
                    new WatchlistIdentity(
                        WatchlistMediaType.Anime,
                        AniListMetadataProvider.ProviderKey,
                        candidate.ExternalId),
                    candidate.PreferredTitle,
                    candidate.NativeTitle,
                    candidate.CoverImageUrl,
                    candidate.Format,
                    candidate.Status,
                    candidate.SeasonYear,
                    DetailsUrl: "/Watchlist");

                await franchises.UpsertMemberAsync(
                    franchiseId,
                    media,
                    relation.RelationType,
                    false,
                    cancellationToken);
                await relations.UpsertProviderAsync(
                    current.Media.Identity,
                    media.Identity,
                    relation.RelationType,
                    AniListMetadataProvider.ProviderKey,
                    1.0,
                    confirmed: true,
                    cancellationToken);

                if (seen.Add(media.Identity.Key) && seen.Count < MaxMembersPerRefresh)
                {
                    queue.Enqueue(new FranchiseMember(
                        franchiseId,
                        media,
                        relation.RelationType,
                        false));
                }
            }
        }

        await franchises.MarkRefreshedAsync(franchiseId, cancellationToken);
    }

    private static bool IsAniListAnime(FranchiseMember member) =>
        member.Media.Identity.ProviderKey == AniListMetadataProvider.ProviderKey &&
        member.Media.Identity.MediaType == WatchlistMediaType.Anime;
}
