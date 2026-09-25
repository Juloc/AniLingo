namespace AniLingo.Web.Features.Acquisition.Ownership;

// Decisions that combine persisted AniLingo ownership with the read-only Sonarr observation.
// Every AniLingo grab, import and rename seam consults these before acting.
public static partial class SonarrParallelSafety
{
    // A Sonarr grab this recent may not be imported/rescanned yet, so AniLingo must not grab the
    // same episode again.
    public static readonly TimeSpan RecentSonarrActivityWindow = TimeSpan.FromHours(24);

    public static OwnershipDecision CanGrab(
        AcquisitionOwnershipSnapshot ownership,
        AcquisitionGrabRequest request,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(request);

        var state = ownership.State;
        var sonarr = ownership.Sonarr;
        var basic = CanGrab(state, sonarr, request.AnimeKey, request.ReleaseKey);
        if (!basic.Allowed)
        {
            return basic;
        }

        var mode = GetMode(state, request.AnimeKey);
        var linkedSeriesId = SonarrOwnershipRecognizer.GetLinkedSeriesId(state, request.AnimeKey);
        var unverified = RequireVerifiedSonarr(sonarr, mode, linkedSeriesId);
        if (unverified is not null)
        {
            return unverified;
        }

        var series = SonarrOwnershipRecognizer.FindSeries(sonarr, linkedSeriesId);
        if (series is null)
        {
            return basic;
        }

        if (mode == AnimeManagementMode.AniLingoManaged && series.Monitored)
        {
            return new(
                false,
                $"Sonarr still monitors '{series.Title}'. Unmonitor it in Sonarr (or hand over with the Sonarr monitoring option) before AniLingo grabs, otherwise both managers grab the same episode.");
        }

        var queued = sonarr.Queue.FirstOrDefault(item =>
            item.SeriesId == series.Id &&
            (item.Episode is null || request.Covers(item.Episode)));
        if (queued is not null)
        {
            return new(false, $"Sonarr is already downloading '{queued.Title}' for this episode.");
        }

        var recentGrab = sonarr.History.FirstOrDefault(item =>
            item.Kind == SonarrHistoryEventKind.Grabbed &&
            item.SeriesId == series.Id &&
            item.Episode is not null &&
            request.Covers(item.Episode) &&
            item.AtUtc is DateTimeOffset at &&
            now - at <= RecentSonarrActivityWindow &&
            !sonarr.History.Any(failed =>
                failed.Kind == SonarrHistoryEventKind.DownloadFailed &&
                failed.DownloadId is not null &&
                string.Equals(failed.DownloadId, item.DownloadId, StringComparison.OrdinalIgnoreCase)));
        if (recentGrab is not null)
        {
            return new(
                false,
                $"Sonarr grabbed '{recentGrab.SourceTitle}' for this episode at {recentGrab.AtUtc:u}; wait for Sonarr's import and a library rescan.");
        }

        return new(true, "Release and episode are not owned by Sonarr or another AniLingo job.");
    }

    public static OwnershipDecision CanImport(
        AcquisitionOwnershipSnapshot ownership,
        AcquisitionImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AnimeKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.JobId);

        var state = ownership.State;
        var sonarr = ownership.Sonarr;
        var mode = GetMode(state, request.AnimeKey);
        if (mode == AnimeManagementMode.ReadOnlyCoexistence)
        {
            return new(false, "Read-only coexistence: Sonarr imports this anime.");
        }

        state.Jobs.TryGetValue(request.JobId, out var job);
        if (job is not null && job.Owner != AcquisitionOwner.AniLingo)
        {
            return new(false, "Acquisition job is owned by Sonarr.");
        }

        if (job is not null && !job.AnimeKey.Equals(request.AnimeKey, StringComparison.OrdinalIgnoreCase))
        {
            return new(false, "Acquisition job belongs to another anime.");
        }

        var downloadId = request.DownloadId ?? job?.DownloadId;
        if (SonarrOwnershipRecognizer.IsSonarrDownload(sonarr, downloadId))
        {
            return job is null
                ? new(false, "Download is tracked by Sonarr; Sonarr imports it.")
                : new(false, "Sonarr is also tracking this AniLingo download (shared download-client category?); refusing a conflicting import.");
        }

