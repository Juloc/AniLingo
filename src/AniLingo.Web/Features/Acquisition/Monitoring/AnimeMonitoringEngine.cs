using AniLingo.Web.Features.Acquisition;
using AniLingo.Web.Features.Acquisition.Ownership;
using AniLingo.Web.Features.Acquisition.Quality;

namespace AniLingo.Web.Features.Acquisition.Monitoring;

public static class AnimeMonitoringEngine
{
    public static IReadOnlyList<AnimeWantedEpisode> GetWanted(
        AnimeMonitoringState state,
        IEnumerable<AnimeEpisodeInventory> inventory,
        AnimeQualityProfile profile,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(profile);

        var wanted = new List<AnimeWantedEpisode>();

        foreach (var episode in inventory)
        {
            if (!IsMonitored(state, episode.Key))
            {
                continue;
            }

            if (episode.AirsAtUtc is DateTimeOffset airsAt && airsAt > now)
            {
                continue;
            }

            if (!episode.HasFile)
            {
                wanted.Add(new AnimeWantedEpisode(
                    episode.Key,
                    AnimeWantedReason.Missing,
                    now));
                continue;
            }

            if (episode.CurrentFile is not null &&
                profile.UpgradeAllowed &&
                !IsCutoffMet(profile, episode.CurrentFile))
            {
                wanted.Add(new AnimeWantedEpisode(
                    episode.Key,
                    AnimeWantedReason.CutoffUnmet,
                    now));
            }
        }

        return wanted;
    }

    public static AnimeMonitoringState RefreshWanted(
        AnimeMonitoringState state,
        IEnumerable<AnimeEpisodeInventory> inventory,
        AnimeQualityProfile profile,
        DateTimeOffset now)
    {
        var computed = GetWanted(state, inventory, profile, now);
        var wanted = new Dictionary<string, AnimeWantedEpisode>(StringComparer.OrdinalIgnoreCase);
        var history = state.History.ToList();

        foreach (var item in computed)
        {
            var id = item.Key.ToString();
            if (state.Wanted.TryGetValue(id, out var existing) && existing.Reason == item.Reason)
            {
                wanted[id] = existing;
                continue;
            }

            wanted[id] = item;
            history.Add(new AnimeMonitoringHistoryEntry(
                now,
                item.Key,
                "wanted",
                item.Reason.ToString()));
        }

        foreach (var previous in state.Wanted.Values)
        {
            if (wanted.ContainsKey(previous.Key.ToString()))
            {
                continue;
            }

            history.Add(new AnimeMonitoringHistoryEntry(
                now,
                previous.Key,
                "wanted-cleared",
                previous.Reason.ToString()));
        }

        TrimHistory(history);
        return state with
        {
            Wanted = wanted,
            History = history
        };
    }

    public static IReadOnlyList<AnimeSearchRequest> PlanSearches(
        AnimeMonitoringState state,
        IEnumerable<AnimeWantedEpisode> wanted,
        AnimeSearchTrigger trigger,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(wanted);

        var requests = new List<AnimeSearchRequest>();
        foreach (var item in wanted)
        {
            if (!state.Anime.TryGetValue(item.Key.AnimeKey, out var settings))
            {
                continue;
            }

            if (trigger == AnimeSearchTrigger.SearchOnAdd && !settings.SearchOnAdd)
            {
                continue;
            }

            if (state.Attempts.TryGetValue(item.Key.ToString(), out var attempt))
            {
                if (attempt.Status is AnimeAcquisitionAttemptStatus.Pending or AnimeAcquisitionAttemptStatus.Grabbed)
                {
                    continue;
                }

                if (attempt.NextRetryAtUtc is DateTimeOffset retryAt && retryAt > now)
                {
                    continue;
                }
            }

            requests.Add(new AnimeSearchRequest(item.Key, item.Reason, trigger));
        }

        return requests;
    }

