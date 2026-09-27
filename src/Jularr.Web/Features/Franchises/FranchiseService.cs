using Jularr.Web.Features.Calendar;
using Jularr.Web.Features.Metadata;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.Watchlist;

namespace Jularr.Web.Features.Franchises;

/// <summary>Reads one work and its related works from the provider.</summary>
public interface IFranchiseRelationSource
{
    Task<AniListRelatedMedia> GetRelatedAsync(WatchlistIdentity work, CancellationToken cancellationToken);
}

public sealed class AniListFranchiseRelationSource(
    AniListMetadataProvider anime,
    NovelAniListProvider reading) : IFranchiseRelationSource
{
    public Task<AniListRelatedMedia> GetRelatedAsync(WatchlistIdentity work, CancellationToken cancellationToken) =>
        work.MediaType == WatchlistMediaType.Anime
            ? anime.GetRelatedMediaAsync(work.ExternalKey, cancellationToken)
            : reading.GetRelatedMediaAsync(work.ExternalKey, cancellationToken);
}

/// <param name="Complete">Every member's relations are current; nothing is left for a later run.</param>
/// <param name="RetryAfter">Set when the run stopped early and the next run should wait this long.</param>
public sealed record FranchiseRefreshResult(int Requests, bool Complete, TimeSpan? RetryAfter);

public enum FranchiseRefreshRequest
{
    Queued,
    NotFound,
    NotAllowed,
    CoolingDown
}

public static class FranchiseRefreshStatus
{
    /// <summary>The status message of an accepted or postponed request; null when it was refused.</summary>
    public static string? MessageKey(FranchiseRefreshRequest result) => result switch
    {
        FranchiseRefreshRequest.Queued => "franchise.refreshQueued",
        FranchiseRefreshRequest.CoolingDown => "franchise.refreshCoolingDown",
        _ => null
    };
}

