using AniLingo.Web.Features.Acquisition.Ownership;

namespace AniLingo.Web.Features.Sonarr;

public sealed record SonarrMigrationResult(
    bool Success,
    bool Changed,
    string Message);

// Applies an owner migration action (keep Sonarr / parallel / hand over / revert) to the
// canonical ownership store. The only optional Sonarr side effect is toggling series monitoring,
// and only when the owner explicitly asked for it on the migration request.
public sealed class SonarrMigrationService(
    AcquisitionOwnershipStore ownershipStore,
    SonarrObservationService observationService,
    SonarrConnectionStore connectionStore,
    ISonarrSeriesMonitoringClient monitoringClient,
    ILogger<SonarrMigrationService> logger)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<SonarrMigrationResult> ApplyAsync(
        AnimeMigrationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await gate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await observationService.GetSnapshotAsync(
                forceRefresh: true,
                cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var plan = SonarrMigration.Plan(snapshot.State, snapshot.Sonarr, request, now);
            if (!plan.Allowed)
            {
                return new(false, false, plan.Reason);
            }

            if (!plan.Changed)
            {
                return new(true, false, plan.Reason);
            }

            if (plan.SetSonarrMonitored is bool monitored && plan.SonarrSeriesId is int seriesId)
            {
                var settings = await connectionStore.LoadAsync(cancellationToken);
                if (settings is null)
                {
                    return new(false, false, "Sonarr is not configured, so its monitoring cannot be changed.");
                }

                try
                {
                    await monitoringClient.SetMonitoredAsync(settings, seriesId, monitored, cancellationToken);
                }
                catch (Exception exception) when (
                    exception is SonarrObserverException or HttpRequestException or TaskCanceledException &&
                    !cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(
                        exception,
                        "Sonarr monitoring change for series {SeriesId} failed; migration of {AnimeKey} was not applied.",
                        seriesId,
                        request.AnimeKey);
                    return new(false, false, $"Sonarr monitoring could not be changed, so nothing was migrated: {exception.Message}");
                }
            }

            // Re-plan against the freshly locked state; plans are idempotent, so a concurrent
            // identical change results in a no-op instead of a duplicate event.
            await ownershipStore.UpdateAsync(
                current =>
                {
                    var replanned = SonarrMigration.Plan(
                        current,
                        snapshot.Sonarr,
                        request,
                        now);
                    return replanned.Allowed && replanned.Changed ? replanned.State : current;
                },
                cancellationToken);

            observationService.Invalidate();
            logger.LogInformation(
                "Sonarr migration {Action} applied to {AnimeKey}: {Detail}",
                request.Action,
                request.AnimeKey,
                plan.Reason);
            return new(true, true, plan.Reason);
        }
        finally
        {
            gate.Release();
        }
    }
}
