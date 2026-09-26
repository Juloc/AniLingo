using AniLingo.Web.Features.Acquisition.Import;
using AniLingo.Web.Features.Acquisition.Monitoring;

namespace AniLingo.Web.Features.Acquisition.Pipeline;

/// <summary>
/// In-process scheduler for anime acquisition. Runs the pipeline for all monitored anime on the
/// canonical interval from the monitoring state, recovers persisted import/attempt state at
/// startup and serializes every pipeline run (periodic, "run now", interactive grab) through one
/// gate so two runs can never grab the same episode.
/// </summary>
public sealed class AnimeAcquisitionScheduler(
    IServiceScopeFactory scopeFactory,
    AnimeMonitoringStore monitoring,
    ILogger<AnimeAcquisitionScheduler> logger) : BackgroundService
{
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(45);

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim wake = new(0, 1);

    public DateTimeOffset? LastRunAtUtc { get; private set; }
    public AnimeAcquisitionRunSummary? LastRun { get; private set; }
    public string? LastRunError { get; private set; }
    public DateTimeOffset? NextRunAtUtc { get; private set; }
    public bool IsRunning => gate.CurrentCount == 0;

    /// <summary>Wakes the loop for a full run of all monitored anime.</summary>
    public void RequestRun()
    {
        if (wake.CurrentCount == 0)
        {
            wake.Release();
        }
    }

    /// <summary>Runs an action against the pipeline while holding the run gate.</summary>
    public async Task<T> RunExclusiveAsync<T>(
        Func<AnimeAcquisitionPipeline, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            return await action(scope.ServiceProvider.GetRequiredService<AnimeAcquisitionPipeline>(), cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<AnimeAcquisitionRunSummary> RunNowAsync(
        string? animeKey,
        AnimeSearchTrigger trigger,
        CancellationToken cancellationToken) =>
        RunExclusiveAsync(
            async (pipeline, token) =>
            {
                var summary = await pipeline.RunAsync(trigger, animeKey, token);
                if (animeKey is null)
                {
                    LastRunAtUtc = DateTimeOffset.UtcNow;
                    LastRun = summary;
                    LastRunError = null;
                }

                return summary;
            },
            cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        await RecoverAsync(stoppingToken);

        var delay = StartupDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            NextRunAtUtc = DateTimeOffset.UtcNow + delay;
            bool requested;
            try
            {
                requested = await wake.WaitAsync(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            AnimeMonitoringSchedule schedule;
            try
            {
                schedule = (await monitoring.LoadAsync(stoppingToken)).Schedule;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException)
            {
                logger.LogWarning(exception, "Monitoring state could not be read; using the default schedule.");
                schedule = AnimeMonitoringSchedule.Default;
            }

            if (schedule.Enabled || requested)
            {
                try
                {
                    var summary = await RunNowAsync(null, AnimeSearchTrigger.PeriodicMissing, stoppingToken);
                    logger.LogInformation("Anime acquisition run finished: {Summary}", summary);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    LastRunAtUtc = DateTimeOffset.UtcNow;
                    LastRunError = exception.Message;
                    logger.LogWarning(exception, "Anime acquisition run failed.");
                }
            }

            delay = schedule.Interval;
        }
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunExclusiveAsync(
                async (pipeline, token) =>
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var recovered = await scope.ServiceProvider
                        .GetRequiredService<AnimeImportExecutor>()
                        .RecoverAsync(token);
                    await pipeline.ReconcileAttemptsAsync(token);
                    if (recovered > 0)
                    {
                        logger.LogInformation("Resumed {Count} anime import(s) after startup.", recovered);
                    }

                    return recovered;
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Anime acquisition recovery after startup failed; the next run retries it.");
        }
    }
}
