using AniLingo.Web.Features.Acquisition.Ownership;

namespace AniLingo.Web.Features.Sonarr;

// Builds the read-only Sonarr observation from the canonical Sonarr connection
// (SonarrConnectionStore) and combines it with the canonical ownership store. Acquisition
// executors call GetSnapshotAsync before grabbing, importing or renaming.
public sealed class SonarrObservationService(
    SonarrConnectionStore connectionStore,
    ISonarrObserverClient observerClient,
    AcquisitionOwnershipStore ownershipStore,
    ILogger<SonarrObservationService> logger)
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly HashSet<string> reportedConflicts = new(StringComparer.Ordinal);
    private SonarrObservedState? cached;

    public async Task<AcquisitionOwnershipSnapshot> GetSnapshotAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var state = await ownershipStore.LoadAsync(cancellationToken);
        var sonarr = await GetObservationAsync(state, forceRefresh, cancellationToken);
        return new(state, sonarr);
    }

    public async Task<SonarrObservedState> GetObservationAsync(
        AcquisitionOwnershipState state,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        await gate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (!forceRefresh &&
                cached?.ObservedAtUtc is DateTimeOffset observedAt &&
                now - observedAt < CacheDuration)
            {
                return cached;
            }

            cached = await ObserveAsync(state, now, cancellationToken);
            ReportConflicts(state, cached);
            return cached;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Invalidate() => cached = null;

    private async Task<SonarrObservedState> ObserveAsync(
        AcquisitionOwnershipState state,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var settings = await connectionStore.LoadAsync(cancellationToken);
        if (settings is null)
        {
            return SonarrObservedState.NotConfigured with { ObservedAtUtc = now };
        }

        try
        {
            var series = await observerClient.GetSeriesAsync(settings, cancellationToken);
            var queue = await observerClient.GetQueueAsync(settings, cancellationToken);
            var history = await observerClient.GetRecentHistoryAsync(settings, cancellationToken);

            // Exact Sonarr file paths matter only for anime AniLingo may mutate; read-only
            // coexistence already blocks every AniLingo mutation, so skip those series.
            var knownSeries = series.Select(item => item.Id).ToHashSet();
            var files = new List<SonarrObservedEpisodeFile>();
            foreach (var seriesId in state.Anime.Values
                         .Where(item =>
                             item.Mode != AnimeManagementMode.ReadOnlyCoexistence &&
                             item.SonarrSeriesId is int id &&
                             knownSeries.Contains(id))
                         .Select(item => item.SonarrSeriesId!.Value)
                         .Distinct())
            {
                files.AddRange(await observerClient.GetEpisodeFilesAsync(settings, seriesId, cancellationToken));
            }

            return SonarrObservedState.FromObservation(series, files, queue, history, now);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is SonarrObserverException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(
                exception,
                "Sonarr observation failed; Sonarr-linked AniLingo acquisition actions are paused until Sonarr can be observed.");
            return SonarrObservedState.Unavailable(
                exception is SonarrObserverException ? exception.Message : "Sonarr could not be reached.",
                now);
        }
    }

    private void ReportConflicts(AcquisitionOwnershipState state, SonarrObservedState sonarr)
    {
        foreach (var conflict in SonarrParallelSafety.DetectConflicts(state, sonarr))
        {
            if (reportedConflicts.Count > 1_000)
            {
                reportedConflicts.Clear();
            }

            var key = $"{conflict.Kind}|{conflict.AnimeKey}|{conflict.Value}";
            if (reportedConflicts.Add(key))
            {
                logger.LogWarning(
                    "Sonarr/AniLingo ownership conflict ({Kind}) for {AnimeKey}: {Value}. {Reason}",
                    conflict.Kind,
                    conflict.AnimeKey,
                    conflict.Value,
                    conflict.Reason);
            }
        }
    }
}
