using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Sabnzbd;
using Jularr.Web.Infrastructure;

namespace Jularr.Web.Features.Operations;

/// <summary>What the admin may do with one operation right now.</summary>
public readonly record struct OperationActionState(
    bool CanCancel,
    bool CanRetry,
    bool RetryPayloadMissing)
{
    /// <summary>Retry is offered (possibly disabled) for a failed or interrupted, retryable operation.</summary>
    public bool RetryOffered => CanRetry || RetryPayloadMissing;
}

/// <summary>
/// The single rule set for cancel and retry, shared by the operation detail page and the Activity
/// Center so both gate the same way. Pure: runtime availability is passed in.
/// </summary>
public static class OperationActionPolicy
{
    public static bool IsSabnzbdJob(OperationSnapshot operation) =>
        SabnzbdDownloadService.IsSabnzbdOperation(operation);

    /// <summary>
    /// Active operations can be cancelled, except work owned by an external provider — unless that
    /// provider is SABnzbd, which Jularr can cancel through its API.
    /// </summary>
    public static bool CanCancel(OperationSnapshot operation) =>
        operation.CanCancel || (IsSabnzbdJob(operation) && operation.IsActive);

    public static bool IsRetryCandidate(OperationSnapshot operation) =>
        operation.Retryable
        && operation.Status is OperationStatus.Failed or OperationStatus.Interrupted;

    /// <param name="runtimeAvailable">
    /// Whether the in-process queue still holds the work delegate (anonymous delegates are never
    /// serialised, so it is gone after a restart), or the operation is a SABnzbd job.
    /// </param>
    public static OperationActionState Evaluate(
        OperationSnapshot operation,
        bool runtimeAvailable)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var retryCandidate = IsRetryCandidate(operation);
        return new OperationActionState(
            CanCancel(operation),
            retryCandidate && runtimeAvailable,
            retryCandidate && !runtimeAvailable);
    }
}

public enum OperationActionStatus
{
    NotFound = 1,
    Completed = 2,
    Unavailable = 3,
    External = 4
}

/// <param name="Message">Set only for <see cref="OperationActionStatus.External"/>: the provider's own outcome text.</param>
public sealed record OperationActionResult(
    OperationActionStatus Status,
    string? Message = null);

/// <summary>Cancels and retries operations through the queue or provider that owns their work.</summary>
public sealed class OperationControlService(
    AppDbContext db,
    BackgroundJobQueue backgroundJobs,
    PlaybackJobQueue playbackJobs,
    SabnzbdDownloadService sabnzbd)
{
    public bool HasRuntime(OperationSnapshot operation) =>
        OperationActionPolicy.IsSabnzbdJob(operation)
        || (operation.Lane == OperationLane.Interactive
            ? playbackJobs.HasRuntimeWork(operation.Id)
            : backgroundJobs.HasRuntimeWork(operation.Id));

    public OperationActionState Evaluate(OperationSnapshot operation) =>
        OperationActionPolicy.Evaluate(
            operation,
            OperationActionPolicy.IsRetryCandidate(operation) && HasRuntime(operation));

    public async Task<OperationActionResult> CancelAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var operation = await new OperationStore(db).GetAsync(id, cancellationToken);
        if (operation is null)
        {
            return new OperationActionResult(OperationActionStatus.NotFound);
        }

        if (OperationActionPolicy.IsSabnzbdJob(operation))
        {
            return await RunSabnzbdAsync(() => sabnzbd.CancelAsync(id, cancellationToken));
        }

        var cancelled = operation.Lane == OperationLane.Interactive
            ? await playbackJobs.CancelAsync(id, cancellationToken)
            : await backgroundJobs.CancelAsync(id, cancellationToken);

        return new OperationActionResult(
            cancelled ? OperationActionStatus.Completed : OperationActionStatus.Unavailable);
    }

    public async Task<OperationActionResult> RetryAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var operation = await new OperationStore(db).GetAsync(id, cancellationToken);
        if (operation is null)
        {
            return new OperationActionResult(OperationActionStatus.NotFound);
        }

        if (OperationActionPolicy.IsSabnzbdJob(operation))
        {
            return await RunSabnzbdAsync(() => sabnzbd.RetryAsync(id, cancellationToken));
        }

        var retried = operation.Lane == OperationLane.Interactive
            ? await playbackJobs.RetryAsync(id, cancellationToken)
            : await backgroundJobs.RetryAsync(id, cancellationToken);

        return new OperationActionResult(
            retried ? OperationActionStatus.Completed : OperationActionStatus.Unavailable);
    }

    private static async Task<OperationActionResult> RunSabnzbdAsync(
        Func<Task<SabnzbdActionOutcome>> action)
    {
        try
        {
            return new OperationActionResult(
                OperationActionStatus.External,
                (await action()).Message);
        }
        catch (InvalidOperationException exception)
        {
            return new OperationActionResult(OperationActionStatus.External, exception.Message);
        }
    }
}
