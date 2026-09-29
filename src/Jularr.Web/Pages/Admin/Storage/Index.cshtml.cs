using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Storage;
using Jularr.Web.Features.Storage.Insights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Admin.Storage;

/// <summary>
/// Storage insights (#414): where disk space goes and what Jularr can safely clean up. Usage comes
/// from the library inventory and cached storage state, so opening this page never wakes a
/// sleeping NAS; the cleanup only removes rebuildable Jularr cache leftovers, never library media.
/// </summary>
[Authorize(Policy = JularrPolicies.AdminMedia)]
public sealed class IndexModel(
    AppDbContext db,
    StorageUsageService usageService,
    StorageCleanupService cleanupService) : PageModel
{
    // Entries listed per cache area in the cleanup preview; the totals always cover all of them.
    public const int PreviewEntriesPerArea = 8;

    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public StorageUsageReport Usage { get; private set; } = new([], [], []);

    public StorageCacheReport Cache { get; private set; } = new([], StorageCleanupPlan.Empty);

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostCleanAsync(
        List<StorageCacheAreaKind> areas,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        var result = await cleanupService.CleanAsync(areas, cancellationToken);

        TempData["Status"] = result.Removed == 0
            ? Ui["admin.storage.cleanup.nothing"]
            : Ui.Format(
                "admin.storage.cleanup.done",
                ("files", result.Removed.ToString("N0")),
                ("size", StorageHealth.FormatBytes(result.BytesFreed)));
        return RedirectToPage();
    }

    public string HealthLabel(StorageHealthState? health) =>
        health switch
        {
            StorageHealthState.Online => Ui["admin.system.health.online"],
            StorageHealthState.Starting => Ui["admin.system.health.starting"],
            StorageHealthState.OfflineExpected => Ui["admin.system.health.sleeping"],
            StorageHealthState.OfflineUnexpected => Ui["admin.system.health.unavailable"],
            StorageHealthState.Error => Ui["admin.system.health.error"],
            _ => Ui["admin.storage.roots.notChecked"]
        };

    public static string HealthCss(StorageHealthState? health) =>
        health switch
        {
            StorageHealthState.Online => "status-ok",
            StorageHealthState.Starting or StorageHealthState.OfflineExpected => "status-warning",
            StorageHealthState.OfflineUnexpected or StorageHealthState.Error => "status-error",
            _ => ""
        };

    public string MediaKindLabel(StorageMediaKind kind) =>
        Ui[$"admin.storage.media.{kind.ToString().ToLowerInvariant()}"];

    public string AreaLabel(StorageCacheAreaKind area) =>
        Ui[$"admin.storage.cache.area.{area.ToString().ToLowerInvariant()}"];

    public string ReasonLabel(ReclaimReason reason) =>
        Ui[$"admin.storage.cache.reason.{reason.ToString().ToLowerInvariant()}"];

    public static string EntryName(ReclaimCandidate candidate) =>
        Path.GetFileName(candidate.Path);

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        Usage = await usageService.GetAsync(StorageUsageService.DefaultLargestItems, cancellationToken);
        Cache = await cleanupService.PreviewAsync(cancellationToken);
    }
}
