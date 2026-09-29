using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Sabnzbd;
using Jularr.Web.Features.Operations;

namespace Jularr.Web.Features.Acquisition.Import;

/// <summary>
/// Restart recovery of Anime imports, on top of the shared import step
/// (<see cref="CompletedDownloadImportService"/>): resumes imports that were interrupted or
/// deferred, and imports completed anime downloads the SABnzbd monitor missed because Jularr was
/// not running. A download whose files can never be located is failed after the shared
/// <see cref="CompletedDownloadImportService.CompletedImportTimeout"/>, so it shows up on the
/// acquisition overview instead of waiting for ever.
/// </summary>
public sealed class AnimeImportRecovery(
    CompletedDownloadImportService imports,
    AnimeImportExecutor executor,
    AnimeImportStore records,
    AppDbContext db)
{
    public Task<int> RecoverAsync(CancellationToken cancellationToken) =>
        RecoverAsync(DateTime.UtcNow, cancellationToken);

    /// <summary>Returns how many imports were resumed or started.</summary>
    public async Task<int> RecoverAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var recovered = 0;
        var operations = new OperationStore(db);
        var state = await records.LoadAsync(cancellationToken);

        // Interrupted or deferred imports continue from the mapped path they recorded, which stays
        // readable after the download client dropped the job from its history.
        foreach (var record in state.Imports.Where(record => record.Status == AnimeImportStatus.Importing).ToArray())
        {
            var download = await operations.GetAsync(record.DownloadOperationId, cancellationToken);
            if (download is null)
            {
                await executor.FailAsync(record, "The download operation no longer exists.", cancellationToken);
                continue;
            }

            if (string.IsNullOrWhiteSpace(record.DownloadPath))
            {
                await executor.FailUnavailableAsync(download, AnimeImportExecutor.NoStoragePathMessage, cancellationToken);
            }
            else
            {
                await imports.ImportAtAsync(
                    download,
                    MediaAcquisitionKind.Anime,
                    request: null,
                    new CompletedDownloadLocation(
                        true,
                        record.DownloadPath,
                        "Resuming the interrupted import from its recorded path."),
                    nowUtc,
                    cancellationToken);
            }

            recovered++;
        }

        var known = state.Imports.Select(record => record.DownloadOperationId).ToHashSet();
        var since = nowUtc - AnimeImportExecutor.RecoveryWindow;
        var completed = (await operations.ListAsync(
                new OperationListFilter(View: "history", Category: SabnzbdDownloadService.OperationCategory, Limit: 200),
                cancellationToken))
            .Where(operation =>
                AnimeImportExecutor.IsAnimeDownload(operation) &&
                operation.Status == OperationStatus.Succeeded &&
                !string.IsNullOrWhiteSpace(operation.ExternalId) &&
                operation.FinishedAtUtc is { } finished && finished >= since &&
                !known.Contains(operation.Id))
            .ToArray();

        foreach (var operation in completed)
        {
            var result = await imports.ImportAsync(
                operation,
                MediaAcquisitionKind.Anime,
                request: null,
                nowUtc,
                cancellationToken);
            recovered++;

            // No import record means the shared step could not locate the files (the download
            // client is unreachable, no longer lists the job or reports no path). Keep trying
            // until the files are overdue, then end it with a visible reason.
            var finishedAt = operation.FinishedAtUtc ?? operation.UpdatedAtUtc;
            if (result.Disposition == CompletedDownloadImportDisposition.RetryLater &&
                nowUtc - finishedAt >= CompletedDownloadImportService.CompletedImportTimeout &&
                await records.FindByDownloadAsync(operation.Id, cancellationToken) is null)
            {
                await executor.FailUnavailableAsync(
                    operation,
                    $"{result.Message} Gave up importing {CompletedDownloadImportService.CompletedImportTimeout.TotalHours:0} hours after the download finished.",
                    cancellationToken);
            }
        }

        return recovered;
    }
}
