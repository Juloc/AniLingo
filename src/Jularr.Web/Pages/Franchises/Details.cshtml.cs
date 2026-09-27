using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Franchises;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Metadata;
using Jularr.Web.Features.Watchlist;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Franchises;

public sealed class DetailsModel(
    AppDbContext db,
    CurrentAccountContext account,
    FranchiseStore franchises,
    FranchiseService franchiseService,
    MediaRelationStore relations,
    WatchlistStore watchlist) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public FranchiseSummary Franchise { get; private set; } = null!;

    public IReadOnlyList<FranchiseMember> Members { get; private set; } = [];

    public IReadOnlyList<MediaRelation> Relations { get; private set; } = [];

    public IReadOnlySet<string> FollowedKeys { get; private set; } = new HashSet<string>();

    public bool IsFollowed { get; private set; }

    public bool IsOwner => account.IsOwner;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        var franchise = await franchises.GetAsync(id, cancellationToken);
        if (franchise is null)
        {
            return NotFound();
        }

        Franchise = franchise;
        Members = await franchises.GetMembersAsync(id, cancellationToken);
        Relations = await relations.GetForMembersAsync(
            Members.Select(member => member.Media.Identity).ToArray(),
            cancellationToken);
        FollowedKeys = await watchlist.GetEffectiveKeysAsync(account.ProfileId, cancellationToken);
        IsFollowed = await franchises.IsFollowedAsync(account.ProfileId, id, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostFollowAsync(Guid id, CancellationToken cancellationToken)
    {
        var franchise = await franchises.GetAsync(id, cancellationToken);
        if (franchise is null)
        {
            return NotFound();
        }

        await franchises.FollowAsync(account.ProfileId, id, cancellationToken);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostUnfollowAsync(Guid id, CancellationToken cancellationToken)
    {
        await franchiseService.UnfollowAsync(account.ProfileId, id, cancellationToken);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRefreshAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await franchiseService.RefreshAsync(id, cancellationToken);
        }
        catch (MetadataProviderException)
        {
            TempData["Status"] = "Provider relations could not be refreshed. Existing franchise data was kept.";
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRelationAsync(
        Guid id,
        string? fromKey,
        string? toKey,
        string? relationType,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner ||
            string.IsNullOrWhiteSpace(fromKey) ||
            string.IsNullOrWhiteSpace(toKey) ||
            string.IsNullOrWhiteSpace(relationType) ||
            fromKey == toKey)
        {
            return BadRequest();
        }

        var members = await franchises.GetMembersAsync(id, cancellationToken);
        var from = members.FirstOrDefault(member => member.Media.Identity.Key == fromKey);
        var to = members.FirstOrDefault(member => member.Media.Identity.Key == toKey);
        if (from is null || to is null)
        {
            return BadRequest();
        }

        await relations.UpsertManualAsync(
            from.Media.Identity,
            to.Media.Identity,
            relationType,
            cancellationToken);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRelationReviewAsync(
        Guid id,
        Guid relationId,
        bool confirm,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        await relations.SetReviewStateAsync(
            relationId,
            confirm ? MediaRelationReviewState.Confirmed : MediaRelationReviewState.Rejected,
            cancellationToken);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRelationDeleteAsync(
        Guid id,
        Guid relationId,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        await relations.DeleteManualAsync(relationId, cancellationToken);
        return RedirectToPage(new { id });
    }

    public static string TypeLabelKey(WatchlistMediaType type) =>
        $"watchlist.type.{WatchlistMediaTypeNames.ToCategory(type)}";
}
