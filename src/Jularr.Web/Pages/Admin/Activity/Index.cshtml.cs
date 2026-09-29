using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Admin.Activity;

/// <summary>
/// The admin/manager view of background work (downloads, imports, scans, preparation,
/// maintenance), aggregated from the existing <see cref="OperationStore"/>. Not the personal
/// consumer /Activity history.
/// </summary>
[Authorize(Policy = JularrPolicies.AdminMedia)]
public sealed class IndexModel(
    AppDbContext db,
    OperationControlService controls) : PageModel
{
    private const int ActiveLimit = 500;
    private const int AttentionLimit = 100;

    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public ActivityCenterSnapshot Activity { get; private set; } = ActivityCenterSnapshot.Empty;

    private Dictionary<Guid, OperationActionState> actions = [];

    public OperationActionState ActionsFor(OperationSnapshot operation) =>
        actions.TryGetValue(operation.Id, out var state) ? state : default;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCancelAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var result = await controls.CancelAsync(id, cancellationToken);
        if (result.Status == OperationActionStatus.NotFound)
        {
            return NotFound();
        }

        TempData["Status"] = result.Message
            ?? (result.Status == OperationActionStatus.Completed
                ? Ui["admin.operation.cancelRequested"]
                : Ui["admin.operation.cancelUnavailable"]);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRetryAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var result = await controls.RetryAsync(id, cancellationToken);
        if (result.Status == OperationActionStatus.NotFound)
        {
            return NotFound();
        }

        TempData["Status"] = result.Message
            ?? (result.Status == OperationActionStatus.Completed
                ? Ui["admin.operation.retryQueued"]
                : Ui["admin.operation.retryUnavailable"]);
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var store = new OperationStore(db);

        var active = await store.ListAsync(
            new OperationListFilter(View: "active", Limit: ActiveLimit),
            cancellationToken);
        var failed = await store.ListAsync(
            new OperationListFilter(View: "history", Status: OperationStatus.Failed, Limit: AttentionLimit),
            cancellationToken);
        var interrupted = await store.ListAsync(
            new OperationListFilter(View: "history", Status: OperationStatus.Interrupted, Limit: AttentionLimit),
            cancellationToken);
        var summary = await store.GetSummaryAsync(cancellationToken);

        Activity = ActivityCenterBuilder.Build(
            active.Concat(failed).Concat(interrupted),
            summary.CompletedToday,
            DateTime.UtcNow);

        actions = Activity.Groups
            .SelectMany(group => group.Items)
            .ToDictionary(operation => operation.Id, controls.Evaluate);
    }
}
