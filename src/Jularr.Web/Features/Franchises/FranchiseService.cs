using Jularr.Web.Features.Metadata;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.Watchlist;

namespace Jularr.Web.Features.Franchises;

public sealed class FranchiseService(
    FranchiseStore franchises,
    MediaRelationStore relations,
    AniListMetadataProvider aniList,
    NovelAniListProvider readingAniList,
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
        catch (Exception exception) when (
            exception is MetadataProviderException or NovelMetadataProviderException)
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
        var queue = new Queue<FranchiseMember>(members.Where(IsAniListMember));
        var seen = members
            .Select(member => member.Media.Identity.Key)
            .ToHashSet(StringComparer.Ordinal);

        while (queue.Count > 0 && seen.Count < MaxMembersPerRefresh)
        {
            var current = queue.Dequeue();
            var related = current.Media.Identity.MediaType == WatchlistMediaType.Anime
                ? await aniList.GetRelatedMediaAsync(
                    current.Media.Identity.ExternalKey,
                    cancellationToken)
                : await readingAniList.GetRelatedMediaAsync(
                    current.Media.Identity.ExternalKey,
                    cancellationToken);

            foreach (var relation in related)
            {
                if (!StrongRelationTypes.Contains(relation.RelationType) ||
                    !TryMapRelation(relation, out var media))
                {
                    continue;
                }

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

    private static bool IsAniListMember(FranchiseMember member) =>
        member.Media.Identity.ProviderKey == AniListMetadataProvider.ProviderKey &&
        member.Media.Identity.MediaType is
            WatchlistMediaType.Anime or
            WatchlistMediaType.Manga or
            WatchlistMediaType.LightNovel;

    private static bool TryMapRelation(
        AniListMediaRelation relation,
        out WatchlistDraft media)
    {
        var type = relation.MediaType switch
        {
            "ANIME" => WatchlistMediaType.Anime,
            "MANGA" when string.Equals(relation.Format, "NOVEL", StringComparison.OrdinalIgnoreCase) =>
                WatchlistMediaType.LightNovel,
            "MANGA" => WatchlistMediaType.Manga,
            _ => (WatchlistMediaType?)null
        };

        if (type is null)
        {
            media = null!;
            return false;
        }

        media = new WatchlistDraft(
            new WatchlistIdentity(
                type.Value,
                AniListMetadataProvider.ProviderKey,
                relation.ExternalId),
            relation.Title,
            relation.NativeTitle,
            relation.CoverImageUrl,
            relation.Format,
            relation.Status,
            relation.Year,
            DetailsUrl: type == WatchlistMediaType.Anime
                ? $"https://anilist.co/anime/{relation.ExternalId}"
                : $"https://anilist.co/manga/{relation.ExternalId}");
        return true;
    }
}
