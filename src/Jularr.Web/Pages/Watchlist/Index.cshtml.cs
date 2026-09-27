using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Franchises;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Watchlist;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Watchlist;

public sealed class IndexModel(
    AppDbContext db,
    CurrentAccountContext account,
    WatchlistStore watchlist,
    WatchlistLibraryResolver library,
    FranchiseStore franchises,
    FranchiseService franchiseService) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public IReadOnlyList<WatchlistItem> Items { get; private set; } = [];

    public IReadOnlyList<FranchiseSummary> Franchises { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        Items = await library.ApplyAsync(
            await watchlist.GetEffectiveAsync(account.ProfileId, cancellationToken),
            cancellationToken);
        Franchises = await franchises.ListFollowedAsync(account.ProfileId, cancellationToken);
    }

    public async Task<IActionResult> OnPostRemoveAsync(
        string mediaType,
        string provider,
        string externalId,
        CancellationToken cancellationToken)
    {
        if (!WatchlistDraftInput.TryIdentity(mediaType, provider, externalId, out var identity))
        {
            return BadRequest();
        }

        await watchlist.UnfollowAsync(account.ProfileId, identity, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostFollowFranchiseAsync(
        string mediaType,
        string provider,
        string externalId,
        CancellationToken cancellationToken)
    {
        if (!WatchlistDraftInput.TryIdentity(mediaType, provider, externalId, out var identity) ||
            !FranchiseService.CanSeed(identity))
        {
            return BadRequest();
        }

        await franchiseService.FollowFromSeedAsync(account.ProfileId, identity, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUnfollowFranchiseAsync(
        Guid franchiseId,
        CancellationToken cancellationToken)
    {
        await franchiseService.UnfollowAsync(account.ProfileId, franchiseId, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRefreshFranchiseAsync(
        Guid franchiseId,
        CancellationToken cancellationToken)
    {
        var result = await franchiseService.RequestRefreshAsync(
            franchiseId,
            account.ProfileId,
            account.IsOwner,
            cancellationToken);
        if (FranchiseRefreshStatus.MessageKey(result) is not { } key)
        {
            return result == FranchiseRefreshRequest.NotFound ? NotFound() : Forbid();
        }

        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        TempData["Status"] = ui[key];
        return RedirectToPage();
    }
}
