using Jularr.Web.Data;
using Jularr.Web.Features.Ai;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Jularr.Web.Infrastructure.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Pages.Admin;

/// <summary>
/// Owner AI control center: the shared Codex connection, detected capabilities, discovered models,
/// provider quota, live activity and usage across all profiles.
/// </summary>
[Authorize(Policy = JularrPolicies.AdminSystem)]
public sealed class AiModel(
    AppDbContext db,
    CodexCliProvider codex,
    AiModelCatalogService catalogs,
    AiUsageStore usageStore,
    AiActivityTracker activityTracker,
    TimeProvider time) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public AiProviderStatus Provider { get; private set; } =
        new("codex-cli", "OpenAI Codex CLI", false, false, null, null, null);

    public DeviceLoginSnapshot Login { get; private set; } =
        new(DeviceLoginState.Idle, null, null, null, null);

    public AiServerDiagnostics Diagnostics { get; private set; } =
        new(AiProviderCapabilities.None(AiTransports.CodexExec), null, null, null, null);

    public AiModelCatalog Catalog { get; private set; } = AiModelCatalog.Empty(AiModelCatalogKeys.CodexServer);

    public AiUsageReport Usage { get; private set; } = AiUsageReport.Empty(AiUsagePeriod.Today);

    public AiActivityPanel Activity { get; private set; } = null!;

    public IReadOnlyDictionary<string, string> ProfileNames { get; private set; } = new Dictionary<string, string>();

    public DateTimeOffset Now { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Period { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostConnectAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var snapshot = await codex.StartDeviceLoginAsync(cancellationToken);

        TempData["Status"] = snapshot.State switch
        {
            DeviceLoginState.WaitingForUser => Ui["admin.ai.loginStarted"],
            DeviceLoginState.Succeeded => Ui["admin.ai.connected"],
            DeviceLoginState.Failed => snapshot.Message ?? Ui["admin.ai.loginFailed"],
            _ => Ui["admin.ai.loginStarting"]
        };

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCancelAsync()
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        codex.CancelDeviceLogin();
        TempData["Status"] = Ui["admin.ai.loginCancelled"];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostLogoutAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var success = await codex.LogoutAsync(cancellationToken);
        TempData["Status"] = success ? Ui["admin.ai.disconnected"] : Ui["admin.ai.logoutFailed"];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRefreshModelsAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var catalog = await catalogs.RefreshAsync(AiModelCatalogKeys.CodexServer, codex.ListModelsAsync, cancellationToken);
        TempData["Status"] = catalog.LastError is null
            ? Ui.Format("settings.ai.modelsRefreshed", ("count", catalog.Models.Count))
            : catalog.Discovery == AiModelDiscovery.Unsupported
                ? AiViewFormat.CatalogStatus(Ui, catalog, time.GetUtcNow())
                : Ui["ai.models.refreshFailed"];
        return RedirectToPage(new { Period });
    }

    public async Task<IActionResult> OnPostRefreshQuotaAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var diagnostics = await codex.GetDiagnosticsAsync(refreshQuota: true, cancellationToken);
        TempData["Status"] = diagnostics.Quota is null
            ? Ui["ai.quota.unavailable"]
            : Ui["ai.quota.refreshedStatus"];
        return RedirectToPage(new { Period });
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Now = time.GetUtcNow();
        Provider = await codex.GetStatusAsync(cancellationToken);
        Login = codex.GetDeviceLoginSnapshot();
        // Account, quota and models come from the last explicit refresh; a GET does not ask Codex.
        Catalog = await catalogs.GetCachedAsync(AiModelCatalogKeys.CodexServer, cancellationToken);
        var diagnostics = codex.GetCachedDiagnostics();
        Diagnostics = diagnostics with { Capabilities = diagnostics.Capabilities.WithCatalog(Catalog) };

        ProfileNames = await db.OwnerAccounts
            .AsNoTracking()
            .Select(x => new { x.Id, x.UserName })
            .ToDictionaryAsync(x => x.Id, x => x.UserName, StringComparer.Ordinal, cancellationToken);

        Usage = await usageStore.GetReportAsync(
            null,
            DateOnly.FromDateTime(Now.UtcDateTime),
            Settings.AiModel.ParsePeriod(Period),
            cancellationToken);

        Activity = new AiActivityPanel(
            Ui,
            AiActivityEndpoints.List(activityTracker, null, Now).Take(25).ToArray(),
            AllProfiles: true,
            ProfileNames);
    }
}
