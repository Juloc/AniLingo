using Jularr.Web.Features.Acquisition.Sabnzbd;
using Jularr.Web.Infrastructure;

namespace Jularr.Web.Features.Operations;

/// <summary>
/// What became of a cancel or retry. <see cref="Message"/> is set when the download client explained
/// the outcome itself; otherwise the caller says whether the worker took the request.
/// </summary>
public sealed record OperationActionOutcome(bool Succeeded, string? Message = null);

/// <summary>
/// The one place that knows whether an operation can be cancelled or retried right now and does it,
/// for the activity list and the operation page alike. An action is only offered when a worker (or the
/// download client) can actually carry it out.
/// </summary>
public interface IOperationActions
{
    bool CanCancel(OperationSnapshot operation);

    bool CanRetry(OperationSnapshot operation);

    /// <summary>Whether something in this process (or the download client) still holds the work of the operation.</summary>
    bool HasRuntime(OperationSnapshot operation);

    Task<OperationActionOutcome> CancelAsync(OperationSnapshot operation, CancellationToken cancellationToken);

    Task<OperationActionOutcome> RetryAsync(OperationSnapshot operation, CancellationToken cancellationToken);
}

public sealed class OperationActions(
    BackgroundJobQueue backgroundJobs,
    PlaybackJobQueue playbackJobs,
    SabnzbdDownloadService sabnzbd) : IOperationActions
{
    public bool CanCancel(OperationSnapshot operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation.CanCancel
            || (SabnzbdDownloadService.IsSabnzbdOperation(operation) && operation.IsActive);
    }

    public bool CanRetry(OperationSnapshot operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation.Retryable
            && operation.Status is OperationStatus.Failed or OperationStatus.Interrupted
            && HasRuntime(operation);
    }

    public async Task<OperationActionOutcome> CancelAsync(
        OperationSnapshot operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (SabnzbdDownloadService.IsSabnzbdOperation(operation))
        {
            return await RunSabnzbdAsync(() => sabnzbd.CancelAsync(operation.Id, cancellationToken));
        }

        return new OperationActionOutcome(
            operation.Lane == OperationLane.Interactive
                ? await playbackJobs.CancelAsync(operation.Id, cancellationToken)
                : await backgroundJobs.CancelAsync(operation.Id, cancellationToken));
    }

    public async Task<OperationActionOutcome> RetryAsync(
        OperationSnapshot operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (SabnzbdDownloadService.IsSabnzbdOperation(operation))
        {
            return await RunSabnzbdAsync(() => sabnzbd.RetryAsync(operation.Id, cancellationToken));
        }

        return new OperationActionOutcome(
            operation.Lane == OperationLane.Interactive
                ? await playbackJobs.RetryAsync(operation.Id, cancellationToken)
                : await backgroundJobs.RetryAsync(operation.Id, cancellationToken));
    }

    public bool HasRuntime(OperationSnapshot operation) =>
        SabnzbdDownloadService.IsSabnzbdOperation(operation)
        || (operation.Lane == OperationLane.Interactive
            ? playbackJobs.HasRuntimeWork(operation.Id)
            : backgroundJobs.HasRuntimeWork(operation.Id));

    private static async Task<OperationActionOutcome> RunSabnzbdAsync(Func<Task<SabnzbdActionOutcome>> action)
    {
        try
        {
            var outcome = await action();
            return new OperationActionOutcome(outcome.Success, outcome.Message);
        }
        catch (InvalidOperationException exception)
        {
            return new OperationActionOutcome(false, exception.Message);
        }
    }
}
