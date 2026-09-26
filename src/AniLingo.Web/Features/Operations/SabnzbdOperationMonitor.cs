using AniLingo.Web.Data;
using AniLingo.Web.Features.Acquisition.DownloadClients;
using AniLingo.Web.Features.Acquisition.Import;
using AniLingo.Web.Features.Acquisition.Sabnzbd;
using AniLingo.Web.Features.Books;

namespace AniLingo.Web.Features.Operations;

public sealed record SabnzbdProjectedFailure(
    OperationSnapshot Operation,
    SabnzbdFailureKind FailureKind,
    string Reason);

public sealed record SabnzbdProjectionResult(
    IReadOnlyList<OperationSnapshot> Completed,
    IReadOnlyList<SabnzbdProjectedFailure> Failed);

/// <summary>
/// Projects SABnzbd queue/history state onto the canonical Operations that
/// carry SABnzbd jobs. Operations are the only place job lifecycle,
/// progress and ETA are stored.
/// </summary>
public static class SabnzbdOperationProjector
{
    public static readonly TimeSpan MissingJobTimeout = TimeSpan.FromMinutes(15);

    public static async Task<SabnzbdProjectionResult> ApplyAsync(
        OperationStore store,
        IReadOnlyList<OperationSnapshot> operations,
        SabnzbdQueueSnapshot queue,
        SabnzbdHistorySnapshot history,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var completed = new List<OperationSnapshot>();
        var failed = new List<SabnzbdProjectedFailure>();

        var queueById = queue.Jobs
            .GroupBy(job => job.NzoId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var historyById = history.Jobs
            .GroupBy(job => job.NzoId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var operation in operations)
        {
            if (string.IsNullOrWhiteSpace(operation.ExternalId))
            {
                continue;
            }

            if (queueById.TryGetValue(operation.ExternalId, out var queued))
            {
                await EnsureRunningAsync(store, operation, cancellationToken);

                long? completedBytes = queued.SizeBytes is { } total && queued.SizeLeftBytes is { } left
                    ? Math.Clamp(total - left, 0, total)
                    : null;
                DateTime? eta = queued.TimeLeft is { } remaining && remaining > TimeSpan.Zero
                    ? nowUtc.Add(remaining)
                    : null;

                await store.ReportProgressAsync(
                    operation.Id,
                    queued.Percentage is { } percent ? (int)Math.Round(percent) : null,
                    queue.Paused
                        ? "SABnzbd queue is paused."
                        : $"SABnzbd: {queued.Status ?? "Queued"}.",
                    completedBytes,
                    queued.SizeBytes,
                    // SABnzbd only reports the overall queue speed.
                    queue.Jobs.Count == 1 ? queue.BytesPerSecond : null,
                    eta,
                    cancellationToken);
                continue;
            }

            if (historyById.TryGetValue(operation.ExternalId, out var finished))
            {
                if (finished.IsCompleted)
                {
                    await store.MarkSucceededAsync(
                        operation.Id,
                        "SABnzbd download and post-processing completed.",
                        cancellationToken);
                    completed.Add(operation);
                    continue;
                }

                if (finished.IsFailed)
                {
                    var kind = finished.FailureKind == SabnzbdFailureKind.None
                        ? SabnzbdFailureKind.Unknown
                        : finished.FailureKind;
                    var reason = SabnzbdFailureDescriptions.Describe(kind, finished.FailureMessage);
                    await store.MarkFailedAsync(operation.Id, reason, cancellationToken);
                    failed.Add(new SabnzbdProjectedFailure(operation, kind, reason));
                    continue;
                }

                await EnsureRunningAsync(store, operation, cancellationToken);
                await store.ReportProgressAsync(
                    operation.Id,
                    Math.Max(operation.ProgressPercent ?? 0, 99),
                    $"SABnzbd post-processing: {finished.Status ?? "processing"}.",
                    finished.SizeBytes,
                    finished.SizeBytes,
                    bytesPerSecond: null,
                    etaUtc: null,
                    cancellationToken);
                continue;
            }

            if (nowUtc - operation.UpdatedAtUtc >= MissingJobTimeout)
            {
                const string missing = "SABnzbd job no longer appears in queue or history.";
                await store.MarkFailedAsync(operation.Id, missing, cancellationToken);
                failed.Add(new SabnzbdProjectedFailure(operation, SabnzbdFailureKind.Unknown, missing));
            }
        }

        return new SabnzbdProjectionResult(completed, failed);
    }

    private static async Task EnsureRunningAsync(
        OperationStore store,
        OperationSnapshot operation,
        CancellationToken cancellationToken)
    {
        if (operation.Status == OperationStatus.Queued)
        {
            await store.MarkRunningAsync(operation.Id, cancellationToken);
        }
    }
}

/// <summary>
/// The one SABnzbd monitor loop for Books and Anime downloads. It resumes
/// after a restart from the persisted external references on Operations.
/// </summary>
public sealed class SabnzbdOperationMonitorService(
    IServiceScopeFactory scopeFactory,
    DownloadClientStore downloadClients,
    ILogger<SabnzbdOperationMonitorService> logger) : BackgroundService
{
    private static readonly TimeSpan ActivePollInterval =
        TimeSpan.FromSeconds(3);
    private static readonly TimeSpan IdlePollInterval =
        TimeSpan.FromSeconds(12);

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        await Task.Yield();
        await RecoverAcquisitionsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var hasActiveJobs = false;

            try
            {
                hasActiveJobs = await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Could not refresh SABnzbd operation status.");
            }

            try
            {
                await Task.Delay(
                    hasActiveJobs
                        ? ActivePollInterval
                        : IdlePollInterval,
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task<bool> PollOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var store = new OperationStore(services.GetRequiredService<AppDbContext>());
        var operations = await store.ListActiveExternalAsync(
            SabnzbdClient.ProviderId,
            cancellationToken);

        if (operations.Count == 0)
        {
            return false;
        }

        var entry = (await downloadClients.LoadAllAsync(cancellationToken))
            .Where(item => item.Type == DownloadClientType.Sabnzbd && item.Enabled)
            .OrderBy(item => item.Priority)
            .FirstOrDefault();
        if (entry is null)
        {
            logger.LogWarning(
                "{Count} SABnzbd downloads are active but no SABnzbd download client is configured.",
                operations.Count);
            return true;
        }

        var connection = SabnzbdDownloadClient.ToConnection(entry);
        var client = services.GetRequiredService<ISabnzbdClient>();
        var queue = await client.GetQueueAsync(connection, cancellationToken);
        var history = await client.GetHistoryAsync(
            connection,
            operations.Select(operation => operation.ExternalId!).ToArray(),
            cancellationToken);

        var result = await SabnzbdOperationProjector.ApplyAsync(
            store,
            operations,
            queue,
            history,
            DateTime.UtcNow,
            cancellationToken);

        await ImportCompletedBookDownloadsAsync(services, result.Completed, cancellationToken);
        await ImportCompletedAnimeDownloadsAsync(services, result.Completed, history, cancellationToken);
        await ContinueFailedAnimeAcquisitionsAsync(services, result.Failed, cancellationToken);
        return true;
    }

    private async Task ImportCompletedAnimeDownloadsAsync(
        IServiceProvider services,
        IReadOnlyList<OperationSnapshot> completed,
        SabnzbdHistorySnapshot history,
        CancellationToken cancellationToken)
    {
        foreach (var operation in completed.Where(AnimeImportExecutor.IsAnimeDownload))
        {
            try
            {
                var storagePath = history.Jobs
                    .FirstOrDefault(job => job.NzoId == operation.ExternalId)?
                    .StoragePath;
                await services.GetRequiredService<AnimeImportExecutor>()
                    .ImportCompletedAsync(operation, storagePath, cancellationToken);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                    or InvalidDataException
                    or IOException
                    or UnauthorizedAccessException)
            {
                // The import executor recovers unfinished imports on the next start.
                logger.LogWarning(
                    exception,
                    "Could not import the completed anime download of operation {OperationId}.",
                    operation.Id);
            }
        }
    }

    private async Task RecoverAcquisitionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var advanced = await scope.ServiceProvider
                .GetRequiredService<SabnzbdAcquisitionService>()
                .RecoverAsync(cancellationToken);
            if (advanced > 0)
            {
                logger.LogInformation(
                    "Resumed {Count} anime acquisitions whose SABnzbd download failed before the restart.",
                    advanced);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not recover SABnzbd anime acquisitions after startup.");
        }
    }

    private async Task ContinueFailedAnimeAcquisitionsAsync(
        IServiceProvider services,
        IReadOnlyList<SabnzbdProjectedFailure> failures,
        CancellationToken cancellationToken)
    {
        var acquisitions = services.GetRequiredService<SabnzbdAcquisitionService>();
        foreach (var failure in failures.Where(failure =>
                     failure.Operation.Kind == SabnzbdAcquisitionService.OperationKind))
        {
            try
            {
                await acquisitions.HandleFailedAsync(
                    failure.Operation.Id,
                    failure.FailureKind,
                    failure.Reason,
                    cancellationToken);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                    or InvalidDataException
                    or IOException)
            {
                // RecoverAsync retries this acquisition on the next start.
                logger.LogWarning(
                    exception,
                    "Could not continue the anime acquisition after SABnzbd operation {OperationId} failed.",
                    failure.Operation.Id);
            }
        }
    }

    private async Task ImportCompletedBookDownloadsAsync(
        IServiceProvider services,
        IReadOnlyList<OperationSnapshot> completed,
        CancellationToken cancellationToken)
    {
        try
        {
            await BookInboxImport.ImportAfterDownloadsAsync(
                services,
                completed,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or IOException
                or UnauthorizedAccessException)
        {
            // The runner already recorded the failed import operation.
            logger.LogWarning(
                exception,
                "Could not import the Books inbox after a completed SABnzbd download.");
        }
    }
}