        if (!string.IsNullOrWhiteSpace(request.SourcePath))
        {
            var recognized = SonarrOwnershipRecognizer.RecognizePath(sonarr, request.SourcePath);
            if (recognized.Kind == SonarrPathOwnershipKind.QueueOutput)
            {
                return new(false, recognized.Detail);
            }
        }

        if (mode == AnimeManagementMode.ParallelAcquisition && job is null)
        {
            return new(false, "Parallel acquisition imports only downloads registered as AniLingo jobs.");
        }

        var unverified = RequireVerifiedSonarr(
            sonarr,
            mode,
            SonarrOwnershipRecognizer.GetLinkedSeriesId(state, request.AnimeKey));
        return unverified ?? new(true, "Download is owned by AniLingo and not tracked by Sonarr.");
    }

    public static OwnershipDecision CanMutateLibraryPath(
        AcquisitionOwnershipSnapshot ownership,
        string animeKey,
        string path)
    {
        ArgumentNullException.ThrowIfNull(ownership);

        var state = ownership.State;
        var sonarr = ownership.Sonarr;
        var basic = CanMutatePath(state, sonarr, animeKey, path);
        if (!basic.Allowed)
        {
            return basic;
        }

        var mode = GetMode(state, animeKey);
        var linkedSeriesId = SonarrOwnershipRecognizer.GetLinkedSeriesId(state, animeKey);
        var unverified = RequireVerifiedSonarr(sonarr, mode, linkedSeriesId);
        if (unverified is not null)
        {
            return unverified;
        }

        var recognized = SonarrOwnershipRecognizer.RecognizePath(sonarr, path);
        if (recognized.Kind == SonarrPathOwnershipKind.QueueOutput)
        {
            return new(false, recognized.Detail);
        }

        if (recognized.Series is { } series)
        {
            if (linkedSeriesId != series.Id)
            {
                return new(false, $"{recognized.Detail} That series is not linked to this anime.");
            }

            if (series.Monitored)
            {
                return new(
                    false,
                    $"{recognized.Detail} Sonarr still monitors it; changing the file would make Sonarr re-import or re-grab it (rename loop).");
            }
        }

        return basic;
    }

    public static OwnershipDecision CanRename(
        AcquisitionOwnershipSnapshot ownership,
        string animeKey,
        string sourcePath,
        string targetPath,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var source = CanMutateLibraryPath(ownership, animeKey, sourcePath);
        if (!source.Allowed)
        {
            return new(false, $"Source: {source.Reason}");
        }

        var target = CanMutateLibraryPath(ownership, animeKey, targetPath);
        if (!target.Allowed)
        {
            return new(false, $"Target: {target.Reason}");
        }

        var recentSonarrChange = ownership.Sonarr.History.FirstOrDefault(item =>
            item.Kind is SonarrHistoryEventKind.Renamed or SonarrHistoryEventKind.Imported &&
            item.AtUtc is DateTimeOffset at &&
            now - at <= RecentSonarrActivityWindow &&
            (SonarrOwnershipRecognizer.PathEquals(item.Path, sourcePath) ||
             SonarrOwnershipRecognizer.PathEquals(item.Path, targetPath) ||
             SonarrOwnershipRecognizer.PathEquals(item.SourcePath, targetPath)));
        if (recentSonarrChange is not null)
        {
            return new(
                false,
                $"Sonarr {recentSonarrChange.EventType} this file at {recentSonarrChange.AtUtc:u}; renaming it now would start a rename loop.");
        }

        return new(true, "Source and target are owned by AniLingo for this anime and Sonarr is not acting on them.");
    }

    private static OwnershipDecision? RequireVerifiedSonarr(
        SonarrObservedState sonarr,
        AnimeManagementMode mode,
        int? linkedSeriesId)
    {
        if (sonarr.Status != SonarrObservationStatus.Unavailable)
        {
            return null;
        }

        // Without a current observation AniLingo cannot prove Sonarr is idle. Parallel mode and
        // every Sonarr-linked anime therefore fail closed; only unlinked AniLingo-managed anime continue.
        if (linkedSeriesId is null && mode == AnimeManagementMode.AniLingoManaged)
        {
            return null;
        }

        return new(
            false,
            $"Sonarr could not be observed ({sonarr.StatusDetail ?? "unavailable"}); AniLingo pauses this action until Sonarr ownership can be verified.");
    }

    private static IEnumerable<OwnershipConflict> DetectSonarrConflicts(
        AcquisitionOwnershipState state,
        SonarrObservedState sonarr)
    {
        foreach (var job in state.Jobs.Values.Where(job =>
                     job.Owner == AcquisitionOwner.AniLingo &&
                     job.DownloadId is not null &&
                     job.Status is AcquisitionOwnershipStatus.Pending or AcquisitionOwnershipStatus.Importing &&
                     SonarrOwnershipRecognizer.IsSonarrDownload(sonarr, job.DownloadId)))
        {
            yield return new(
                "download",
                job.AnimeKey,
                job.DownloadId!,
                "Sonarr is tracking an AniLingo download. Give AniLingo its own download-client category so only one manager imports it.");
        }

        foreach (var assignment in state.Anime.Values)
        {
            if (assignment.SonarrSeriesId is not int seriesId)
            {
                continue;
            }

            if (sonarr.Status == SonarrObservationStatus.Unavailable &&
                assignment.Mode != AnimeManagementMode.ReadOnlyCoexistence)
            {
                yield return new(
                    "unverified",
                    assignment.AnimeKey,
                    seriesId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "Sonarr cannot be observed; AniLingo grabs, imports and renames for this Sonarr-linked anime are paused.");
                continue;
            }

            var series = SonarrOwnershipRecognizer.FindSeries(sonarr, seriesId);
            if (series is null)
            {
                continue;
            }

            if (assignment.Mode == AnimeManagementMode.AniLingoManaged)
            {
                if (series.Monitored)
                {
                    yield return new(
                        "monitoring",
                        assignment.AnimeKey,
                        series.Title,
                        "AniLingo manages this anime but Sonarr still monitors the series. AniLingo grabs and renames stay blocked until Sonarr stops monitoring it.");
                }

                foreach (var activity in sonarr.History.Where(item =>
                             item.SeriesId == seriesId &&
                             item.Kind is SonarrHistoryEventKind.Grabbed or SonarrHistoryEventKind.Imported or SonarrHistoryEventKind.Renamed &&
                             item.AtUtc is DateTimeOffset at &&
                             at > assignment.ChangedAtUtc))
                {
                    yield return new(
                        "sonarr-activity",
                        assignment.AnimeKey,
                        activity.SourceTitle ?? activity.Path ?? activity.EventType,
                        $"Sonarr {activity.EventType} after the anime was handed over to AniLingo.");
                }
            }
            else if (assignment.SonarrUnmonitoredByAniLingo && !series.Monitored)
            {
                yield return new(
                    "monitoring",
                    assignment.AnimeKey,
                    series.Title,
                    "Sonarr owns this anime again but AniLingo left the Sonarr series unmonitored. Re-enable monitoring in Sonarr or revert with the Sonarr monitoring option.");
            }
        }

        foreach (var rename in sonarr.History.Where(item => item.Kind == SonarrHistoryEventKind.Renamed))
        {
            var owned = state.Paths.Values.FirstOrDefault(path =>
                path.Owner == AcquisitionOwner.AniLingo &&
                (SonarrOwnershipRecognizer.PathEquals(path.Path, rename.SourcePath) ||
                 SonarrOwnershipRecognizer.PathEquals(path.Path, rename.Path)));
            if (owned is not null)
            {
                yield return new(
                    "rename-loop",
                    owned.AnimeKey,
                    owned.Path,
                    "Sonarr renamed an AniLingo-owned file. AniLingo will not rename it back; stop Sonarr from managing this series.");
            }
        }
    }
}
