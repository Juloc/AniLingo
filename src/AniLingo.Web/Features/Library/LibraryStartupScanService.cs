using AniLingo.Web.Data;
using AniLingo.Web.Features.Subtitles;
using AniLingo.Web.Features.Operations;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Library;

public sealed class LibraryStartupScanService(
    IServiceScopeFactory scopeFactory,
    ILogger<LibraryStartupScanService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ensure host startup is never held up by NAS enumeration.
        await Task.Yield();

        (Guid Id, string Name)[] roots;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            roots = await db.LibraryRoots
                .AsNoTracking()
                .Where(x => x.IsEnabled)
                .OrderBy(x => x.CreatedAt)
                .Select(x => new { x.Id, x.Name })
                .AsAsyncEnumerable()
                .Select(x => (x.Id, x.Name))
                .ToArrayAsync(stoppingToken);
        }

        var successfulScans = 0;

        foreach (var root in roots)
        {
            stoppingToken.ThrowIfCancellationRequested();

            Guid operationId = Guid.Empty;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var operations = new OperationStore(db);

                operationId = await operations.CreateAsync(
                    new OperationDescriptor(
                        "startup-library-scan",
                        "Library",
                        "Startup library reconciliation",
                        root.Name,
                        Lane: OperationLane.Maintenance,
                        Retryable: false),
                    stoppingToken);

                await operations.MarkRunningAsync(
                    operationId,
                    stoppingToken);
                await operations.ReportProgressAsync(
                    operationId,
                    10,
                    "Reconciling media files.",
                    cancellationToken: stoppingToken);

                var scanner = scope.ServiceProvider.GetRequiredService<LibraryScanner>();
                var result = await scanner.ScanAsync(root.Id, stoppingToken);
                successfulScans++;

                await operations.ReportProgressAsync(
                    operationId,
                    100,
                    $"{result.Discovered} added, {result.Updated} changed, {result.Removed} removed.",
                    cancellationToken: stoppingToken);
                await operations.MarkSucceededAsync(
                    operationId,
                    "Startup library reconciliation completed.",
                    stoppingToken);

                logger.LogInformation(
                    "Startup library reconciliation completed for {RootId}: {Discovered} added, {Updated} changed, {Removed} removed.",
                    root.Id,
                    result.Discovered,
                    result.Updated,
                    result.Removed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                if (operationId != Guid.Empty)
                {
                    await TryMarkInterruptedAsync(
                        operationId,
                        "Interrupted because AniLingo is stopping.");
                }
                break;
            }
            catch (DirectoryNotFoundException exception)
            {
                await TryMarkFailedAsync(operationId, "Media root is unavailable.");
                logger.LogWarning(
                    exception,
                    "Startup library reconciliation skipped unavailable root {RootId}. Existing library state was preserved.",
                    root.Id);
            }
            catch (IOException exception)
            {
                await TryMarkFailedAsync(operationId, "Media root could not be read safely.");
                logger.LogWarning(
                    exception,
                    "Startup library reconciliation could not safely read root {RootId}. Existing library state was preserved.",
                    root.Id);
            }
            catch (UnauthorizedAccessException exception)
            {
                await TryMarkFailedAsync(operationId, "Media root access was denied.");
                logger.LogWarning(
                    exception,
                    "Startup library reconciliation cannot access root {RootId}. Existing library state was preserved.",
                    root.Id);
            }
            catch (Exception exception)
            {
                await TryMarkFailedAsync(
                    operationId,
                    $"{exception.GetType().Name}: {exception.Message}");
                logger.LogError(
                    exception,
                    "Startup library reconciliation failed for root {RootId}; remaining roots will still be processed.",
                    root.Id);
            }
        }

        if (successfulScans == 0 || stoppingToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var subtitles = scope.ServiceProvider.GetRequiredService<SubtitleImportService>();
            var queued = await subtitles.QueueAllMissingAsync(stoppingToken);

            logger.LogInformation(
                "Startup learning-text preparation queued for {EpisodeCount} episode(s) after {RootCount} successful library reconciliation(s).",
                queued,
                successfulScans);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Startup library reconciliation completed, but learning-text preparation could not be queued.");
        }
    

    private async Task TryMarkFailedAsync(Guid operationId, string message)
    {
        if (operationId == Guid.Empty)
        {
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await new OperationStore(db).MarkFailedAsync(
                operationId,
                message,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not persist failure state for startup operation {OperationId}.",
                operationId);
        }
    }

    private async Task TryMarkInterruptedAsync(Guid operationId, string message)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await new OperationStore(db).MarkInterruptedAsync(
                operationId,
                message,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not persist interrupted state for startup operation {OperationId}.",
                operationId);
        }
    }}
}