    public static AnimeAutoGrabDecision EvaluateCandidate(
        AnimeQualityProfile profile,
        AnimeWantedEpisode wanted,
        AnimeReleaseScoreResult candidate,
        AnimeReleaseScoreResult? currentFile,
        AnimeMonitoringState state,
        AcquisitionOwnershipSnapshot? ownership = null,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(wanted);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(state);

        if (!candidate.Accepted)
        {
            return new(false, "Candidate is rejected by the assigned quality profile.", candidate);
        }

        var release = candidate.Candidate.Release;
        if (!MatchesEpisode(wanted.Key, release))
        {
            return new(false, "Candidate episode numbering does not match the wanted episode.", candidate);
        }

        if (state.Attempts.Values.Any(attempt =>
                attempt.ReleaseKey is not null &&
                attempt.ReleaseKey.Equals(release.ReleaseKey, StringComparison.OrdinalIgnoreCase) &&
                attempt.Status is AnimeAcquisitionAttemptStatus.Pending or AnimeAcquisitionAttemptStatus.Grabbed))
        {
            return new(false, "Release is already pending or was already grabbed.", candidate);
        }

        if (ownership is not null)
        {
            var key = wanted.Key;
            var ownershipDecision = SonarrParallelSafety.CanGrab(
                ownership,
                new AcquisitionGrabRequest(
                    key.AnimeKey,
                    release.ReleaseKey,
                    release.SeasonNumber ?? key.SeasonNumber,
                    release.EpisodeStart ?? key.EpisodeNumber,
                    release.EpisodeEnd ?? key.EpisodeNumber,
                    release.AbsoluteEpisodeStart ?? key.AbsoluteEpisodeNumber,
                    release.AbsoluteEpisodeEnd ?? key.AbsoluteEpisodeNumber),
                now ?? DateTimeOffset.UtcNow);
            if (!ownershipDecision.Allowed)
            {
                return new(false, $"Ownership: {ownershipDecision.Reason}", candidate);
            }
        }

        if (wanted.Reason == AnimeWantedReason.Missing)
        {
            return new(true, "Accepted candidate satisfies a missing monitored episode.", candidate);
        }

        if (currentFile is null)
        {
            return new(false, "Upgrade decision requires the current file score.", candidate);
        }

        return AnimeReleaseScorer.IsUpgrade(profile, currentFile, candidate)
            ? new(true, "Accepted candidate is an upgrade over the current file.", candidate)
            : new(false, "Candidate is not an upgrade over the current file.", candidate);
    }

    public static AnimeMonitoringState MarkPending(
        AnimeMonitoringState state,
        AnimeSearchRequest request,
        DateTimeOffset now)
    {
        var attempts = CloneAttempts(state);
        var key = request.Key.ToString();
        attempts[key] = attempts.TryGetValue(key, out var existing)
            ? existing with
            {
                Status = AnimeAcquisitionAttemptStatus.Pending,
                LastAttemptAtUtc = now,
                NextRetryAtUtc = null
            }
            : new AnimeAcquisitionAttempt(
                request.Key,
                AnimeAcquisitionAttemptStatus.Pending,
                null,
                0,
                now,
                null);

        return WithHistory(state, attempts, request.Key, now, "search-pending", request.Reason.ToString());
    }

    public static AnimeMonitoringState MarkGrabbed(
        AnimeMonitoringState state,
        AnimeEpisodeKey key,
        string releaseKey,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseKey);

        var attempts = CloneAttempts(state);
        var id = key.ToString();
        var existing = attempts.TryGetValue(id, out var found)
            ? found
            : new AnimeAcquisitionAttempt(key, AnimeAcquisitionAttemptStatus.None, null, 0, null, null);

        attempts[id] = existing with
        {
            Status = AnimeAcquisitionAttemptStatus.Grabbed,
            ReleaseKey = releaseKey,
            LastAttemptAtUtc = now,
            NextRetryAtUtc = null
        };

