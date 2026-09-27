using Jularr.Web.Data;
using Jularr.Web.Features.Admin;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Playback.Decision;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Profile;

/// <summary>
/// Any signed-in user's own live playback sessions, with a Stop action (#518). Only ever the
/// current profile's own sessions: <see cref="PlaybackStreamSessionStore.Remove"/> refuses to end
/// another profile's session.
/// </summary>
public sealed class DevicesModel(
    AppDbContext db,
    AdminSessionsService sessionsService,
    PlaybackStreamSessionStore sessionStore,
    CurrentAccountContext account) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public IReadOnlyList<AdminSessionRow> Sessions { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        Sessions = await sessionsService.ListForProfileAsync(account.ProfileId, cancellationToken);
    }

    public async Task<IActionResult> OnPostStopAsync(Guid id, CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        TempData["Status"] = sessionStore.Remove(id, account.ProfileId)
            ? Ui["profile.devices.stopped"]
            : Ui["profile.devices.alreadyEnded"];

        return RedirectToPage();
    }
}
