namespace AniLingo.Web.Features.Acquisition.Ownership;

public static partial class SonarrParallelSafety
{
    public static AnimeManagementMode GetMode(
        AcquisitionOwnershipState state,
        string animeKey) =>
        state.Anime.TryGetValue(animeKey, out var assignment)
            ? assignment.Mode
            : AnimeManagementMode.ReadOnlyCoexistence;

    public static OwnershipDecision CanGrab(
        AcquisitionOwnershipState state,
        SonarrObservedState sonarr,
        string animeKey,
        string releaseKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(animeKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseKey);

        var mode = GetMode(state, animeKey);
        if (mode == AnimeManagementMode.ReadOnlyCoexistence)
        {
            return new(false, "Anime is in read-only Sonarr coexistence mode.");
        }

        if (sonarr.ActiveReleaseKeys.Contains(releaseKey))
        {
            return new(false, "Sonarr already owns an active job for this release.");
        }

        if (state.Jobs.Values.Any(job =>
                job.ReleaseKey is not null &&
                job.ReleaseKey.Equals(releaseKey, StringComparison.OrdinalIgnoreCase) &&
                job.Status is AcquisitionOwnershipStatus.Pending or AcquisitionOwnershipStatus.Importing or AcquisitionOwnershipStatus.Completed))
        {
            return new(false, "This release is already owned by an acquisition job.");
        }

        return new(true, "Release is not owned by Sonarr or another Jularr job.");
    }

    public static OwnershipDecision CanMutatePath(
        AcquisitionOwnershipState state,
        SonarrObservedState sonarr,
        string animeKey,
        string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(animeKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalized = NormalizePath(path);
        var mode = GetMode(state, animeKey);

        if (mode == AnimeManagementMode.ReadOnlyCoexistence)
        {
            return new(false, "Read-only coexistence forbids Jularr filesystem mutations.");
        }

        if (sonarr.ActivePaths.Any(item =>
                NormalizePath(item).Equals(normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return new(false, "Path is currently active in Sonarr.");
        }

        if (state.Paths.TryGetValue(normalized, out var owned))
        {
            if (owned.Owner != AcquisitionOwner.AniLingo)
            {
                return new(false, "Path is owned by Sonarr.");
            }

            if (!owned.AnimeKey.Equals(animeKey, StringComparison.OrdinalIgnoreCase))
            {
                return new(false, "Path belongs to another anime.");
            }

            return new(true, "Path is owned by Jularr for this anime.");
        }

        if (mode == AnimeManagementMode.ParallelAcquisition)
        {
            return new(false, "Parallel mode may mutate only paths explicitly owned by Jularr.");
        }

        return new(true, "AniLingo-managed mode allows unclaimed target paths when Sonarr has no active ownership.");
    }

    public static OwnershipDecision CanChangeMode(
        AcquisitionOwnershipState state,
        string animeKey,
        AnimeManagementMode target)
    {
        var current = GetMode(state, animeKey);
        if (current == target)
        {
            return new(true, "Anime already uses the requested management mode.");
        }

        var hasActiveAniLingoJobs = state.Jobs.Values.Any(job =>
            job.Owner == AcquisitionOwner.AniLingo &&
            job.AnimeKey.Equals(animeKey, StringComparison.OrdinalIgnoreCase) &&
            job.Status is AcquisitionOwnershipStatus.Pending or AcquisitionOwnershipStatus.Importing);

        if (hasActiveAniLingoJobs && target == AnimeManagementMode.ReadOnlyCoexistence)
        {
            return new(false, "Finish or cancel active Jularr jobs before handing the anime back to Sonarr.");
        }

        return new(true, "Management mode transition is safe.");
    }

    public static AcquisitionOwnershipState SetMode(
        AcquisitionOwnershipState state,
        string animeKey,
        AnimeManagementMode target,
        DateTimeOffset now)
    {
        var decision = CanChangeMode(state, animeKey, target);
        if (!decision.Allowed)
        {
            throw new InvalidOperationException(decision.Reason);
        }

        // Keep the Sonarr link and monitoring marker: a mode change must not forget which
        // Sonarr series manages the anime or which Sonarr change a revert has to restore.
        var assignment = state.Anime.TryGetValue(animeKey, out var existing)
            ? existing with { Mode = target, ChangedAtUtc = now }
            : new AnimeManagementAssignment(animeKey, target, now);

        var anime = new Dictionary<string, AnimeManagementAssignment>(
            state.Anime,
            StringComparer.OrdinalIgnoreCase)
        {
            [animeKey] = assignment
        };

        return state with { Anime = anime };
    }

    public static AcquisitionOwnershipState RegisterJob(
        AcquisitionOwnershipState state,
        AcquisitionOwnership job)
    {
        ArgumentNullException.ThrowIfNull(job);

        var jobs = new Dictionary<string, AcquisitionOwnership>(
            state.Jobs,
            StringComparer.OrdinalIgnoreCase)
        {
            [job.JobId] = job
        };

        return state with { Jobs = jobs };
    }

    public static AcquisitionOwnershipState RegisterPath(
        AcquisitionOwnershipState state,
        ManagedMediaPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var paths = new Dictionary<string, ManagedMediaPath>(
            state.Paths,
            StringComparer.OrdinalIgnoreCase)
        {
            [NormalizePath(path.Path)] = path with { Path = NormalizePath(path.Path) }
        };

        return state with { Paths = paths };
    }

    public static IReadOnlyList<OwnershipConflict> DetectConflicts(
        AcquisitionOwnershipState state,
        SonarrObservedState sonarr)
    {
        var conflicts = new List<OwnershipConflict>();

        foreach (var job in state.Jobs.Values.Where(job =>
                     job.Owner == AcquisitionOwner.AniLingo &&
                     job.ReleaseKey is not null &&
                     job.Status is AcquisitionOwnershipStatus.Pending or AcquisitionOwnershipStatus.Importing))
        {
            if (sonarr.ActiveReleaseKeys.Contains(job.ReleaseKey!))
            {
                conflicts.Add(new(
                    "release",
                    job.AnimeKey,
                    job.ReleaseKey!,
                    "Jularr and Sonarr both report an active job for the same release."));
            }
        }

        foreach (var path in state.Paths.Values.Where(path => path.Owner == AcquisitionOwner.AniLingo))
        {
            if (sonarr.ActivePaths.Any(item =>
                    NormalizePath(item).Equals(path.Path, StringComparison.OrdinalIgnoreCase)))
            {
                conflicts.Add(new(
                    "path",
                    path.AnimeKey,
                    path.Path,
                    "AniLingo-owned path is also active in Sonarr."));
            }
        }

        conflicts.AddRange(DetectSonarrConflicts(state, sonarr));
        return conflicts;
    }

    public static string NormalizePath(string path)
    {
        var full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
