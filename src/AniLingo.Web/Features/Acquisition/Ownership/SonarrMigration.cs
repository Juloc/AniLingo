namespace AniLingo.Web.Features.Acquisition.Ownership;

public sealed record AnimeMigrationRequest(
    string AnimeKey,
    AnimeMigrationAction Action,
    int? SonarrSeriesId = null,
    string? SonarrSeriesTitle = null,
    bool ApplySonarrMonitoring = false);

// SetSonarrMonitored is the only Sonarr change a migration may request. It is null when Sonarr
// must not be touched; the caller applies it before persisting State.
public sealed record AnimeMigrationPlan(
    bool Allowed,
    string Reason,
    AcquisitionOwnershipState State,
    AnimeManagementMode FromMode,
    AnimeManagementMode ToMode,
    bool Changed,
    int? SonarrSeriesId,
    bool? SetSonarrMonitored);

// Per-anime owner migration controls: keep Sonarr, stage parallel acquisition, hand over to
// Jularr, revert. Plans are pure and idempotent: applying the same request to the resulting
// state is a no-op.
public static class SonarrMigration
{
    public static AnimeManagementMode TargetMode(AnimeMigrationAction action) =>
        action switch
        {
            AnimeMigrationAction.KeepSonarr => AnimeManagementMode.ReadOnlyCoexistence,
            AnimeMigrationAction.StartParallel => AnimeManagementMode.ParallelAcquisition,
            AnimeMigrationAction.HandOverToAniLingo => AnimeManagementMode.AniLingoManaged,
            AnimeMigrationAction.Revert => AnimeManagementMode.ReadOnlyCoexistence,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown migration action.")
        };

    public static AnimeMigrationPlan Plan(
        AcquisitionOwnershipState state,
        SonarrObservedState sonarr,
        AnimeMigrationRequest request,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(sonarr);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AnimeKey);

        var animeKey = request.AnimeKey.Trim();
        state.Anime.TryGetValue(animeKey, out var existing);
        var from = existing?.Mode ?? AnimeManagementMode.ReadOnlyCoexistence;
        var to = TargetMode(request.Action);
        var unmonitoredByAniLingo = existing?.SonarrUnmonitoredByAniLingo ?? false;
        var seriesId = request.SonarrSeriesId ?? existing?.SonarrSeriesId;

        AnimeMigrationPlan Deny(string reason) =>
            new(false, reason, state, from, from, false, seriesId, null);

        if (request.SonarrSeriesId is int requested &&
            existing?.SonarrSeriesId is int linked &&
            requested != linked &&
            unmonitoredByAniLingo)
        {
            return Deny("Jularr unmonitored the previously linked Sonarr series. Revert with the Sonarr monitoring option before linking another series.");
        }

        if (request.Action == AnimeMigrationAction.Revert && existing is null)
        {
            return new(true, "Nothing to revert: the anime is Sonarr-owned by default.", state, from, from, false, seriesId, null);
        }

        var modeDecision = SonarrParallelSafety.CanChangeMode(state, animeKey, to);
        if (!modeDecision.Allowed)
        {
            return Deny(modeDecision.Reason);
        }

        var series = SonarrOwnershipRecognizer.FindSeries(sonarr, seriesId);
        var seriesTitle = series?.Title ??
                          request.SonarrSeriesTitle ??
                          (seriesId == existing?.SonarrSeriesId ? existing?.SonarrSeriesTitle : null);
        bool? setSonarrMonitored = null;
        var notes = new List<string>();

        if (to == AnimeManagementMode.AniLingoManaged && seriesId is not null)
        {
            if (sonarr.Status == SonarrObservationStatus.Unavailable)
            {
                return Deny("Sonarr cannot be observed. Handover needs a current Sonarr queue for the linked series.");
            }

            var active = sonarr.Queue.Count(item => item.SeriesId == seriesId);
            if (active > 0)
            {
                return Deny($"Sonarr has {active} active download(s) for '{seriesTitle}'. Let Sonarr finish importing them before handing the anime over.");
            }

            if (series is { Monitored: true })
            {
                if (request.ApplySonarrMonitoring)
                {
                    setSonarrMonitored = false;
                    unmonitoredByAniLingo = true;
                    notes.Add($"Sonarr series '{series.Title}' will be unmonitored.");
                }
                else
                {
                    notes.Add($"Sonarr still monitors '{series.Title}'; Jularr grabs and renames stay blocked until it is unmonitored in Sonarr.");
                }
            }
        }
        else if (to != AnimeManagementMode.AniLingoManaged &&
                 unmonitoredByAniLingo &&
                 request.ApplySonarrMonitoring)
        {
            if (series is not null)
            {
                if (!series.Monitored)
                {
                    setSonarrMonitored = true;
                    notes.Add($"Sonarr monitoring for '{series.Title}' will be restored.");
                }

                unmonitoredByAniLingo = false;
            }
            else if (sonarr.Status == SonarrObservationStatus.Unavailable)
            {
                return Deny("Sonarr cannot be observed, so its monitoring cannot be restored. Try again when Sonarr is reachable.");
            }
            else
            {
                // The series no longer exists in Sonarr (or Sonarr was removed): nothing to restore.
                unmonitoredByAniLingo = false;
                notes.Add("The linked Sonarr series no longer exists; there is no Sonarr monitoring to restore.");
            }
        }
        else if (to != AnimeManagementMode.AniLingoManaged && unmonitoredByAniLingo)
        {
            notes.Add("Jularr previously unmonitored the Sonarr series; re-enable monitoring in Sonarr or use the Sonarr monitoring option.");
        }

        var changed = existing is null ||
                      existing.Mode != to ||
                      existing.SonarrSeriesId != seriesId ||
                      existing.SonarrUnmonitoredByAniLingo != unmonitoredByAniLingo ||
                      setSonarrMonitored is not null;

        if (!changed)
        {
            return new(
                true,
                Join($"Anime already uses {Describe(to)}.", notes),
                state,
                from,
                to,
                false,
                seriesId,
                null);
        }

        var assignment = new AnimeManagementAssignment(
            animeKey,
            to,
            existing is null || existing.Mode != to ? now : existing.ChangedAtUtc,
            seriesId,
            seriesTitle,
            unmonitoredByAniLingo);

        var anime = new Dictionary<string, AnimeManagementAssignment>(
            state.Anime,
            StringComparer.OrdinalIgnoreCase)
        {
            [animeKey] = assignment
        };

        var detail = Join($"{Describe(from)} -> {Describe(to)}.", notes);
        var log = state.MigrationLog.ToList();
        log.Add(new AnimeMigrationEvent(now, animeKey, request.Action, from, to, detail));
        if (log.Count > AcquisitionOwnershipState.MaxMigrationEvents)
        {
            log.RemoveRange(0, log.Count - AcquisitionOwnershipState.MaxMigrationEvents);
        }

        return new(
            true,
            detail,
            state with { Anime = anime, MigrationLog = log },
            from,
            to,
            true,
            seriesId,
            setSonarrMonitored);
    }

    public static string Describe(AnimeManagementMode mode) =>
        mode switch
        {
            AnimeManagementMode.ReadOnlyCoexistence => "Sonarr-managed (read-only coexistence)",
            AnimeManagementMode.ParallelAcquisition => "parallel acquisition",
            AnimeManagementMode.AniLingoManaged => "AniLingo-managed",
            _ => mode.ToString()
        };

    private static string Join(string head, IReadOnlyList<string> notes) =>
        notes.Count == 0 ? head : head + " " + string.Join(" ", notes);
}
