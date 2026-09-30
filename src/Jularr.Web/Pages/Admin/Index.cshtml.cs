using Jularr.Web.Data;
using Jularr.Web.Features.Admin;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Playback.Decision;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Admin;

/// <summary>
/// Admin → Dashboard: the live operational picture of the server. Is Jularr healthy, what is running,
/// how loaded is the machine, what is broken and does anything need the admin. The figures are read on
/// each request; Refresh loads them again.
/// </summary>
[Authorize(Policy = JularrPolicies.AdminMedia)]
public sealed class IndexModel(
    AppDbContext db,
    AdminDashboardService dashboard,
    IAuthorizationService authorization,
    PlaybackStreamSessionStore sessionStore) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public AdminDashboardSnapshot Snapshot { get; private set; } = null!;

    /// <summary>Whether the caller may see and stop live playback sessions.</summary>
    public bool CanManageSessions { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        CanManageSessions = await CanStopSessionsAsync();
        Snapshot = await dashboard.GetAsync(CanManageSessions, cancellationToken);
    }

    public async Task<IActionResult> OnPostStopSessionAsync(Guid id)
    {
        if (!await CanStopSessionsAsync())
        {
            return Forbid();
        }

        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        TempData["Status"] = sessionStore.RemoveAny(id)
            ? Ui["admin.sessions.stopped"]
            : Ui["admin.sessions.alreadyEnded"];
        return RedirectToPage();
    }

    private async Task<bool> CanStopSessionsAsync() =>
        (await authorization.AuthorizeAsync(User, JularrPolicies.SessionsStopOthers)).Succeeded;
}
