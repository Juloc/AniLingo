using Jularr.Web.Data;
using Jularr.Web.Features.Admin;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Devices;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Playback.Decision;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Profile;

/// <summary>
/// Any signed-in user's own live playback sessions, with a Stop action (#518), and their own
/// known devices, with a Revoke action (#527). Only ever the current profile's own data:
/// <see cref="PlaybackStreamSessionStore.Remove"/> refuses to end another profile's session and
/// <see cref="KnownDeviceRegistry.RevokeAsync"/> refuses to revoke another profile's device.
/// </summary>
public sealed class DevicesModel(
    AppDbContext db,
    AdminSessionsService sessionsService,
    PlaybackStreamSessionStore sessionStore,
    KnownDeviceRegistry deviceRegistry,
    CurrentAccountContext account) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public IReadOnlyList<AdminSessionRow> Sessions { get; private set; } = [];

    public IReadOnlyList<KnownDeviceRow> KnownDevices { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        Sessions = await sessionsService.ListForProfileAsync(account.ProfileId, cancellationToken);
        KnownDevices = await deviceRegistry.ListForProfileAsync(account.ProfileId, cancellationToken);
    }

    public async Task<IActionResult> OnPostStopAsync(Guid id, CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        TempData["Status"] = sessionStore.Remove(id, account.ProfileId)
            ? Ui["profile.devices.stopped"]
            : Ui["profile.devices.alreadyEnded"];

        return RedirectToPage();
    }

    /// <summary>Revokes one of the signed-in user's own known devices. Never another profile's.</summary>
    public async Task<IActionResult> OnPostRevokeAsync(string id, CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        TempData["Status"] = await deviceRegistry.RevokeAsync(id, account.ProfileId, cancellationToken)
            ? Ui["profile.devices.revoked"]
            : Ui["profile.devices.alreadyGone"];

        return RedirectToPage();
    }
}
