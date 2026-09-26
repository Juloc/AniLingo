using AniLingo.Web.Data;
using AniLingo.Web.Features.Acquisition.DownloadClients;
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
/// The one path that sends work to SABnzbd for Books and Anime. Submissions
/// go through the canonical <see cref="DownloadClientSubmissionService"/>
/// (the highest-priority enabled, healthy usenet client, with failover),
/// so SABnzbd here is one download client implementation, not a special
/// case; Operations remain the single status store and the SABnzbd
/// operation monitor still projects queue/history onto them.
/// </summary>
public sealed class SabnzbdDownloadService(
    DownloadClientSubmissionService submissions,
    DownloadClientStore clientStore,
    ISabnzbdClient client,
    SabnzbdAcquisitionStore acquisitions,
    AppDbContext db)
{
    public const string OperationCategory = DownloadClientSubmissionService.OperationCategory;

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

    public async Task<SabnzbdSubmissionOutcome> SubmitUrlAsync(
        SabnzbdSubmission submission,
        Uri nzbUrl,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(nzbUrl);

        var outcome = await submissions.SubmitAsync(
            new DownloadSubmissionSpec(
                submission.OperationKind,
                submission.Title,
                submission.Subject,
                submission.ProfileId,
                DownloadProtocol.Usenet,
                nzbUrl,
                MagnetUri: null,
                submission.JobName,
                IsBooks: submission.Purpose == SabnzbdPurpose.Books),
            cancellationToken);

        return ToSubmissionOutcome(outcome);
    }

    public async Task<SabnzbdSubmissionOutcome> SubmitFileAsync(
        SabnzbdSubmission submission,
        Stream nzb,
        string fileName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(nzb);
        if (string.IsNullOrWhiteSpace(fileName)
            || !fileName.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Only .nzb files can be sent to SABnzbd.");
        }

        var outcome = await submissions.SubmitAsync(
            new DownloadSubmissionSpec(
                submission.OperationKind,
                submission.Title,
                submission.Subject,
                submission.ProfileId,
                DownloadProtocol.Usenet,
                Url: null,
                MagnetUri: null,
                submission.JobName ?? fileName,
                IsBooks: submission.Purpose == SabnzbdPurpose.Books,
                File: nzb,
                FileName: fileName),
            cancellationToken);

        return ToSubmissionOutcome(outcome);
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

        var entry = await RequireSabnzbdEntryAsync(cancellationToken);
        var nzoId = operation.ExternalId!;

        bool cancelled;
        try
        {
            cancelled = await new SabnzbdDownloadClient(client)
                .DeleteAsync(entry, nzoId, deleteFiles: true, cancellationToken);
        }
        catch (Exception exception) when (IsTransportFailure(exception))
        {
            return new SabnzbdActionOutcome(false, "SABnzbd could not cancel the download: " + exception.Message);
        }

        if (!cancelled)
        {
            return new SabnzbdActionOutcome(false, "SABnzbd could not cancel the download.");
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

        var entry = await RequireSabnzbdEntryAsync(cancellationToken);
        var connection = SabnzbdDownloadClient.ToConnection(entry);

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

    private async Task<DownloadClientEntry> RequireSabnzbdEntryAsync(CancellationToken cancellationToken)
    {
        var entry = (await clientStore.LoadAllAsync(cancellationToken))
            .Where(item => item.Type == DownloadClientType.Sabnzbd && item.Enabled)
            .OrderBy(item => item.Priority)
            .FirstOrDefault();

        return entry
            ?? throw new InvalidOperationException(
                "SABnzbd is not configured. Configure it under Settings → Download Clients.");
    }

    private static SabnzbdSubmissionOutcome ToSubmissionOutcome(DownloadSubmissionOutcome outcome) =>
        new(outcome.Accepted, outcome.OperationId, outcome.ExternalId, outcome.Message);

    private static bool IsTransportFailure(Exception exception) =>
        exception is HttpRequestException
            or TaskCanceledException
            or SabnzbdException
            or DownloadClientException
            or ArgumentException;
}
