using System.Text.Json;
using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.DownloadClients;
using Jularr.Web.Features.Acquisition.Sabnzbd;
using Jularr.Web.Features.Operations;

namespace Jularr.Web.Features.Books;

/// <summary>
/// Keeps book requests moving without the owner: approved requests that found no release are
/// searched again when their backoff is due, a book download that failed in SABnzbd (or held no
/// usable book) goes on with the next release (the dropped one is never sent again), and a
/// request whose download finished while its import was missed — for example across a restart —
/// is imported and closed.
/// </summary>
public sealed class BookRequestSearchService(
    IServiceScopeFactory scopes,
    ILogger<BookRequestSearchService> logger,
    TimeProvider? clock = null) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// A download that finished this long ago without closing its request lost its import; the
    /// SABnzbd monitor imports right after completion, so younger ones are left to it.
    /// </summary>
    public static readonly TimeSpan MissedImportAfter = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
                await RecoverDownloadsAsync(scope.ServiceProvider, now, stoppingToken);
                await SearchDueAsync(scope.ServiceProvider, now, stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Searching waiting book requests failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Searches every approved book request whose next search is due. Returns how many ran.</summary>
    public static async Task<int> SearchDueAsync(IServiceProvider services, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var store = services.GetRequiredService<AcquisitionAccessStore>();
        var service = services.GetRequiredService<AcquisitionRequestService>();
        var due = (await store.ListByStatusAsync(MediaAcquisitionKind.Book, AcquisitionRequestStatus.Approved, cancellationToken))
            .Where(request => BookAcquisitionExecutor.ReadPayload(request).NextSearchUtc is { } next && next <= nowUtc)
            .ToArray();
        foreach (var request in due)
        {
            await service.ContinueAsync(request.Id, cancellationToken);
        }

        return due.Length;
    }

    /// <summary>
    /// Continues the book requests whose SABnzbd download failed: the executor skips releases that
    /// were tried already, so this sends the next best one or waits for a new release.
    /// </summary>
    public static async Task<int> ContinueAfterFailedDownloadsAsync(
        IServiceProvider services,
        IReadOnlyCollection<Guid> failedOperationIds,
        CancellationToken cancellationToken)
    {
        if (failedOperationIds.Count == 0)
        {
            return 0;
        }

        var store = services.GetRequiredService<AcquisitionAccessStore>();
        var operations = new OperationStore(services.GetRequiredService<AppDbContext>());
        var affected = (await store.ListDownloadingAsync(MediaAcquisitionKind.Book, cancellationToken))
            .Where(request => request.OperationId is { } operationId && failedOperationIds.Contains(operationId))
            .ToArray();
        foreach (var request in affected)
        {
            var operation = await operations.GetAsync(request.OperationId!.Value, cancellationToken);
            await ContinueWithProblemAsync(services, request, DownloadFailure(operation), cancellationToken);
        }

        return affected.Length;
    }

    /// <summary>
    /// Continues the book requests whose finished download held no usable book: the release is
    /// already in <see cref="BookRequestPayload.TriedReleases"/>, so the next ranked one is sent
    /// right away, or the request waits for the next search.
    /// </summary>
    public static async Task<int> ContinueAfterUnsuitableDownloadsAsync(
        IServiceProvider services,
        IReadOnlyCollection<Guid> requestIds,
        string reason,
        CancellationToken cancellationToken)
    {
        var store = services.GetRequiredService<AcquisitionAccessStore>();
        var continued = 0;
        foreach (var id in requestIds)
        {
            if (await store.GetAsync(id, cancellationToken) is { } request)
            {
                await ContinueWithProblemAsync(services, request, reason, cancellationToken);
                continued++;
            }
        }

        return continued;
    }

    /// <summary>
    /// Brings "Downloading" book requests back in line with their download after a restart or a
    /// missed poll: a failed or vanished download continues with the next release, a finished one
    /// whose import never ran is imported now (with the storage path SABnzbd reports).
    /// Returns how many requests were moved on.
    /// </summary>
    public static async Task<int> RecoverDownloadsAsync(IServiceProvider services, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var store = services.GetRequiredService<AcquisitionAccessStore>();
        var operations = new OperationStore(services.GetRequiredService<AppDbContext>());
        var failed = new List<Guid>();
        var finished = new List<OperationSnapshot>();
        foreach (var request in await store.ListDownloadingAsync(MediaAcquisitionKind.Book, cancellationToken))
        {
            var operation = request.OperationId is { } operationId
                ? await operations.GetAsync(operationId, cancellationToken)
                : null;
            if (operation is null || operation.Status is OperationStatus.Failed or OperationStatus.Cancelled or OperationStatus.Interrupted)
            {
                await ContinueWithProblemAsync(services, request, DownloadFailure(operation), cancellationToken);
                failed.Add(request.Id);
            }
            else if (operation.Status == OperationStatus.Succeeded && nowUtc - operation.UpdatedAtUtc >= MissedImportAfter)
            {
                finished.Add(operation);
            }
        }

        // Without an answer from SABnzbd the finished ones wait for the next run.
        if (finished.Count > 0 && await StoragePathsAsync(services, finished, cancellationToken) is { } storagePaths)
        {
            await BookInboxImport.ImportAfterDownloadsAsync(services, finished, storagePaths, cancellationToken);
            return failed.Count + finished.Count;
        }

        return failed.Count;
    }

    private static async Task ContinueWithProblemAsync(
        IServiceProvider services,
        AcquisitionRequest request,
        string problem,
        CancellationToken cancellationToken)
    {
        var store = services.GetRequiredService<AcquisitionAccessStore>();
        var payload = BookAcquisitionExecutor.ReadPayload(request) with { LastProblem = problem };
        await store.UpdatePayloadAsync(request.Id, JsonSerializer.Serialize(payload, JsonSerializerOptions.Web), cancellationToken);
        await services.GetRequiredService<AcquisitionRequestService>().ContinueAsync(request.Id, cancellationToken);
    }

    private static string DownloadFailure(OperationSnapshot? operation) =>
        string.IsNullOrWhiteSpace(operation?.Error)
            ? "The download failed."
            : $"The download failed: {operation.Error.Trim().TrimEnd('.')}.";

    /// <summary>
    /// Where SABnzbd put each finished job; a path is unknown when SABnzbd is not configured or
    /// has forgotten the job. Null when SABnzbd could not be asked right now.
    /// </summary>
    private static async Task<IReadOnlyDictionary<Guid, string?>?> StoragePathsAsync(
        IServiceProvider services,
        IReadOnlyList<OperationSnapshot> finished,
        CancellationToken cancellationToken)
    {
        var entry = (await services.GetRequiredService<DownloadClientStore>().LoadAllAsync(cancellationToken))
            .Where(item => item.Type == DownloadClientType.Sabnzbd && item.Enabled)
            .OrderBy(item => item.Priority)
            .FirstOrDefault();
        var ids = finished.Select(operation => operation.ExternalId).OfType<string>().ToArray();
        if (entry is null || ids.Length == 0)
        {
            return finished.ToDictionary(operation => operation.Id, _ => (string?)null);
        }

        try
        {
            var history = await services.GetRequiredService<ISabnzbdClient>()
                .GetHistoryAsync(SabnzbdDownloadClient.ToConnection(entry), ids, cancellationToken);
            return finished.ToDictionary(
                operation => operation.Id,
                operation => history.Jobs.FirstOrDefault(job => job.NzoId == operation.ExternalId)?.StoragePath);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
