using AniLingo.Web.Data;
using AniLingo.Web.Features.Acquisition.Sabnzbd;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Localization;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Admin;

[Authorize(Roles = AccountRoles.Owner)]
public sealed class OperationModel(
    AppDbContext db,
    BackgroundJobQueue backgroundJobs,
    PlaybackJobQueue playbackJobs,
    SabnzbdDownloadService sabnzbd,
    SabnzbdAcquisitionStore acquisitions) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public OperationSnapshot Operation { get; private set; } = null!;
    public IReadOnlyList<OperationLogEntry> Logs { get; private set; } = [];
    public SabnzbdAcquisition? Acquisition { get; private set; }
    public SabnzbdAcquisitionAttempt? AcquisitionAttempt { get; private set; }
    public IReadOnlyList<SabnzbdBlockedRelease> AcquisitionBlocklist { get; private set; } = [];

    public bool IsSabnzbdJob =>
        SabnzbdDownloadService.IsSabnzbdOperation(Operation);

    public bool CanCancel =>
        Operation.CanCancel || (IsSabnzbdJob && Operation.IsActive);

    public bool RuntimeAvailable =>
        IsSabnzbdJob
        || (Operation.Lane == OperationLane.Interactive
            ? playbackJobs.HasRuntimeWork(Operation.Id)
            : backgroundJobs.HasRuntimeWork(Operation.Id));

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

        var operation = await new OperationStore(db).GetAsync(id, cancellationToken);
        if (operation is null)
        {
            return NotFound();
        }

        if (SabnzbdDownloadService.IsSabnzbdOperation(operation))
        {
            TempData["Status"] = (await RunSabnzbdActionAsync(
                () => sabnzbd.CancelAsync(id, cancellationToken))).Message;
            return RedirectToPage(new { id });
        }

        var cancelled = operation.Lane == OperationLane.Interactive
            ? await playbackJobs.CancelAsync(id, cancellationToken)
            : await backgroundJobs.CancelAsync(id, cancellationToken);

        TempData["Status"] = cancelled
            ? Ui["admin.operation.cancelRequested"]
            : Ui["admin.operation.cancelUnavailable"];
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRetryAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var operation = await new OperationStore(db).GetAsync(id, cancellationToken);
        if (operation is null)
        {
            return NotFound();
        }

        if (SabnzbdDownloadService.IsSabnzbdOperation(operation))
        {
            TempData["Status"] = (await RunSabnzbdActionAsync(
                () => sabnzbd.RetryAsync(id, cancellationToken))).Message;
            return RedirectToPage(new { id });
        }

        var retried = operation.Lane == OperationLane.Interactive
            ? await playbackJobs.RetryAsync(id, cancellationToken)
            : await backgroundJobs.RetryAsync(id, cancellationToken);

        TempData["Status"] = retried
            ? Ui["admin.operation.retryQueued"]
            : Ui["admin.operation.retryUnavailable"];
        return RedirectToPage(new { id });
    }

    private static async Task<SabnzbdActionOutcome> RunSabnzbdActionAsync(
        Func<Task<SabnzbdActionOutcome>> action)
    {
        try
        {
            return await action();
        }
        catch (InvalidOperationException exception)
        {
            return new SabnzbdActionOutcome(false, exception.Message);
        }
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
