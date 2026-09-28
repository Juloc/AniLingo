using Jularr.Web.Data;
using Jularr.Web.Features.Admin;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Playback.Decision;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Admin;

/// <summary>Admin page: every live playback session on the server, with a Stop action (#518).</summary>
[Authorize(Policy = JularrPolicies.SessionsStopOthers)]
public sealed class SessionsModel(
    AppDbContext db,
    AdminSessionsService sessionsService,
    PlaybackStreamSessionStore sessionStore) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public IReadOnlyList<AdminSessionRow> Sessions { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        Sessions = await sessionsService.ListAllAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostStopAsync(Guid id, CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        TempData["Status"] = sessionStore.RemoveAny(id)
            ? Ui["admin.sessions.stopped"]
            : Ui["admin.sessions.alreadyEnded"];

        return RedirectToPage();
    }
}