/// <summary>
/// Follows and grows franchises. Membership is built on the server from provider relations only:
/// following names the seed's identity, and the background refresh
/// (<see cref="FranchiseRefreshService"/>) reads the seed and its related works from AniList,
/// a bounded number of members per run, through the shared <see cref="AniListRequestLimiter"/>.
/// </summary>
public sealed class FranchiseService(
    FranchiseStore franchises,
    MediaRelationStore relations,
    IFranchiseRelationSource source,
    AniListRequestLimiter limiter,
    FranchiseRefreshSignal signal,
    ILogger<FranchiseService> logger,
    TimeProvider? clock = null)
{
    /// <summary>Provider requests of one run; a larger franchise continues in the next run.</summary>
    public const int MaxRequestsPerRun = 10;

    /// <summary>
    /// Works a franchise can hold. Known members are still refreshed at the cap, so a sequel of an
    /// existing member is found as soon as there is room; only growth beyond the cap stops.
    /// </summary>
    public const int MaxMembers = 250;

    /// <summary>A member's relations are read again after this, to find new sequels.</summary>
    public static readonly TimeSpan MemberRecheckAfter = TimeSpan.FromDays(3);

    /// <summary>Minimum time between manual refreshes of one franchise.</summary>
    public static readonly TimeSpan RefreshCooldown = TimeSpan.FromHours(1);

    /// <summary>Pause after a provider failure other than the rate limit.</summary>
    public static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(15);

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

    public static bool CanSeed(WatchlistIdentity identity) =>
        identity.ProviderKey == AniListMetadataProvider.ProviderKey &&
        identity.MediaType is WatchlistMediaType.Anime or WatchlistMediaType.Manga or WatchlistMediaType.LightNovel;

    /// <summary>Follows the franchise of <paramref name="seed"/> and queues its refresh. No provider call.</summary>
    public async Task<Guid> FollowFromSeedAsync(
        string profileId,
        WatchlistIdentity seed,
        CancellationToken cancellationToken)
    {
        if (!CanSeed(seed))
        {
            throw new ArgumentException("Only AniList anime, manga and light novels start a franchise.", nameof(seed));
        }

        var franchiseId = await franchises.GetOrCreateBySeedAsync(seed, cancellationToken);
        await franchises.FollowAsync(profileId, franchiseId, cancellationToken);
        signal.Wake();
        return franchiseId;
    }

    public async Task FollowAsync(string profileId, Guid franchiseId, CancellationToken cancellationToken)
    {
        await franchises.FollowAsync(profileId, franchiseId, cancellationToken);
        signal.Wake();
    }

    public Task UnfollowAsync(
        string profileId,
        Guid franchiseId,
        CancellationToken cancellationToken) =>
        franchises.UnfollowAsync(profileId, franchiseId, cancellationToken);

    /// <summary>
    /// Queues a full refresh for a follower of the franchise or the owner, at most once per
    /// <see cref="RefreshCooldown"/>.
    /// </summary>
    public async Task<FranchiseRefreshRequest> RequestRefreshAsync(
        Guid franchiseId,
        string profileId,
        bool isOwner,
        CancellationToken cancellationToken)
    {
        if (await franchises.GetAsync(franchiseId, cancellationToken) is null)
        {
            return FranchiseRefreshRequest.NotFound;
        }

        if (!isOwner && !await franchises.IsFollowedAsync(profileId, franchiseId, cancellationToken))
        {
            return FranchiseRefreshRequest.NotAllowed;
        }

        var now = Now();
        if (!await franchises.TryRequestRefreshAsync(franchiseId, now, now - RefreshCooldown, cancellationToken))
        {
            return FranchiseRefreshRequest.CoolingDown;
        }

        signal.Wake();
        return FranchiseRefreshRequest.Queued;
    }

    /// <summary>
    /// One bounded refresh run: reads the relations of up to <see cref="MaxRequestsPerRun"/> due
    /// members (the seed first, then never-read ones, then the oldest), adds newly related works
    /// and records the graph. Stops early while AniList rate limits Jularr.
    /// </summary>
    public async Task<FranchiseRefreshResult> RefreshAsync(Guid franchiseId, CancellationToken cancellationToken)
    {
        var franchise = await franchises.GetAsync(franchiseId, cancellationToken);
        if (franchise is null)
        {
            return new FranchiseRefreshResult(0, true, null);
        }

        var requestedAt = await franchises.GetRefreshRequestedAsync(franchiseId, cancellationToken);
        var members = await franchises.GetMembersAsync(franchiseId, cancellationToken);
        var keys = members.Select(member => member.Media.Identity.Key).ToHashSet(StringComparer.Ordinal);
        var due = new Queue<DueMember>(DueMembers(franchise.Seed, members, requestedAt, Now()));
        var requests = 0;

        while (requests < MaxRequestsPerRun && due.TryDequeue(out var target))
        {
            if (await limiter.WaitAsync(cancellationToken) is { } blocked)
            {
                return new FranchiseRefreshResult(requests, false, blocked);
            }

            AniListRelatedMedia related;
            try
            {
                requests++;
                related = await source.GetRelatedAsync(target.Identity, cancellationToken);
            }
            catch (Exception exception) when (
                exception is MetadataProviderException or NovelMetadataProviderException)
            {
                if (limiter.BlockedFor() is { } limited)
                {
                    return new FranchiseRefreshResult(requests, false, limited);
                }

                logger.LogInformation(
                    exception,
                    "Relations of {Work} in franchise {FranchiseId} could not be read; retrying later.",
                    target.Identity.Key,
                    franchiseId);
                await franchises.MarkMemberCheckedAsync(franchiseId, target.Identity, Now(), cancellationToken);
                return new FranchiseRefreshResult(requests, false, FailureBackoff);
            }

            var self = related.Media is { } media &&
                       media.ExternalId == target.Identity.ExternalKey &&
                       TryMap(media, out var own)
                ? own with { Identity = target.Identity }
                : null;
            // A seed AniList does not return (removed or adult) still gets a row, so it is not
            // asked for again on every run.
            var data = self ?? (target.IsSeed && !keys.Contains(target.Identity.Key)
                ? new WatchlistDraft(target.Identity, $"AniList {target.Identity.ExternalKey}")
                : null);
            if (data is not null)
            {
                await franchises.UpsertMemberAsync(franchiseId, data, target.RelationType, target.IsSeed, cancellationToken);
                keys.Add(target.Identity.Key);
                if (target.IsSeed)
                {
                    await franchises.SetTitleAsync(franchiseId, data.Title, cancellationToken);
                }
            }

            foreach (var relation in related.Relations)
            {
                if (!StrongRelationTypes.Contains(relation.RelationType) ||
                    !TryMap(relation.Media, out var work) ||
                    work.Identity.Key == target.Identity.Key)
                {
                    continue;
                }

                var isNew = !keys.Contains(work.Identity.Key);
                if (isNew && keys.Count >= MaxMembers)
                {
                    continue;
                }

                // Known members keep the data read for them directly; only new works are added.
                if (isNew)
                {
                    await franchises.UpsertMemberAsync(franchiseId, work, relation.RelationType, false, cancellationToken);
                }

                await relations.UpsertProviderAsync(
                    target.Identity,
                    work.Identity,
                    relation.RelationType,
                    AniListMetadataProvider.ProviderKey,
                    cancellationToken);
                if (keys.Add(work.Identity.Key) && CanSeed(work.Identity))
                {
                    due.Enqueue(new DueMember(work.Identity, relation.RelationType, false, null));
                }
            }

            await franchises.MarkMemberCheckedAsync(franchiseId, target.Identity, Now(), cancellationToken);
        }

        var remaining = DueMembers(
                franchise.Seed,
                await franchises.GetMembersAsync(franchiseId, cancellationToken),
                requestedAt,
                Now())
            .Any();
        if (!remaining)
        {
            await franchises.MarkRefreshedAsync(franchiseId, Now(), cancellationToken);
        }

        return new FranchiseRefreshResult(requests, !remaining, null);
    }

    private sealed record DueMember(WatchlistIdentity Identity, string? RelationType, bool IsSeed, DateTime? CheckedAt);

    /// <summary>Members whose relations should be read now, in the order they are read.</summary>
    private static IEnumerable<DueMember> DueMembers(
        WatchlistIdentity seed,
        IReadOnlyList<FranchiseMember> members,
        DateTime? requestedAt,
        DateTime nowUtc)
    {
        var candidates = members
            .Where(member => CanSeed(member.Media.Identity))
            .Select(member => new DueMember(member.Media.Identity, member.RelationType, member.IsSeed, member.RelationsCheckedAtUtc))
            .ToList();
        if (!members.Any(member => member.IsSeed))
        {
            candidates.Insert(0, new DueMember(seed, null, true, null));
        }

        return candidates
            .Where(member =>
                member.CheckedAt is not { } checkedAt ||
                nowUtc - checkedAt >= MemberRecheckAfter ||
                (requestedAt is { } requested && checkedAt < requested))
            .OrderByDescending(member => member.IsSeed)
            .ThenBy(member => member.CheckedAt ?? DateTime.MinValue);
    }

    private static bool TryMap(AniListMediaSummary media, out WatchlistDraft draft)
    {
        var type = media.MediaType switch
        {
            "ANIME" => WatchlistMediaType.Anime,
            "MANGA" when string.Equals(media.Format, "NOVEL", StringComparison.OrdinalIgnoreCase) =>
                WatchlistMediaType.LightNovel,
            "MANGA" => WatchlistMediaType.Manga,
            _ => (WatchlistMediaType?)null
        };

        if (type is null || string.IsNullOrWhiteSpace(media.Title))
        {
            draft = null!;
            return false;
        }

        draft = new WatchlistDraft(
            new WatchlistIdentity(type.Value, AniListMetadataProvider.ProviderKey, media.ExternalId),
            media.Title,
            media.NativeTitle,
            media.CoverImageUrl,
            media.Format,
            media.Status,
            media.Year);
        return true;
    }

    private DateTime Now() => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
}
