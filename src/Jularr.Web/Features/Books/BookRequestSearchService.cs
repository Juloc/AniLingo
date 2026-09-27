using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Operations;

namespace Jularr.Web.Features.Books;

/// <summary>
/// Keeps book requests moving without the owner: approved requests that found no release are
/// searched again when their backoff is due, and a book download that failed in SABnzbd goes on
/// with the next release (the failed one is never sent again).
/// </summary>
public sealed class BookRequestSearchService(
    IServiceScopeFactory scopes,
    ILogger<BookRequestSearchService> logger,
    TimeProvider? clock = null) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await SearchDueAsync(scope.ServiceProvider, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, stoppingToken);
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
        var service = services.GetRequiredService<AcquisitionRequestService>();
        var affected = (await store.ListDownloadingAsync(MediaAcquisitionKind.Book, cancellationToken))
            .Where(request => request.OperationId is { } operationId && failedOperationIds.Contains(operationId))
            .ToArray();
        foreach (var request in affected)
        {
            await service.ContinueAsync(request.Id, cancellationToken);
        }

        return affected.Length;
    }
}
