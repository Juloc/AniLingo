using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.DownloadClients;
using Jularr.Web.Features.Operations;

namespace Jularr.Web.Features.Acquisition.Import;

/// <summary>
/// The one completed-download import step for request-backed and manual downloads: resolve the
/// completed path from the exact download client, apply the remote-path mapping, hand the files
/// to the media type's importer through <see cref="CompletedDownloadDispatcher"/> and record on
/// the download Operation what happened (reported path, mapped path, destination, import mode,
/// result). Request state stays with Wanted; this class never grabs another release.
/// </summary>
public sealed class CompletedDownloadImportService(
    ICompletedDownloadLocationResolver locations,
    CompletedDownloadDispatcher dispatcher,
    AppDbContext db,
    AcquisitionAccessStore requests,
    ILogger<CompletedDownloadImportService> logger)
{
    /// <summary>
    /// Operation kind of a download the owner sent by hand (an NZB URL or file), not for a
    /// request. Its media type comes from the download routing details.
    /// </summary>
    public const string ManualDownloadOperationKind = "sabnzbd-download";

    /// <summary>
    /// How long a finished download may wait for its files (a purged SABnzbd history entry,
    /// an unmapped path, an offline share) before Jularr stops trying and asks the owner.
    /// </summary>
    public static readonly TimeSpan CompletedImportTimeout = TimeSpan.FromHours(24);

    public const int MaxManualImportsPerPass = 25;

    public async Task<CompletedDownloadImportResult> ImportAsync(
        OperationSnapshot operation,
        MediaAcquisitionKind kind,
        AcquisitionRequest? request,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var location = await locations.ResolveAsync(operation, cancellationToken);
        if (!location.Resolved || string.IsNullOrWhiteSpace(location.SourcePath))
        {
            var waiting = CompletedDownloadImportResult.RetryLater(location.Message);
            await RecordAsync(operation, location, waiting, nowUtc, cancellationToken);
            return waiting;
        }

        var result = await dispatcher.DispatchAsync(
            new CompletedDownloadImportRequest(
                request,
                operation,
                location.SourcePath,
                kind),
            cancellationToken);
        await RecordAsync(operation, location, result, nowUtc, cancellationToken);
        return result;
    }

    /// <summary>Records that waiting for the files timed out, so the operation shows why nothing was imported.</summary>
    public Task RecordGaveUpAsync(
        OperationSnapshot operation,
        string message,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        WriteAsync(
            operation,
            previous => new DownloadImportDetails(
                DownloadImportState.GaveUp,
                message,
                nowUtc,
                previous?.ReportedPath,
                previous?.LocalPath,
                previous?.Destination,
                previous?.Mode),
            cancellationToken);

    /// <summary>
    /// Imports finished manual downloads (no request) of every media type with an importer.
    /// Each is imported once: a completed or rejected import is recorded on the operation and
    /// never repeated; a download whose files stay unavailable gives up after
    /// <see cref="CompletedImportTimeout"/>. Returns how many downloads were handled.
    /// </summary>
    public async Task<int> ImportManualDownloadsAsync(
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var store = new OperationStore(db);
        var finished = await store.ListAsync(
            new OperationListFilter(
                Status: OperationStatus.Succeeded,
                Kind: ManualDownloadOperationKind,
                Limit: 100),
            cancellationToken);

        var handled = 0;
        foreach (var operation in finished.OrderBy(item => item.FinishedAtUtc ?? item.UpdatedAtUtc))
        {
            if (handled >= MaxManualImportsPerPass)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!DownloadOperationDetails.TryParse(operation.Details, out var details) ||
                details is null ||
                !dispatcher.Supports(details.MediaKind) ||
                details.Import?.State is DownloadImportState.Completed or DownloadImportState.Rejected or DownloadImportState.GaveUp ||
                string.IsNullOrWhiteSpace(operation.ExternalId))
            {
                continue;
            }

            if (await requests.FindByOperationAsync(operation.Id, cancellationToken) is not null)
            {
                // Request-backed downloads belong to Wanted.
                continue;
            }

            var finishedAt = operation.FinishedAtUtc ?? operation.UpdatedAtUtc;
            if (nowUtc - finishedAt >= CompletedImportTimeout)
            {
                await RecordGaveUpAsync(
                    operation,
                    $"{details.Import?.Result ?? "The completed download could not be imported."} Gave up importing {CompletedImportTimeout.TotalHours:0} hours after the download finished.",
                    nowUtc,
                    cancellationToken);
                handled++;
                continue;
            }

            var result = await ImportAsync(
                operation,
                details.MediaKind,
                request: null,
                nowUtc,
                cancellationToken);
            if (result.Disposition != CompletedDownloadImportDisposition.RetryLater)
            {
                logger.LogInformation(
                    "Manual {MediaKind} download {OperationId} import finished: {Result}",
                    details.MediaKind,
                    operation.Id,
                    result.Message);
            }

            handled++;
        }

        return handled;
    }

    private Task RecordAsync(
        OperationSnapshot operation,
        CompletedDownloadLocation location,
        CompletedDownloadImportResult result,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        WriteAsync(
            operation,
            previous => new DownloadImportDetails(
                result.Disposition switch
                {
                    CompletedDownloadImportDisposition.Completed => DownloadImportState.Completed,
                    CompletedDownloadImportDisposition.RejectedRelease => DownloadImportState.Rejected,
                    _ => DownloadImportState.Waiting
                },
                result.Message,
                nowUtc,
                location.ReportedPath ?? previous?.ReportedPath,
                location.Resolved ? location.SourcePath : previous?.LocalPath,
                result.Placement?.Destination ?? previous?.Destination,
                result.Placement is { } placement ? placement.Mode : previous?.Mode),
            cancellationToken);

    private async Task WriteAsync(
        OperationSnapshot operation,
        Func<DownloadImportDetails?, DownloadImportDetails> build,
        CancellationToken cancellationToken)
    {
        // Downloads submitted before routing details existed have nowhere to record the import.
        var store = new OperationStore(db);
        var current = await store.GetAsync(operation.Id, cancellationToken) ?? operation;
        if (!DownloadOperationDetails.TryParse(current.Details, out var details) || details is null)
        {
            return;
        }

        var previous = details.Import;
        var next = build(previous);
        await store.SetDetailsAsync(
            operation.Id,
            (details with { Import = next }).Serialize(),
            cancellationToken);

        // The log keeps each change of state or result once, not every retry of the same wait.
        if (previous is null || previous.State != next.State || previous.Result != next.Result)
        {
            await store.AppendLogAsync(
                operation.Id,
                next.State switch
                {
                    DownloadImportState.Completed => OperationLogLevel.Information,
                    DownloadImportState.Waiting => OperationLogLevel.Information,
                    _ => OperationLogLevel.Warning
                },
                "Import",
                next.Result,
                cancellationToken);
        }
    }
}
