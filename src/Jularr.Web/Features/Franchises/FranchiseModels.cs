using Jularr.Web.Features.Watchlist;
using Jularr.Web.Features.Localization;

namespace Jularr.Web.Features.Franchises;

/// <param name="Title">The seed's provider title; empty until the first refresh has read it.</param>
public sealed record FranchiseSummary(
    Guid Id,
    string Title,
    WatchlistIdentity Seed,
    DateTime? LastRefreshedAtUtc,
    int MemberCount);

/// <param name="RelationType">How the work was found: its relation to the member that listed it.</param>
/// <param name="RelationsCheckedAtUtc">When the work's own relations were last read from the provider.</param>
public sealed record FranchiseMember(
    Guid FranchiseId,
    WatchlistDraft Media,
    string? RelationType,
    bool IsSeed,
    DateTime? RelationsCheckedAtUtc = null);

/// <summary>
/// Other works of the same franchise directly typed to one work (an "Adaptation" bucket, a
/// "Sequel / Prequel" bucket, ...), or the franchise-wide fallback bucket for members reached
/// only through shared membership, with no specific typed edge to that work.
/// </summary>
/// <param name="GroupKey">One of <see cref="FranchiseLabels.RelationGroupOrder"/>.</param>
public sealed record FranchiseRelationGroup(
    string GroupKey,
    IReadOnlyList<WatchlistDraft> Items);

public sealed record FranchiseLinksViewModel(
    UiTextBundle Ui,
    IReadOnlyList<FranchiseSummary> Franchises,
    IReadOnlyList<FranchiseRelationGroup> Groups);
