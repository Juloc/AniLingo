using Jularr.Web.Features.Operations;
using Jularr.Web.Infrastructure;

namespace Jularr.Web.Features.Media.Optimization;

// Runs optimizations as Operations on the background queue. That queue also runs library scans,
// so a remux never overlaps a queued scan of the same folder, and imports never wait for it.
public sealed class MediaOptimizationQueue(
    BackgroundJobQueue jobs,
    MediaOptimizationJournal journal)
{
    public const string OperationCategory = "Library";

    public ValueTask<Guid> QueueAsync(
        IReadOnlyList<Guid> mediaFileIds,
        string subject,
        string? profileId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediaFileIds);
        var ids = mediaFileIds.Distinct().ToArray();

        return jobs.QueueAsync(
            new OperationDescriptor(
                MediaContainerOptimizer.OperationKind,
                OperationCategory,
                "Optimize media for Direct Play",
                subject,
                profileId,
                OperationLane.Maintenance,
                Retryable: true),
            (operation, services, token) =>
                services.GetRequiredService<MediaContainerOptimizer>().OptimizeAsync(ids, operation, token),
            cancellationToken);
    }

    public async ValueTask<Guid?> QueueRecoveryAsync(CancellationToken cancellationToken)
    {
        if (!journal.HasEntries())
        {
            return null;
        }

        return await jobs.QueueAsync(
            new OperationDescriptor(
                MediaContainerOptimizer.OperationKind,
                OperationCategory,
                "Recover interrupted media optimization",
                Lane: OperationLane.Maintenance,
                Retryable: true),
            (operation, services, token) =>
                services.GetRequiredService<MediaContainerOptimizer>().RecoverAsync(operation, token),
            cancellationToken);
    }
}

// Once per start: an optimization interrupted by a crash or restart is finished or undone.
public sealed class MediaOptimizationRecoveryService(
    MediaOptimizationQueue queue,
    ILogger<MediaOptimizationRecoveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        try
        {
            await queue.QueueRecoveryAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Could not queue recovery of interrupted media optimizations.");
        }
    }
}
