using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.DownloadClients;
using Jularr.Web.Features.Acquisition.Sabnzbd;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Admin;

[Authorize(Policy = JularrPolicies.AdminMedia)]
public sealed class OperationModel(
    AppDbContext db,
    OperationControlService controls,
    SabnzbdAcquisitionStore acquisitions) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public OperationSnapshot Operation { get; private set; } = null!;
    public IReadOnlyList<OperationLogEntry> Logs { get; private set; } = [];
    public SabnzbdAcquisition? Acquisition { get; private set; }
    public SabnzbdAcquisitionAttempt? AcquisitionAttempt { get; private set; }
    public IReadOnlyList<SabnzbdBlockedRelease> AcquisitionBlocklist { get; private set; } = [];

    /// <summary>Routing and completed-download import details of an external download, when recorded.</summary>
    public DownloadOperationDetails? DownloadDetails { get; private set; }

    public bool IsSabnzbdJob =>
        OperationActionPolicy.IsSabnzbdJob(Operation);

    public bool CanCancel =>
        OperationActionPolicy.CanCancel(Operation);

    public bool RuntimeAvailable =>
        controls.HasRuntime(Operation);

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        return await LoadAsync(id, cancellationToken)
            ? Page()
            : NotFound();
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
        return RedirectToPage(new { id });
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
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var store = new OperationStore(db);
        var operation = await store.GetAsync(id, cancellationToken);
        if (operation is null)
        {
            return false;
        }

        Operation = operation;
        DownloadDetails = DownloadOperationDetails.TryParse(operation.Details, out var details) ? details : null;
        Logs = await store.ListLogsAsync(
            new OperationLogFilter(OperationId: id, Limit: 300),
            cancellationToken);

        if (operation.Kind == SabnzbdAcquisitionService.OperationKind)
        {
            var state = await acquisitions.LoadAsync(cancellationToken);
            if (state.FindByOperation(id) is { } relation)
            {
                Acquisition = relation.Acquisition;
                AcquisitionAttempt = relation.Attempt;
                var identities = relation.Acquisition.Attempts
                    .Select(attempt => attempt.ReleaseIdentity)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                AcquisitionBlocklist = state.Blocklist
                    .Where(entry => identities.Contains(entry.ReleaseIdentity))
                    .ToArray();
            }
        }

        return true;
    }
}
