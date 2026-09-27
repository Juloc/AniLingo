using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.DownloadClients;
using Jularr.Web.Features.Operations;

namespace Jularr.Web.Features.Acquisition.Import;

/// <summary>
/// Result of handing one completed external download to the canonical media importer.
/// RetryLater is for infrastructure/storage problems and must never trigger another grab.
/// RejectedRelease means the downloaded package itself is unsuitable and Wanted may try
/// the next release.
/// </summary>
public enum CompletedDownloadImportDisposition
{
    Completed,
    RetryLater,
    RejectedRelease
}

public sealed record CompletedDownloadImportResult(
    CompletedDownloadImportDisposition Disposition,
    string Message,
    string? ResultUrl = null)
{
    public static CompletedDownloadImportResult Completed(
        string message,
        string resultUrl) =>
        new(CompletedDownloadImportDisposition.Completed, message, resultUrl);

    public static CompletedDownloadImportResult RetryLater(string message) =>
        new(CompletedDownloadImportDisposition.RetryLater, message);

    public static CompletedDownloadImportResult RejectRelease(string message) =>
        new(CompletedDownloadImportDisposition.RejectedRelease, message);
}

public sealed record CompletedDownloadImportRequest(
    AcquisitionRequest Request,
    OperationSnapshot Operation,
    string SourcePath);

/// <summary>
/// One media-specific adapter behind the single completed-download dispatcher.
/// Adapters normalize/import media; they do not poll SABnzbd or own retry scheduling.
/// </summary>
public interface ICompletedDownloadImportAdapter
{
    MediaAcquisitionKind Kind { get; }

    Task<CompletedDownloadImportResult> ImportAsync(
        CompletedDownloadImportRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Canonical completed-download dispatch point. Download monitoring resolves lifecycle state;
/// this class only selects the importer for the request's media type.
/// </summary>
public sealed class CompletedDownloadDispatcher(
    IEnumerable<ICompletedDownloadImportAdapter> adapters)
{
    private readonly IReadOnlyDictionary<MediaAcquisitionKind, ICompletedDownloadImportAdapter> adaptersByKind =
        adapters
            .GroupBy(adapter => adapter.Kind)
            .ToDictionary(
                group => group.Key,
                group => group.Count() == 1
                    ? group.Single()
                    : throw new InvalidOperationException(
                        $"More than one completed-download adapter is registered for {group.Key}."));

    public Task<CompletedDownloadImportResult> DispatchAsync(
        CompletedDownloadImportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!adaptersByKind.TryGetValue(request.Request.Kind, out var adapter))
        {
            return Task.FromResult(
                CompletedDownloadImportResult.RetryLater(
                    $"No completed-download importer is registered for {request.Request.Kind}."));
        }

        return adapter.ImportAsync(request, cancellationToken);
    }
}

public sealed record CompletedDownloadLocation(
    bool Resolved,
    string? SourcePath,
    string Message);

/// <summary>
/// Resolves the completed path from the exact download-client connection recorded at submit time,
/// then applies the one canonical acquisition remote-path mapping before import.
/// </summary>
public sealed class CompletedDownloadLocationResolver(
    DownloadClientStore downloadClients,
    IDownloadClient client,
    AnimeImportSettingsStore importSettings)
{
    public async Task<CompletedDownloadLocation> ResolveAsync(
        OperationSnapshot operation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(operation.ExternalId))
        {
            return new CompletedDownloadLocation(
                false,
                null,
                "The completed download has no external job id.");
        }

        var entries = await downloadClients.LoadAllAsync(cancellationToken);
        DownloadClientEntry? entry = null;

        if (DownloadOperationDetails.TryParse(operation.Details, out var details) &&
            details is not null)
        {
            entry = entries.FirstOrDefault(candidate =>
                candidate.Id == details.ClientEntryId);
            if (entry is null)
            {
                return new CompletedDownloadLocation(
                    false,
                    null,
                    $"The download client used by this job ({details.ClientEntryId}) is no longer configured.");
            }
        }
        else
        {
            // Legacy operations created before the selected client id was persisted.
            entry = entries
                .Where(candidate =>
                    candidate.Type == DownloadClientType.Sabnzbd &&
                    candidate.Enabled)
                .OrderBy(candidate => candidate.Priority)
                .FirstOrDefault();

            if (entry is null)
            {
                return new CompletedDownloadLocation(
                    false,
                    null,
                    "No SABnzbd connection is available to resolve the completed path.");
            }
        }

        try
        {
            var statuses = await client.GetStatusAsync(
                entry,
                [operation.ExternalId],
                cancellationToken);
            var completed = statuses.FirstOrDefault(status =>
                status.ExternalId == operation.ExternalId &&
                status.IsCompleted);

            if (completed?.StoragePath is not { Length: > 0 } reportedPath)
            {
                return new CompletedDownloadLocation(
                    false,
                    null,
                    "SABnzbd has not exposed the completed storage path yet.");
            }

            var settings = await importSettings.LoadAsync(cancellationToken);
            var localPath = settings.TranslatePath(reportedPath.Trim());
            return new CompletedDownloadLocation(
                true,
                localPath,
                string.Equals(localPath, reportedPath, StringComparison.Ordinal)
                    ? "Completed path resolved."
                    : "Completed path resolved through the configured remote-path mapping.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is DownloadClientException or
            HttpRequestException or
            TaskCanceledException or
            InvalidOperationException or
            InvalidDataException)
        {
            return new CompletedDownloadLocation(
                false,
                null,
                $"Completed path is temporarily unavailable: {exception.Message}");
        }
    }
}
