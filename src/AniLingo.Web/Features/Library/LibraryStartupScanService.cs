using AniLingo.Web.Data;
using AniLingo.Web.Features.Subtitles;
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

        Guid[] rootIds;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            rootIds = await db.LibraryRoots
                .AsNoTracking()
                .Where(x => x.IsEnabled)
                .OrderBy(x => x.CreatedAt)
                .Select(x => x.Id)
                .ToArrayAsync(stoppingToken);
        }

        var successfulScans = 0;

        foreach (var rootId in rootIds)
        {
            stoppingToken.ThrowIfCancellationRequested();

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var scanner = scope.ServiceProvider.GetRequiredService<LibraryScanner>();
                var result = await scanner.ScanAsync(rootId, stoppingToken);
                successfulScans++;

                logger.LogInformation(
                    "Startup library reconciliation completed for {RootId}: {Discovered} added, {Updated} changed, {Removed} removed.",
                    rootId,
                    result.Discovered,
                    result.Updated,
                    result.Removed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (DirectoryNotFoundException exception)
            {
                logger.LogWarning(
                    exception,
                    "Startup library reconciliation skipped unavailable root {RootId}. Existing library state was preserved.",
                    rootId);
            }
            catch (IOException exception)
            {
                logger.LogWarning(
                    exception,
                    "Startup library reconciliation could not safely read root {RootId}. Existing library state was preserved.",
                    rootId);
            }
            catch (UnauthorizedAccessException exception)
            {
                logger.LogWarning(
                    exception,
                    "Startup library reconciliation cannot access root {RootId}. Existing library state was preserved.",
                    rootId);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Startup library reconciliation failed for root {RootId}; remaining roots will still be processed.",
                    rootId);
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
    }
}
