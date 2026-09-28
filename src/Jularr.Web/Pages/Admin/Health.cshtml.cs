using Jularr.Web.Data;
using Jularr.Web.Features.Ai;
using Jularr.Web.Features.Appearance;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Health;
using Jularr.Web.Features.Localization;
using Jularr.Web.Infrastructure.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Admin;

/// <summary>
/// Admin &gt; Health (#528, part of epic #510): the dedicated system-health and update-status
/// page, deeper than the Admin overview's quick status row (#518). Every section is an
/// actionable OK/warning/error state with a link to the page that resolves it, not raw logs.
/// </summary>
[Authorize(Policy = JularrPolicies.AdminSystem)]
public sealed class HealthModel(
    AppDbContext db,
    SystemHealthService health,
    GitHubReleaseCheckService updateCheck,
    CodexCliProvider codex) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public SystemHealthSnapshot Snapshot { get; private set; } = null!;

    public AiProviderStatus AiStatus { get; private set; } =
        new("codex-cli", "OpenAI Codex CLI", false, false, null, null, null);

    public UpdateStatus? Update { get; private set; }

    public string RunningVersion { get; } = AppBuildInfo.Version;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        await LoadAsync(cancellationToken);
    }

    // Opt-in: the only place this page talks to GitHub. GET never does (#528).
    public async Task<IActionResult> OnPostCheckUpdateAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var status = await updateCheck.RefreshAsync(cancellationToken);
        TempData["Status"] = status.State switch
        {
            UpdateCheckState.UpToDate => Ui["admin.health.update.upToDate"],
            UpdateCheckState.UpdateAvailable => Ui.Format(
                "admin.health.update.available",
                ("version", status.LatestVersion ?? "?")),
            _ => Ui["admin.health.update.unavailable"]
        };

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Snapshot = await health.GetAsync(cancellationToken);
        AiStatus = await codex.GetStatusAsync(cancellationToken);
        Update = updateCheck.GetCached();
    }
}