        return WithHistory(state, attempts, key, now, "grabbed", releaseKey);
    }

    public static AnimeMonitoringState MarkFailed(
        AnimeMonitoringState state,
        AnimeEpisodeKey key,
        string? releaseKey,
        DateTimeOffset now,
        TimeSpan? baseDelay = null,
        int maxExponent = 5)
    {
        var attempts = CloneAttempts(state);
        var id = key.ToString();
        var existing = attempts.TryGetValue(id, out var found)
            ? found
            : new AnimeAcquisitionAttempt(key, AnimeAcquisitionAttemptStatus.None, null, 0, null, null);

        var failureCount = checked(existing.FailureCount + 1);
        var delay = baseDelay ?? TimeSpan.FromMinutes(5);
        var exponent = Math.Min(Math.Max(failureCount - 1, 0), maxExponent);
        var multiplier = Math.Pow(2, exponent);
        var retryDelay = TimeSpan.FromTicks((long)Math.Min(
            delay.Ticks * multiplier,
            TimeSpan.FromHours(6).Ticks));

        attempts[id] = existing with
        {
            Status = AnimeAcquisitionAttemptStatus.Failed,
            ReleaseKey = releaseKey ?? existing.ReleaseKey,
            FailureCount = failureCount,
            LastAttemptAtUtc = now,
            NextRetryAtUtc = now + retryDelay
        };

        return WithHistory(
            state,
            attempts,
            key,
            now,
            "failed",
            $"Retry after {retryDelay}.");
    }

    public static AnimeMonitoringState ClearAttempt(
        AnimeMonitoringState state,
        AnimeEpisodeKey key,
        DateTimeOffset now,
        string reason)
    {
        var attempts = CloneAttempts(state);
        attempts.Remove(key.ToString());
        return WithHistory(state, attempts, key, now, "attempt-cleared", reason);
    }

    public static bool IsMonitored(AnimeMonitoringState state, AnimeEpisodeKey key)
    {
        if (!state.Anime.TryGetValue(key.AnimeKey, out var settings))
        {
            return false;
        }

        var episodeKey = EpisodeOverrideKey(key.SeasonNumber, key.EpisodeNumber);
        if (settings.EpisodeOverrides.TryGetValue(episodeKey, out var episodeOverride))
        {
            return episodeOverride;
        }

        if (settings.SeasonOverrides.TryGetValue(key.SeasonNumber, out var seasonOverride))
        {
            return seasonOverride;
        }

        return settings.Monitored;
    }

    public static bool IsCutoffMet(
        AnimeQualityProfile profile,
        AnimeReleaseScoreResult current)
    {
        if (string.IsNullOrWhiteSpace(profile.UpgradeCutoffQuality))
        {
            return false;
        }

        var cutoffRank = QualityRank(profile, profile.UpgradeCutoffQuality);
        if (cutoffRank == int.MaxValue)
        {
            return false;
        }

        return current.QualityRank <= cutoffRank;
    }

    public static string EpisodeOverrideKey(int season, int episode) =>
        $"S{season:00}E{episode:00}";

    private static bool MatchesEpisode(AnimeEpisodeKey key, AnimeReleaseInfo release)
    {
        if (release.SeasonNumber is int season &&
            release.EpisodeStart is int start &&
            release.EpisodeEnd is int end)
        {
            return season == key.SeasonNumber &&
                   key.EpisodeNumber >= start &&
                   key.EpisodeNumber <= end;
        }

        if (key.AbsoluteEpisodeNumber is int absolute &&
            release.AbsoluteEpisodeStart is int absoluteStart &&
            release.AbsoluteEpisodeEnd is int absoluteEnd)
        {
            return absolute >= absoluteStart && absolute <= absoluteEnd;
        }

        return false;
    }

    private static int QualityRank(AnimeQualityProfile profile, string quality)
    {
        for (var i = 0; i < profile.QualityOrder.Length; i++)
        {
            if (profile.QualityOrder[i].Equals(quality, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private static Dictionary<string, AnimeAcquisitionAttempt> CloneAttempts(AnimeMonitoringState state) =>
        new(state.Attempts, StringComparer.OrdinalIgnoreCase);

    private static AnimeMonitoringState WithHistory(
        AnimeMonitoringState state,
        Dictionary<string, AnimeAcquisitionAttempt> attempts,
        AnimeEpisodeKey key,
        DateTimeOffset now,
        string eventName,
        string reason)
    {
        var history = state.History.ToList();
        history.Add(new AnimeMonitoringHistoryEntry(now, key, eventName, reason));

        TrimHistory(history);

        return state with
        {
            Attempts = attempts,
            History = history
        };
    }

    private static void TrimHistory(List<AnimeMonitoringHistoryEntry> history)
    {
        const int maxHistory = 2_000;
        if (history.Count > maxHistory)
        {
            history.RemoveRange(0, history.Count - maxHistory);
        }
    }
}
