using AniLingo.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Library;

// Queues one full reconciliation per enabled root once the host is up. The runs themselves
// go through LibraryScanCoordinator like every other scan, so they share its guard,
// progress reporting and history.
public sealed class LibraryStartupScanService(
    IServiceScopeFactory scopeFactory,
    LibraryScanCoordinator scans,
    IHostApplicationLifetime lifetime,
    ILogger<LibraryStartupScanService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Host startup is never held up by NAS probing, and the worker lanes have recovered
        // interrupted runs of the previous process before new ones are queued.
        await WaitForStartAsync(stoppingToken);
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        Guid[] roots;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            roots = await db.LibraryRoots
                .AsNoTracking()
                .Where(x => x.IsEnabled)
                .OrderBy(x => x.CreatedAt)
                .Select(x => x.Id)
                .ToArrayAsync(stoppingToken);
        }

        foreach (var rootId in roots)
        {
            try
            {
                var queued = await scans.QueueAsync(
                    new LibraryScanRequest(rootId, LibraryScanTrigger.Startup),
                    stoppingToken);

                if (!queued.Queued)
                {
                    logger.LogWarning(
                        "Startup library reconciliation was not queued for root {RootId}: {Reason}",
                        rootId,
                        queued.Message);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Startup library reconciliation could not be queued for root {RootId}; remaining roots are still queued.",
                    rootId);
            }
        }
    }

    private async Task WaitForStartAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var startedRegistration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        using var stoppingRegistration = stoppingToken.Register(() => started.TrySetResult());
        await started.Task;
    }
}
