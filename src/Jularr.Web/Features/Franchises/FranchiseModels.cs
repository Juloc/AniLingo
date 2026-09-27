using Jularr.Web.Features.Watchlist;
using Jularr.Web.Features.Localization;

namespace Jularr.Web.Features.Franchises;

public sealed record FranchiseSummary(
    Guid Id,
    string Title,
    WatchlistIdentity Seed,
    DateTime? LastRefreshedAtUtc,
    int MemberCount);

public sealed record FranchiseMember(
    Guid FranchiseId,
    WatchlistDraft Media,
    string? RelationType,
    bool IsSeed);


public sealed record FranchiseLinksViewModel(
    UiTextBundle Ui,
    IReadOnlyList<FranchiseSummary> Franchises);
