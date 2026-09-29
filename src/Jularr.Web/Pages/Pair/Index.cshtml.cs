using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Pairing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Pair;

/// <summary>
/// Approves an Android TV device-code pairing (#489): any signed-in account (owner or not) can
/// approve a code shown on a TV on the same network — the phone/web half of
/// docs/ANDROID_CLIENTS.md's TV setup flow. There is deliberately no <c>[AllowAnonymous]</c> here:
/// the whole point of pairing is that the TV borrows the identity of the browser/app session that
/// approves it, so approval must go through the normal sign-in the fallback authorization policy
/// already requires. State lives only in the in-memory <see cref="DevicePairingStore"/>.
/// </summary>
public sealed class IndexModel(
    AppDbContext db,
    DevicePairingStore pairing,
    CurrentAccountContext account) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    [BindProperty(SupportsGet = true)]
    public string? Code { get; set; }

    public bool Submitted { get; private set; }

    public bool Approved { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
    }

    public async Task<IActionResult> OnPostApproveAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        Submitted = true;

        var outcome = pairing.Approve(
            Code ?? string.Empty,
            account.ProfileId,
            attemptKey: account.ProfileId);

        Approved = outcome == DevicePairingApproveOutcome.Approved;
        ErrorMessage = outcome switch
        {
            DevicePairingApproveOutcome.Approved => null,
            DevicePairingApproveOutcome.RateLimited => Ui["pairing.approve.rateLimited"],
            _ => Ui["pairing.approve.invalidOrExpired"]
        };

        return Page();
    }
}
