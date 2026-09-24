using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
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
    PlaybackJobQueue playbackJobs) : PageModel
{
    public OperationSnapshot Operation { get; private set; } = null!;
    public IReadOnlyList<OperationLogEntry> Logs { get; private set; } = [];

    public bool RuntimeAvailable =>
        Operation.Lane == OperationLane.Interactive
            ? playbackJobs.HasRuntimeWork(Operation.Id)
            : backgroundJobs.HasRuntimeWork(Operation.Id);

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return await LoadAsync(id, cancellationToken)
            ? Page()
            : NotFound();
    }

    public async Task<IActionResult> OnPostCancelAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var operation = await new OperationStore(db).GetAsync(id, cancellationToken);
        if (operation is null)
        {
            return NotFound();
        }

        var cancelled = operation.Lane == OperationLane.Interactive
            ? await playbackJobs.CancelAsync(id, cancellationToken)
            : await backgroundJobs.CancelAsync(id, cancellationToken);

        TempData["Status"] = cancelled
            ? "Cancellation requested."
            : "This operation is no longer attached to a running worker.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRetryAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var operation = await new OperationStore(db).GetAsync(id, cancellationToken);
        if (operation is null)
        {
            return NotFound();
        }

        var retried = operation.Lane == OperationLane.Interactive
            ? await playbackJobs.RetryAsync(id, cancellationToken)
            : await backgroundJobs.RetryAsync(id, cancellationToken);

        TempData["Status"] = retried
            ? "Retry queued."
            : "Retry is unavailable after a server restart or for this operation type.";
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
        Logs = await store.ListLogsAsync(
            new OperationLogFilter(OperationId: id, Limit: 300),
            cancellationToken);
        return true;
    }
}
