using System.Collections.Concurrent;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.MediaSegments;

// Queues cross-episode OP/ED detection for one anime season as a single bounded maintenance
// Operation, triggered by the owner ("Detect intro/outro for this season" on the segments page).
// Runs every episode of the season through MediaSegmentService.RunDetectorAsync, which already
// persists results per episode and skips episodes whose media identity and detector version are
// unchanged; this queue only bounds concurrency and reports overall progress. Never blocks
// playback: enqueueing gives up quickly, like TrickplayGenerator's queueing.
public sealed class SeasonSegmentDetectionQueue(
    BackgroundJobQueue jobs,
    ILogger<SeasonSegmentDetectionQueue> logger)
{
    public const string OperationKind = "segment-detection";

    // Fingerprint detection is CPU heavy (minutes of audio decoded and hashed per episode); bound
    // how many seasons can be analysed at once regardless of how many owners click the button.
    public const int MaximumPendingRuns = 2;

    public static readonly TimeSpan EnqueueTimeout = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<string, byte> pending = new(StringComparer.Ordinal);

    public static string Key(Guid animeId, int seasonNumber) =>
        $"{animeId:N}-s{seasonNumber}";

    public bool IsPending(Guid animeId, int seasonNumber) =>
        pending.ContainsKey(Key(animeId, seasonNumber));

    public async Task<bool> EnsureQueuedAsync(
        Guid animeId,
        int seasonNumber,
        string subject,
        CancellationToken cancellationToken)
    {
        var key = Key(animeId, seasonNumber);
        if (pending.ContainsKey(key) || pending.Count >= MaximumPendingRuns)
        {
            return false;
        }

        if (!pending.TryAdd(key, 0))
        {
            return false;
        }

        // The shared job queue is bounded; the owner's request must never wait for it.
        using var enqueueTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        enqueueTimeout.CancelAfter(EnqueueTimeout);

        try
        {
            await jobs.QueueAsync(
                new OperationDescriptor(
                    OperationKind,
                    "Library",
                    "Detect intro/outro for this season",
                    subject,
                    Lane: OperationLane.Maintenance,
                    Retryable: false),
                (operation, services, token) => RunAsync(animeId, seasonNumber, key, services, operation, token),
                enqueueTimeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            pending.TryRemove(key, out _);
            logger.LogDebug(
                "Background queue busy; season segment detection for {Subject} was not queued.",
                subject);
            return false;
        }
        catch
        {
            pending.TryRemove(key, out _);
            throw;
        }
    }

    private async Task RunAsync(
        Guid animeId,
        int seasonNumber,
        string key,
        IServiceProvider services,
        OperationExecutionContext operation,
        CancellationToken cancellationToken)
    {
        try
        {
            var db = services.GetRequiredService<AppDbContext>();
            var segments = services.GetRequiredService<MediaSegmentService>();

            var episodeIds = await db.Episodes
                .AsNoTracking()
                .Where(x => x.AnimeId == animeId && x.SeasonNumber == seasonNumber)
                .OrderBy(x => x.Number)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

            int completed = 0, skipped = 0, other = 0;
            for (var i = 0; i < episodeIds.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var run = await segments.RunDetectorAsync(episodeIds[i], force: false, cancellationToken);
                switch (run.Outcome)
                {
                    case SegmentDetectionOutcome.Completed:
                        completed++;
                        break;
                    case SegmentDetectionOutcome.Skipped:
                        skipped++;
                        break;
                    default:
                        other++;
                        break;
                }

                await operation.ReportAsync(
                    (int)((i + 1) * 100.0 / Math.Max(1, episodeIds.Count)),
                    $"Analysed {i + 1}/{episodeIds.Count} episode(s).",
                    cancellationToken: cancellationToken);
            }

            logger.LogInformation(
                "Season segment detection finished for anime {AnimeId} season {SeasonNumber}: " +
                "{Completed} analysed, {Skipped} unchanged, {Other} without media/analysis.",
                animeId,
                seasonNumber,
                completed,
                skipped,
                other);
        }
        finally
        {
            pending.TryRemove(key, out _);
        }
    }
}
