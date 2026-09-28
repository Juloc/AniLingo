using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Devices;
using Jularr.Web.Features.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Admin;

/// <summary>
/// Admin &gt; Devices &amp; security (#527, part of epic #510): every known client/device across
/// every account, with a Revoke action, plus a recent sign-in activity feed. Owner-only
/// (<see cref="JularrPolicies.AdminSystem"/>) unlike Admin &gt; Sessions, which
/// <see cref="JularrPolicies.SessionsStopOthers"/> also grants to a Media manager.
/// </summary>
[Authorize(Policy = JularrPolicies.AdminSystem)]
public sealed class DevicesModel(
    AppDbContext db,
    KnownDeviceRegistry devices,
    SecurityEventLog securityEvents) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public IReadOnlyList<KnownDeviceRow> Devices { get; private set; } = [];

    public IReadOnlyList<SecurityEvent> RecentEvents { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostRevokeAsync(string id, CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        TempData["Status"] = await devices.RevokeAsync(id, requesterProfileId: null, cancellationToken)
            ? Ui["admin.devices.revoked"]
            : Ui["admin.devices.alreadyGone"];

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Devices = await devices.ListAllAsync(cancellationToken);
        RecentEvents = securityEvents.Recent();
    }
}
