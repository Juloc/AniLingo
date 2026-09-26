using AniLingo.Web.Data;
using AniLingo.Web.Features.Operations;

namespace AniLingo.Web.Features.Acquisition.Sabnzbd;

/// <summary>What a SABnzbd submission is and how it appears in Operations.</summary>
public sealed record SabnzbdSubmission(
    string OperationKind,
    string Title,
    string Subject,
    string? ProfileId,
    SabnzbdPurpose Purpose,
    string? JobName = null);

public sealed record SabnzbdSubmissionOutcome(
    bool Accepted,
    Guid OperationId,
    string? NzoId,
    string Message);

public sealed record SabnzbdActionOutcome(
    bool Success,
    string Message);

/// <summary>
/// The one path that sends work to SABnzbd for Books and Anime. Each job
/// is a canonical Operation whose external reference is the SABnzbd
/// <c>nzo_id</c>; the SABnzbd operation monitor projects
/// queue/history state onto it.
/// </summary>
public sealed class SabnzbdDownloadService(
    ISabnzbdClient client,
    SabnzbdConnectionResolver connections,
    SabnzbdAcquisitionStore acquisitions,
    AppDbContext db)
{
    public const string OperationCategory = "External downloads";

    public static bool IsSabnzbdOperation(OperationSnapshot operation) =>
        string.Equals(
            operation.ExternalProvider,
            SabnzbdClient.ProviderId,
            StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(operation.ExternalId);

    public static Uri ParseNzbUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Enter a valid HTTP or HTTPS NZB URL.");
        }

        return uri;
    }

    public Task<SabnzbdSubmissionOutcome> SubmitUrlAsync(
        SabnzbdSubmission submission,
        Uri nzbUrl,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nzbUrl);

        return SubmitAsync(
            submission,
            "Submitting download to SABnzbd.",
            (connection, token) => client.GrabAsync(
                connection,
                new SabnzbdGrabRequest(
                    nzbUrl,
                    submission.JobName,
                    connection.Settings.CategoryFor(submission.Purpose)),
                token),
            cancellationToken);
    }

    public Task<SabnzbdSubmissionOutcome> SubmitFileAsync(
        SabnzbdSubmission submission,
        Stream nzb,
        string fileName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nzb);
        if (string.IsNullOrWhiteSpace(fileName)
            || !fileName.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Only .nzb files can be sent to SABnzbd.");
        }

        return SubmitAsync(
            submission,
            "Submitting NZB to SABnzbd.",
            (connection, token) => client.AddFileAsync(
                connection,
                nzb,
                fileName,
                connection.Settings.CategoryFor(submission.Purpose),
                token),
            cancellationToken);
    }

    public async Task<SabnzbdActionOutcome> CancelAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var store = new OperationStore(db);
        var operation = await store.GetAsync(operationId, cancellationToken);
        if (operation is null || !IsSabnzbdOperation(operation))
        {
            return new SabnzbdActionOutcome(false, "This operation is not a SABnzbd download.");
        }

        if (!operation.IsActive)
        {
            return new SabnzbdActionOutcome(false, "This download is no longer active.");
        }

        var connection = await connections.RequireConnectionAsync(cancellationToken);
        var nzoId = operation.ExternalId!;

        SabnzbdActionResult queue;
        SabnzbdActionResult history;
        try
        {
            // A job is either still queued or already in post-processing
            // history; deleting from both covers either stage.
            queue = await client.CancelAsync(connection, nzoId, deleteFiles: true, cancellationToken);
            history = await client.DeleteHistoryAsync(connection, nzoId, deleteFiles: true, cancellationToken);
        }
        catch (Exception exception) when (IsTransportFailure(exception))
        {
            return new SabnzbdActionOutcome(false, "SABnzbd could not cancel the download: " + exception.Message);
        }

        if (!queue.Success && !history.Success)
        {
            return new SabnzbdActionOutcome(
                false,
                "SABnzbd could not cancel the download: " + (queue.Error ?? history.Error));
        }

        await store.MarkCancelledAsync(
            operation.Id,
            "Cancelled in SABnzbd by the owner.",
            CancellationToken.None);
        return new SabnzbdActionOutcome(true, "Download cancelled in SABnzbd.");
    }

    public async Task<SabnzbdActionOutcome> RetryAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var store = new OperationStore(db);
        var operation = await store.GetAsync(operationId, cancellationToken);
        if (operation is null || !IsSabnzbdOperation(operation))
        {
            return new SabnzbdActionOutcome(false, "This operation is not a SABnzbd download.");
        }

        if (!operation.Retryable
            || operation.Status is not (OperationStatus.Failed or OperationStatus.Interrupted))
        {
            return new SabnzbdActionOutcome(false, "Only failed SABnzbd downloads can be retried.");
        }

        var relation = await acquisitions.FindByOperationAsync(operation.Id, cancellationToken);
        if (relation is { } related
            && related.Acquisition.LatestAttempt?.OperationId != operation.Id)
        {
            return new SabnzbdActionOutcome(
                false,
                "A newer release already replaced this download for the same episodes.");
        }

        var connection = await connections.RequireConnectionAsync(cancellationToken);

        SabnzbdActionResult result;
        try
        {
            result = await client.RetryAsync(connection, operation.ExternalId!, cancellationToken);
        }
        catch (Exception exception) when (IsTransportFailure(exception))
        {
            return new SabnzbdActionOutcome(false, "SABnzbd could not retry the download: " + exception.Message);
        }

        if (!result.Success)
        {
            return new SabnzbdActionOutcome(
                false,
                "SABnzbd could not retry the download: " + (result.Error ?? "unknown error"));
        }

        if (!await store.PrepareRetryAsync(operation.Id, cancellationToken))
        {
            return new SabnzbdActionOutcome(false, "The operation could not be requeued.");
        }

        await store.SetExternalReferenceAsync(
            operation.Id,
            SabnzbdClient.ProviderId,
            result.NewNzoId ?? operation.ExternalId!,
            cancellationToken);
        await store.MarkRunningAsync(operation.Id, cancellationToken);
        await store.ReportProgressAsync(
            operation.Id,
            0,
            "Retry accepted by SABnzbd; waiting for download progress.",
            cancellationToken: cancellationToken);

        if (relation is { } retried)
        {
            // The owner explicitly asked for this release again.
            await acquisitions.UnblockAsync(retried.Attempt.ReleaseIdentity, cancellationToken);
        }

        return new SabnzbdActionOutcome(true, "Retry queued in SABnzbd.");
    }

    private async Task<SabnzbdSubmissionOutcome> SubmitAsync(
        SabnzbdSubmission submission,
        string submittingMessage,
        Func<SabnzbdConnection, CancellationToken, Task<SabnzbdGrabResult>> submit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        var connection = await connections.RequireConnectionAsync(cancellationToken);

        var store = new OperationStore(db);
        var operationId = await store.CreateAsync(
            new OperationDescriptor(
                submission.OperationKind,
                OperationCategory,
                submission.Title,
                submission.Subject,
                submission.ProfileId,
                OperationLane.Normal,
                IsDownload: true,
                Retryable: true,
                ExternalProvider: SabnzbdClient.ProviderId),
            cancellationToken);

        await store.MarkRunningAsync(operationId, cancellationToken);
        await store.ReportProgressAsync(
            operationId,
            0,
            submittingMessage,
            cancellationToken: cancellationToken);

        SabnzbdGrabResult result;
        try
        {
            result = await submit(connection, cancellationToken);
        }
        catch (Exception exception) when (IsTransportFailure(exception))
        {
            var error = "SABnzbd could not be reached: " + exception.Message;
            await store.MarkFailedAsync(operationId, error, CancellationToken.None);
            return new SabnzbdSubmissionOutcome(false, operationId, null, error);
        }

        if (!result.Success)
        {
            var error = result.Error ?? "SABnzbd rejected the request.";
            await store.MarkFailedAsync(operationId, error, CancellationToken.None);
            return new SabnzbdSubmissionOutcome(false, operationId, null, error);
        }

        var nzoId = result.NzoIds.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(nzoId))
        {
            const string untracked =
                "Sent to SABnzbd, but SABnzbd returned no job ID, so live progress is unavailable.";
            await store.MarkSucceededAsync(operationId, untracked, CancellationToken.None);
            return new SabnzbdSubmissionOutcome(true, operationId, null, untracked);
        }

        await store.SetExternalReferenceAsync(
            operationId,
            SabnzbdClient.ProviderId,
            nzoId,
            cancellationToken);
        await store.ReportProgressAsync(
            operationId,
            0,
            "Accepted by SABnzbd; waiting for download progress.",
            cancellationToken: cancellationToken);

        return new SabnzbdSubmissionOutcome(
            true,
            operationId,
            nzoId,
            "SABnzbd download submitted. Track it under Admin → Operations → Downloads.");
    }

    private static bool IsTransportFailure(Exception exception) =>
        exception is HttpRequestException
            or TaskCanceledException
            or SabnzbdException
            or ArgumentException;
}
