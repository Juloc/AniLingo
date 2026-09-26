using System.Collections.Concurrent;
using AniLingo.Web.Features.Acquisition.AniListAutoMonitor;
using AniLingo.Web.Features.Acquisition.Import;
using AniLingo.Web.Features.Acquisition.Monitoring;

namespace AniLingo.Web.Features.Acquisition.Pipeline;

/// <summary>
/// In-process scheduler for anime acquisition. Runs the pipeline for all monitored anime on the
/// one canonical interval stored in the monitoring state, runs owner-requested searches, recovers
/// persisted import/attempt state at startup and serializes every pipeline run (periodic,
/// requested, interactive grab) through one gate: at most one run is active, so two runs can
/// never grab the same episode, and each run is bounded by the pipeline's search limits.
/// </summary>
public sealed class AnimeAcquisitionScheduler(
    IServiceScopeFactory scopeFactory,
    AnimeMonitoringStore monitoring,
    ILogger<AnimeAcquisitionScheduler> logger) : BackgroundService
{
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(45);
    private const int MaxQueuedRequests = 50;

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim wake = new(0, 1);
    private readonly ConcurrentQueue<AnimeAcquisitionRunRequest> requests = new();

    public DateTimeOffset? LastRunAtUtc { get; private set; }
    public AnimeAcquisitionRunSummary? LastRun { get; private set; }
    public string? LastRunError { get; private set; }
    public DateTimeOffset? NextRunAtUtc { get; private set; }
    public bool IsRunning => gate.CurrentCount == 0;
    public int QueuedRequests => requests.Count;

    /// <summary>
    /// Queues a run (all monitored anime when <paramref name="animeKey"/> is null) and wakes the
    /// loop. Returns false when too many requests are already waiting.
    /// </summary>
    public bool RequestRun(string? animeKey = null, AnimeSearchTrigger trigger = AnimeSearchTrigger.Manual)
    {
        if (requests.Count >= MaxQueuedRequests)
        {
            return false;
        }

        requests.Enqueue(new AnimeAcquisitionRunRequest(animeKey, trigger));
        if (wake.CurrentCount == 0)
        {
            try
            {
                wake.Release();
            }
            catch (SemaphoreFullException)
            {
                // Another request woke the loop at the same moment.
            }
        }

        return true;
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
                await ResumeImportsAsync(token);
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

    /// <summary>
    /// Startup recovery: resumes interrupted or missed imports and brings monitoring attempts in
    /// line with the acquisition relation and Operations. Safe to run more than once.
    /// </summary>
    public Task<int> RecoverAsync(CancellationToken cancellationToken) =>
        RunExclusiveAsync(
            async (pipeline, token) =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var recovered = await scope.ServiceProvider
                    .GetRequiredService<AnimeImportExecutor>()
                    .RecoverAsync(token);
                await pipeline.ReconcileAttemptsAsync(token);
                return recovered;
            },
            cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        try
        {
            var recovered = await RecoverAsync(stoppingToken);
            if (recovered > 0)
            {
                logger.LogInformation("Resumed {Count} anime import(s) after startup.", recovered);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Anime acquisition recovery after startup failed; the next run retries it.");
        }

        var delay = StartupDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            NextRunAtUtc = DateTimeOffset.UtcNow + delay;
            bool woken;
            try
            {
                woken = await wake.WaitAsync(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (woken)
            {
                await RunRequestsAsync(stoppingToken);
                // Requested runs do not reset the periodic cadence beyond the remaining delay.
                delay = NextRunAtUtc is { } next && next > DateTimeOffset.UtcNow
                    ? next - DateTimeOffset.UtcNow
                    : TimeSpan.Zero;
                continue;
            }

            var schedule = await LoadScheduleAsync(stoppingToken);
            if (schedule.Enabled)
            {
                await RunSafelyAsync(null, AnimeSearchTrigger.PeriodicMissing, stoppingToken);
            }

            delay = schedule.Interval;
        }
    }

    // Imports deferred while a library scan or rename ran, or missed by the SABnzbd monitor,
    // continue before every run so their episodes are no longer wanted.
    private async Task ResumeImportsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AnimeImportExecutor>().RecoverAsync(cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or InvalidOperationException or HttpRequestException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Pending anime imports could not be resumed; the next run retries them.");
        }
    }

    private async Task RunRequestsAsync(CancellationToken stoppingToken)
    {
        var pending = new List<AnimeAcquisitionRunRequest>();
        while (requests.TryDequeue(out var request))
        {
            pending.Add(request);
        }

        // A full run covers every per-anime request queued with it.
        var batch = pending.Any(request => request.AnimeKey is null)
            ? [pending.First(request => request.AnimeKey is null)]
            : pending.DistinctBy(request => request.AnimeKey, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var request in batch)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            await RunSafelyAsync(request.AnimeKey, request.Trigger, stoppingToken);
        }
    }

    private async Task RunSafelyAsync(
        string? animeKey,
        AnimeSearchTrigger trigger,
        CancellationToken stoppingToken)
    {
        try
        {
            var summary = await RunNowAsync(animeKey, trigger, stoppingToken);
            logger.LogInformation(
                "Anime acquisition run ({Trigger}, {Scope}) finished: {Summary}",
                trigger,
                animeKey ?? "all monitored anime",
                summary);
            if (animeKey is null)
            {
                await RunAniListAutoMonitorAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (animeKey is null)
            {
                LastRunAtUtc = DateTimeOffset.UtcNow;
                LastRunError = exception.Message;
            }

            logger.LogWarning(exception, "Anime acquisition run failed.");
        }
    }

    // P1 item 7: after every full run, auto-monitor anime on each opted-in profile's AniList
    // Current/Planning lists that already exist locally. Best-effort: a failure here never fails
    // the acquisition run itself and is retried on the next full run.
    private async Task RunAniListAutoMonitorAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var settings = await scope.ServiceProvider
                .GetRequiredService<AniListAutoMonitorSettingsStore>()
                .LoadAsync(cancellationToken);
            var enabledProfiles = settings.Profiles
                .Where(pair => pair.Value.Enabled)
                .Select(pair => pair.Key)
                .ToArray();
            if (enabledProfiles.Length == 0)
            {
                return;
            }

            var service = scope.ServiceProvider.GetRequiredService<AniListAutoMonitorService>();
            foreach (var profileId in enabledProfiles)
            {
                var result = await service.RunForProfileAsync(profileId, cancellationToken);
                if (result.NewlyMonitored > 0)
                {
                    logger.LogInformation(
                        "AniList auto-monitor for profile {ProfileId}: {NewlyMonitored} anime newly monitored from {ListEntries} Current/Planning list entries.",
                        profileId,
                        result.NewlyMonitored,
                        result.ListEntries);
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or InvalidOperationException)
        {
            logger.LogWarning(exception, "AniList list auto-monitor pass failed; the next run retries it.");
        }
    }

    private async Task<AnimeMonitoringSchedule> LoadScheduleAsync(CancellationToken stoppingToken)
    {
        try
        {
            return (await monitoring.LoadAsync(stoppingToken)).Schedule;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            logger.LogWarning(exception, "Monitoring state could not be read; using the default schedule.");
            return AnimeMonitoringSchedule.Default;
        }
    }
}

public sealed record AnimeAcquisitionRunRequest(
    string? AnimeKey,
    AnimeSearchTrigger Trigger);
