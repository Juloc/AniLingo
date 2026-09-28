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

/// <summary>
/// Where an importer put the media and how: the library folder or Jularr store it went to,
/// and the import mode used (null: the files are read in place and stay where they are).
/// </summary>
public sealed record CompletedDownloadPlacement(
    string Destination,
    ImportMode? Mode);

/// <summary>
/// A durable, user-visible stage reported by a media importer while it processes a completed
/// download. The shared import service writes it to the download operation and its log.
/// </summary>
public enum CompletedDownloadImportPhase
{
    Verifying,
    Importing,
    MatchingMetadata
}

public sealed record CompletedDownloadImportProgress(
    CompletedDownloadImportPhase Phase,
    string Message,
    CompletedDownloadPlacement? Placement = null);

public sealed record CompletedDownloadImportResult(
    CompletedDownloadImportDisposition Disposition,
    string Message,
    string? ResultUrl = null,
    CompletedDownloadPlacement? Placement = null)
{
    public static CompletedDownloadImportResult Completed(
        string message,
        string resultUrl,
        CompletedDownloadPlacement? placement = null) =>
        new(CompletedDownloadImportDisposition.Completed, message, resultUrl, placement);

    public static CompletedDownloadImportResult RetryLater(
        string message,
        CompletedDownloadPlacement? placement = null) =>
        new(CompletedDownloadImportDisposition.RetryLater, message, Placement: placement);

    public static CompletedDownloadImportResult RejectRelease(
        string message,
        CompletedDownloadPlacement? placement = null) =>
        new(CompletedDownloadImportDisposition.RejectedRelease, message, Placement: placement);
}

/// <summary>
/// One completed download (or one inbox entry) to import. <see cref="Request"/> is the
/// acquisition request it answers and <see cref="Operation"/> the download operation; both are
/// absent for content Jularr did not request, such as a manual NZB or a file in an inbox
/// folder, which then names its media type in <c>MediaKind</c>.
/// </summary>
public sealed record CompletedDownloadImportRequest(
    AcquisitionRequest? Request,
    OperationSnapshot? Operation,
    string SourcePath,
    MediaAcquisitionKind? MediaKind = null,
    Func<CompletedDownloadImportProgress, Task>? ProgressReporter = null)
{
    public MediaAcquisitionKind Kind =>
        MediaKind ?? Request?.Kind ??
        throw new InvalidOperationException("A completed-download import needs a request or a media type.");

    public Task ReportProgressAsync(
        CompletedDownloadImportPhase phase,
        string message,
        CompletedDownloadPlacement? placement = null) =>
        ProgressReporter?.Invoke(new CompletedDownloadImportProgress(phase, message, placement))
        ?? Task.CompletedTask;
}

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

/// <summary>What one scan of a media type's inbox folder imported.</summary>
public sealed record MediaInboxImportResult(
    int Imported,
    string Message,
    string? ResultUrl = null);

/// <summary>
/// The inbox side of a completed-download adapter: imports content that appeared in the media
/// type's configured inbox folder without a Jularr download, with the same importer.
/// <paramref name="excludedFolders"/> are other media types' inbox folders nested inside this
/// one, which the scan must leave alone.
/// </summary>
public interface IMediaInboxImportAdapter
{
    MediaAcquisitionKind Kind { get; }

    Task<MediaInboxImportResult> ImportInboxAsync(
        string inboxRoot,
        IReadOnlyCollection<string> excludedFolders,
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

    public bool Supports(MediaAcquisitionKind kind) =>
        adaptersByKind.ContainsKey(kind);

    public Task<CompletedDownloadImportResult> DispatchAsync(
        CompletedDownloadImportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!adaptersByKind.TryGetValue(request.Kind, out var adapter))
        {
            return Task.FromResult(
                CompletedDownloadImportResult.RetryLater(
                    $"No completed-download importer is registered for {request.Kind}."));
        }

        return adapter.ImportAsync(request, cancellationToken);
    }
}

/// <summary>
/// Where a completed download is: the path the download client reported and, after the one
/// canonical remote-path mapping, the path Jularr reads (<see cref="SourcePath"/>).
/// </summary>
public sealed record CompletedDownloadLocation(
    bool Resolved,
    string? SourcePath,
    string Message,
    string? ReportedPath = null);

public interface ICompletedDownloadLocationResolver
{
    Task<CompletedDownloadLocation> ResolveAsync(
        OperationSnapshot operation,
        CancellationToken cancellationToken);
}

/// <summary>
/// Resolves the completed path from the exact download-client connection recorded at submit time,
/// then applies the one canonical acquisition remote-path mapping before import.
/// </summary>
public sealed class CompletedDownloadLocationResolver(
    DownloadClientStore downloadClients,
    IDownloadClient client,
    AnimeImportSettingsStore importSettings,
    ILogger<CompletedDownloadLocationResolver> logger)
    : ICompletedDownloadLocationResolver
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
                    "The download client used by this job is no longer configured.");
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
                    : "Completed path resolved through the configured remote-path mapping.",
                reportedPath.Trim());
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
            logger.LogWarning(
                exception,
                "Could not resolve completed path for operation {OperationId}.",
                operation.Id);
            return new CompletedDownloadLocation(
                false,
                null,
                "The completed path is temporarily unavailable.");
        }
    }
}
