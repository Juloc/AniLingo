using AniLingo.Web.Data;
using AniLingo.Web.Features.Acquisition.Import;
using AniLingo.Web.Features.Acquisition.Monitoring;
using AniLingo.Web.Features.Acquisition.Ownership;
using AniLingo.Web.Features.Acquisition.Prowlarr;
using AniLingo.Web.Features.Acquisition.Quality;
using AniLingo.Web.Features.Acquisition.Sabnzbd;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Features.Sonarr;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Acquisition.Pipeline;

public sealed record AnimeAcquisitionRunSummary(
    int AnimeCount,
    int Searches,
    int Grabs,
    IReadOnlyList<string> Notes)
{
    public override string ToString() =>
        $"{AnimeCount} anime, {Searches} searches, {Grabs} grabs" +
        (Notes.Count == 0 ? "" : $" — {string.Join(" ", Notes)}");
}

public sealed record AnimeSearchCandidate(
    ProwlarrReleaseCandidate Release,
    AnimeReleaseScoreResult Score,
    AnimeAutoGrabDecision Decision,
    IReadOnlyList<AnimeEpisodeKey> CoveredEpisodes);

public sealed record AnimeInteractiveSearch(
    AnimeAcquisitionTarget Target,
    AnimeEpisodeKey? Episode,
    ProwlarrAnimeSearchMode Mode,
    IReadOnlyList<AnimeSearchCandidate> Candidates,
    IReadOnlyList<ProwlarrSearchWarning> Warnings,
    string? Error);

public sealed record AnimeGrabResult(bool Success, string Message, Guid? OperationId = null);

/// <summary>
/// The one anime acquisition pipeline: refreshes wanted episodes from the library/AniList
/// inventory, searches Prowlarr, scores releases with the assigned quality profile, checks Sonarr
/// ownership and hands the best accepted release to SABnzbd. Every decision is written to the
/// Operation of that search so the owner can see why a release was accepted or rejected.
/// Callers serialize runs through <see cref="AnimeAcquisitionScheduler"/>.
/// </summary>
public sealed class AnimeAcquisitionPipeline(
    AppDbContext db,
    AnimeMonitoringStore monitoring,
    AnimeQualityProfileStore profiles,
    ProwlarrSettingsStore prowlarrSettings,
    ProwlarrAnimeSearchService prowlarr,
    SonarrObservationService observation,
    AcquisitionOwnershipStore ownershipStore,
    SabnzbdAcquisitionStore acquisitions,
    SabnzbdAcquisitionService sabnzbd,
    AnimeImportStore imports,
    AnimeAcquisitionInventory inventory,
    ILogger<AnimeAcquisitionPipeline> logger)
{
    public const string SearchOperationKind = "anime-search";
    public const string GrabOperationKind = "anime-grab";
    public const string OperationCategory = "Acquisition";
    public const string LogModule = "Acquisition";
    public const int MaxSearchesPerAnimePerRun = 6;
    public const int MaxSearchesPerRun = 30;
    public const int MaxLoggedDecisions = 25;
    private const int MaxSearchAliases = 3;

    public async Task<AnimeAcquisitionRunSummary> RunAsync(
        AnimeSearchTrigger trigger,
        string? animeKey,
        CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        await ReconcileAttemptsAsync(cancellationToken);

        var state = await monitoring.LoadAsync(cancellationToken);
        var keys = animeKey is null
            ? state.Anime.Values.Where(settings => settings.Monitored).Select(settings => settings.AnimeKey).ToArray()
            : [animeKey];
        if (keys.Length == 0)
        {
            return new(0, 0, 0, ["No anime is monitored."]);
        }

        var connection = await prowlarrSettings.LoadAsync(cancellationToken);
        if (connection is null)
        {
            notes.Add("Prowlarr is not configured; wanted episodes were refreshed but not searched.");
        }

        var snapshot = await observation.GetSnapshotAsync(forceRefresh: true, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var searches = 0;
        var grabs = 0;

        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = await inventory.LoadAsync(key, cancellationToken);
            if (target is null)
            {
                notes.Add($"{key}: anime no longer exists.");
                continue;
            }

            if (target.Diagnostic is not null)
            {
                notes.Add($"{target.Anime.Title}: {target.Diagnostic}");
            }

            state = await monitoring.UpdateAsync(
                current => AnimeMonitoringEngine.RefreshWantedForAnime(
                    current,
                    key,
                    target.Episodes.Select(episode => AnimeAcquisitionInventory.ToInventory(episode, target.Profile)),
                    target.Profile,
                    now),
                cancellationToken);

            if (connection is null)
            {
                continue;
            }

            var wanted = state.Wanted.Values
                .Where(item => item.Key.AnimeKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Key.SeasonNumber)
                .ThenBy(item => item.Key.EpisodeNumber)
                .ToArray();
            var requests = AnimeMonitoringEngine
                .PlanSearches(state, wanted, trigger, now)
                .Take(Math.Min(MaxSearchesPerAnimePerRun, MaxSearchesPerRun - searches))
                .ToArray();

            foreach (var request in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                searches++;
                var slot = target.Find(request.Key.SeasonNumber, request.Key.EpisodeNumber);
                if (slot is null)
                {
                    continue;
                }

                var wantedEpisode = wanted.First(item => item.Key == request.Key);
                var grabbed = await SearchAndGrabAsync(
                    target,
                    slot,
                    wantedEpisode,
                    wanted,
                    request,
                    connection,
                    snapshot,
                    cancellationToken);
                if (grabbed)
                {
                    grabs++;
                }
            }

            if (searches >= MaxSearchesPerRun)
            {
                notes.Add($"Search limit of {MaxSearchesPerRun} per run reached; remaining wanted episodes wait for the next run.");
                break;
            }
        }

        return new(keys.Length, searches, grabs, notes);
    }

    public async Task<AnimeInteractiveSearch?> SearchInteractiveAsync(
        string animeKey,
        int? seasonNumber,
        int? episodeNumber,
        ProwlarrAnimeSearchMode mode,
        CancellationToken cancellationToken)
    {
        var target = await inventory.LoadAsync(animeKey, cancellationToken);
        if (target is null)
        {
            return null;
        }

        var episode = seasonNumber is { } season && episodeNumber is { } number
            ? target.Find(season, number)
              ?? new AnimeAcquisitionEpisode(new AnimeEpisodeKey(animeKey, season, number), null, null, target.Anime.Title, target.AllTitles)
            : null;
        if (mode == ProwlarrAnimeSearchMode.Episode && episode is null)
        {
            return new(target, null, mode, [], [], "Choose an episode to search.");
        }

        var connection = await prowlarrSettings.LoadAsync(cancellationToken);
        if (connection is null)
        {
            return new(target, episode?.Key, mode, [], [], "Prowlarr is not configured.");
        }

        var now = DateTimeOffset.UtcNow;
        var state = await monitoring.LoadAsync(cancellationToken);
        var scope = mode switch
        {
            ProwlarrAnimeSearchMode.Episode => [episode!],
            ProwlarrAnimeSearchMode.Season => target.Episodes.Where(item => item.Key.SeasonNumber == (seasonNumber ?? episode?.Key.SeasonNumber ?? 1)).ToArray(),
            _ => target.Episodes.ToArray()
        };
        var wanted = scope
            .Select(item => new AnimeWantedEpisode(item.Key, item.HasFile ? AnimeWantedReason.CutoffUnmet : AnimeWantedReason.Missing, now))
            .ToArray();
        var searchTarget = mode switch
        {
            ProwlarrAnimeSearchMode.Episode => SearchTargetFor(episode!),
            ProwlarrAnimeSearchMode.Season => new ProwlarrAnimeSearchTarget(
                scope.FirstOrDefault()?.SearchTitle ?? target.Anime.Title,
                Aliases(scope.FirstOrDefault()?.SearchAliases ?? target.AllTitles, scope.FirstOrDefault()?.SearchTitle ?? target.Anime.Title),
                ProwlarrAnimeSearchMode.Season,
                seasonNumber ?? episode?.Key.SeasonNumber ?? 1),
            _ => new ProwlarrAnimeSearchTarget(target.Anime.Title, Aliases(target.AllTitles, target.Anime.Title), ProwlarrAnimeSearchMode.Anime)
        };

        try
        {
            var result = await prowlarr.SearchAsync(ConnectionFor(connection, state, animeKey), searchTarget, cancellationToken);
            var snapshot = await observation.GetSnapshotAsync(forceRefresh: false, cancellationToken);
            var candidates = Evaluate(target, scope, wanted, episode?.Key, result.Releases, state, snapshot, now);
            return new(target, episode?.Key, mode, candidates, result.Warnings, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is ProwlarrException or HttpRequestException or TaskCanceledException)
        {
            return new(target, episode?.Key, mode, [], [], $"Prowlarr search failed: {exception.Message}");
        }
    }

    /// <summary>
    /// Owner grab of one search result. Ownership and duplicate protection always apply; a
    /// profile rejection may be overridden because the owner chose the release explicitly.
    /// </summary>
    public async Task<AnimeGrabResult> GrabAsync(
        string animeKey,
        int? seasonNumber,
        int? episodeNumber,
        ProwlarrAnimeSearchMode mode,
        string releaseIdentity,
        CancellationToken cancellationToken)
    {
        var search = await SearchInteractiveAsync(animeKey, seasonNumber, episodeNumber, mode, cancellationToken);
        if (search is null)
        {
            return new(false, "Anime not found.");
        }

        if (search.Error is not null)
        {
            return new(false, search.Error);
        }

        var candidate = search.Candidates.FirstOrDefault(item =>
            item.Release.Identity.Equals(releaseIdentity, StringComparison.OrdinalIgnoreCase));
        if (candidate is null)
        {
            return new(false, "The release is no longer offered by Prowlarr; search again.");
        }

        if (candidate.Release.InternalDownloadUri is null)
        {
            return new(false, "The release has no NZB link.");
        }

        var episodes = candidate.CoveredEpisodes.Count > 0
            ? candidate.CoveredEpisodes
            : search.Episode is { } key ? [key] : [];
        if (episodes.Count == 0)
        {
            return new(false, "The release does not cover an episode of this anime.");
        }

        var now = DateTimeOffset.UtcNow;
        var snapshot = await observation.GetSnapshotAsync(forceRefresh: true, cancellationToken);
        var release = candidate.Release.ParsedRelease;
        var ownership = SonarrParallelSafety.CanGrab(
            snapshot,
            new AcquisitionGrabRequest(
                animeKey,
                release.ReleaseKey,
                release.SeasonNumber ?? episodes[0].SeasonNumber,
                release.EpisodeStart ?? episodes[0].EpisodeNumber,
                release.EpisodeEnd ?? episodes[^1].EpisodeNumber,
                release.AbsoluteEpisodeStart ?? episodes[0].AbsoluteEpisodeNumber,
                release.AbsoluteEpisodeEnd ?? episodes[^1].AbsoluteEpisodeNumber),
            now);
        if (!ownership.Allowed)
        {
            return new(false, $"Ownership: {ownership.Reason}");
        }

        var state = await monitoring.LoadAsync(cancellationToken);
        if (state.Attempts.Values.Any(attempt =>
                attempt.ReleaseKey is not null &&
                attempt.ReleaseKey.Equals(release.ReleaseKey, StringComparison.OrdinalIgnoreCase) &&
                attempt.Status is AnimeAcquisitionAttemptStatus.Pending or AnimeAcquisitionAttemptStatus.Grabbed))
        {
            return new(false, "This release is already pending or was already grabbed.");
        }

        var operations = new OperationStore(db);
        var operationId = await operations.CreateAsync(
            new OperationDescriptor(
                GrabOperationKind,
                OperationCategory,
                "Anime grab",
                $"{search.Target.Anime.Title} · {SabnzbdAcquisitionService.FormatEpisodes(episodes)} · {candidate.Release.Title}",
                search.Target.Profile.Id,
                OperationLane.Interactive,
                Retryable: false),
            cancellationToken);
        await operations.MarkRunningAsync(operationId, cancellationToken);
        await operations.AppendLogAsync(
            operationId,
            candidate.Decision.Grab ? OperationLogLevel.Information : OperationLogLevel.Warning,
            LogModule,
            candidate.Decision.Grab
                ? $"Owner grab: {Describe(candidate)}"
                : $"Owner override of a rejected release: {Describe(candidate)}",
            cancellationToken);

        var grabbed = await SubmitAsync(search.Target, episodes, [candidate], operationId, cancellationToken);
        return grabbed is null
            ? new(false, "SABnzbd did not accept the release; see the grab operation for details.", operationId)
            : new(true, $"Sent to SABnzbd: {grabbed.Release.Title}", operationId);
    }

    /// <summary>
    /// Brings persisted monitoring attempts in line with the acquisition relation, Operations and
    /// import records, so restarts never leave an episode stuck as pending/grabbed or grab twice.
    /// </summary>
    public async Task ReconcileAttemptsAsync(CancellationToken cancellationToken)
    {
        var state = await monitoring.LoadAsync(cancellationToken);
        if (state.Attempts.Count == 0)
        {
            return;
        }

        var relations = await acquisitions.LoadAsync(cancellationToken);
        var importState = await imports.LoadAsync(cancellationToken);
        var operations = new OperationStore(db);
        var now = DateTimeOffset.UtcNow;
        var updates = new List<Func<AnimeMonitoringState, AnimeMonitoringState>>();
        var exhausted = new HashSet<Guid>();

        foreach (var attempt in state.Attempts.Values)
        {
            var key = attempt.Key;
            switch (attempt.Status)
            {
                case AnimeAcquisitionAttemptStatus.Pending:
                    updates.Add(current => AnimeMonitoringEngine.ClearAttempt(current, key, now, "Search was interrupted before a grab; it will be retried."));
                    break;

                case AnimeAcquisitionAttemptStatus.Grabbed:
                    var acquisition = relations.Acquisitions
                        .Where(item => item.Episodes.Contains(key) && item.LatestAttempt is not null)
                        .MaxBy(item => item.UpdatedAtUtc);
                    if (acquisition is null)
                    {
                        updates.Add(current => AnimeMonitoringEngine.ClearAttempt(current, key, now, "No acquisition exists for the grab; it will be searched again."));
                        break;
                    }

                    var operation = await operations.GetAsync(acquisition.LatestAttempt!.OperationId, cancellationToken);
                    if (operation is null || operation.IsActive)
                    {
                        break;
                    }

                    if (operation.Status == OperationStatus.Succeeded)
                    {
                        var import = importState.Imports.FirstOrDefault(record => record.DownloadOperationId == operation.Id);
                        if (import?.Status == AnimeImportStatus.Imported)
                        {
                            updates.Add(current => AnimeMonitoringEngine.ClearAttempt(current, key, now, "Imported into the library."));
                        }
                        else if (import?.Status is AnimeImportStatus.Failed or AnimeImportStatus.Dismissed)
                        {
                            updates.Add(current => AnimeMonitoringEngine.MarkFailed(current, key, attempt.ReleaseKey, now));
                        }

                        break;
                    }

                    var canAdvance = acquisition.Attempts.Length < acquisition.MaxAttempts &&
                                     acquisition.PendingCandidates.Any(candidate => !relations.IsBlocked(candidate.ReleaseIdentity));
                    if (!canAdvance)
                    {
                        updates.Add(current => AnimeMonitoringEngine.MarkFailed(current, key, attempt.ReleaseKey, now));
                        exhausted.Add(acquisition.Id);
                    }

                    break;
            }
        }

        if (updates.Count > 0)
        {
            await monitoring.UpdateAsync(
                current => updates.Aggregate(current, (accumulated, update) => update(accumulated)),
                cancellationToken);
        }

        if (exhausted.Count > 0)
        {
            await ownershipStore.UpdateAsync(
                current => exhausted.Aggregate(current, (accumulated, id) =>
                    accumulated.Jobs.TryGetValue(id.ToString(), out var job) && job.Status == AcquisitionOwnershipStatus.Pending
                        ? SonarrParallelSafety.RegisterJob(accumulated, job with { Status = AcquisitionOwnershipStatus.Failed, UpdatedAtUtc = now })
                        : accumulated),
                cancellationToken);
        }
    }

    public async Task UpdateAnimeSettingsAsync(
        Guid animeId,
        string animeKey,
        bool monitored,
        bool searchOnAdd,
        string? profileId,
        int[] indexerIds,
        CancellationToken cancellationToken)
    {
        var profileState = await profiles.LoadAsync(cancellationToken);
        await profiles.AssignAnimeAsync(
            animeId,
            profileId is not null && !profileId.Equals(profileState.DefaultProfileId, StringComparison.OrdinalIgnoreCase)
                ? profileId
                : null,
            cancellationToken);

        await monitoring.UpdateAsync(
            current =>
            {
                var anime = new Dictionary<string, AnimeMonitorSettings>(current.Anime, StringComparer.OrdinalIgnoreCase);
                var existing = anime.TryGetValue(animeKey, out var found) ? found : null;
                anime[animeKey] = new AnimeMonitorSettings(
                    animeKey,
                    monitored,
                    searchOnAdd,
                    existing?.SeasonOverrides ?? [],
                    existing?.EpisodeOverrides ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
                    indexerIds.Length == 0 ? null : indexerIds.Where(id => id > 0).Distinct().Order().ToArray());
                return current with { Anime = anime };
            },
            cancellationToken);
    }

    public Task UpdateScheduleAsync(
        bool enabled,
        int intervalMinutes,
        CancellationToken cancellationToken) =>
        monitoring.UpdateAsync(
            current => current with
            {
                Schedule = new AnimeMonitoringSchedule(
                    enabled,
                    Math.Clamp(intervalMinutes, AnimeMonitoringSchedule.MinimumIntervalMinutes, AnimeMonitoringSchedule.MaximumIntervalMinutes))
            },
            cancellationToken);

    public async Task<AnimeAcquisitionPanel> GetAnimePanelAsync(
        Guid animeId,
        string animeKey,
        CancellationToken cancellationToken)
    {
        var state = await monitoring.LoadAsync(cancellationToken);
        var profileState = await profiles.LoadAsync(cancellationToken);
        var ownership = await ownershipStore.LoadAsync(cancellationToken);
        var relations = await acquisitions.LoadAsync(cancellationToken);
        var prowlarrConfigured = await prowlarrSettings.LoadAsync(cancellationToken) is not null;

        state.Anime.TryGetValue(animeKey, out var settings);
        var assigned = profileState.AnimeProfileAssignments.TryGetValue(animeId.ToString("D"), out var profileId)
            ? profileId
            : profileState.DefaultProfileId;
        var active = await CountActiveDownloadsAsync(relations, animeKey, cancellationToken);
        var lastEvent = state.History
            .LastOrDefault(entry => entry.Key.AnimeKey.Equals(animeKey, StringComparison.OrdinalIgnoreCase));

        return new AnimeAcquisitionPanel(
            animeId,
            animeKey,
            SonarrParallelSafety.GetMode(ownership, animeKey),
            settings,
            assigned,
            profileState.Profiles,
            state.Wanted.Values.Count(item => item.Key.AnimeKey.Equals(animeKey, StringComparison.OrdinalIgnoreCase)),
            active,
            lastEvent is null ? null : $"{lastEvent.AtUtc:u} · {lastEvent.Key} · {lastEvent.Event}: {lastEvent.Reason}",
            prowlarrConfigured);
    }

    public async Task<AnimeAcquisitionOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var state = await monitoring.LoadAsync(cancellationToken);
        var ownership = await ownershipStore.LoadAsync(cancellationToken);
        var relations = await acquisitions.LoadAsync(cancellationToken);
        var importState = await imports.LoadAsync(cancellationToken);
        var profileState = await profiles.LoadAsync(cancellationToken);
        var operations = new OperationStore(db);

        var keys = state.Anime.Keys
            .Concat(state.Wanted.Values.Select(item => item.Key.AnimeKey))
            .Concat(relations.Acquisitions.Select(item => item.AnimeKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var anime = await db.Anime
            .AsNoTracking()
            .Where(item => keys.Contains(item.Key))
            .Select(item => new { item.Id, item.Key, item.Title })
            .ToDictionaryAsync(item => item.Key, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var monitored = state.Anime.Values
            .Where(settings => settings.Monitored && anime.ContainsKey(settings.AnimeKey))
            .Select(settings =>
            {
                var entry = anime[settings.AnimeKey];
                var profile = profileState.AnimeProfileAssignments.TryGetValue(entry.Id.ToString("D"), out var assigned)
                    ? assigned
                    : profileState.DefaultProfileId;
                return new AnimeMonitoredRow(
                    entry.Id,
                    entry.Key,
                    entry.Title,
                    SonarrParallelSafety.GetMode(ownership, entry.Key),
                    profile,
                    state.Wanted.Values.Count(item => item.Key.AnimeKey.Equals(entry.Key, StringComparison.OrdinalIgnoreCase)),
                    settings.IndexerIds ?? []);
            })
            .OrderBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var wanted = state.Wanted.Values
            .Select(item =>
            {
                state.Attempts.TryGetValue(item.Key.ToString(), out var attempt);
                return new AnimeWantedRow(
                    item.Key,
                    anime.TryGetValue(item.Key.AnimeKey, out var entry) ? entry.Title : item.Key.AnimeKey,
                    anime.TryGetValue(item.Key.AnimeKey, out var known) ? known.Id : null,
                    item.Reason,
                    item.BecameWantedAtUtc,
                    attempt);
            })
            .OrderBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Key.SeasonNumber)
            .ThenBy(row => row.Key.EpisodeNumber)
            .ToArray();

        var downloads = new List<AnimeDownloadRow>();
        foreach (var acquisition in relations.Acquisitions.Where(item => item.LatestAttempt is not null).OrderByDescending(item => item.UpdatedAtUtc).Take(50))
        {
            var operation = await operations.GetAsync(acquisition.LatestAttempt!.OperationId, cancellationToken);
            if (operation is null || !operation.IsActive)
            {
                continue;
            }

            downloads.Add(new AnimeDownloadRow(acquisition, acquisition.LatestAttempt, operation));
        }

        var attention = importState.Imports
            .Where(record => record.NeedsAttention)
            .OrderByDescending(record => record.UpdatedAtUtc)
            .ToArray();
        var recentImports = importState.Imports
            .Where(record => !record.NeedsAttention)
            .OrderByDescending(record => record.UpdatedAtUtc)
            .Take(10)
            .ToArray();

        var decisions = (await operations.ListLogsAsync(new OperationLogFilter(Module: LogModule, Limit: 60), cancellationToken))
            .Concat(await operations.ListLogsAsync(new OperationLogFilter(Module: AnimeImportExecutor.LogModule, Limit: 40), cancellationToken))
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Take(80)
            .ToArray();

        return new AnimeAcquisitionOverview(
            state.Schedule,
            monitored,
            wanted,
            downloads,
            attention,
            recentImports,
            decisions,
            state.History.AsEnumerable().Reverse().Take(30).ToArray());
    }

    private async Task<bool> SearchAndGrabAsync(
        AnimeAcquisitionTarget target,
        AnimeAcquisitionEpisode episode,
        AnimeWantedEpisode wanted,
        IReadOnlyList<AnimeWantedEpisode> allWanted,
        AnimeSearchRequest request,
        ProwlarrConnection connection,
        AcquisitionOwnershipSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var operations = new OperationStore(db);
        var now = DateTimeOffset.UtcNow;
        var operationId = await operations.CreateAsync(
            new OperationDescriptor(
                SearchOperationKind,
                OperationCategory,
                "Anime search",
                $"{target.Anime.Title} · {Label(episode.Key)} · {wanted.Reason}",
                target.Profile.Id,
                OperationLane.Normal,
                Retryable: false),
            cancellationToken);
        await operations.MarkRunningAsync(operationId, cancellationToken);

        var state = await monitoring.UpdateAsync(
            current => AnimeMonitoringEngine.MarkPending(current, request, now),
            cancellationToken);

        try
        {
            var result = await prowlarr.SearchAsync(
                ConnectionFor(connection, state, target.Anime.Key),
                SearchTargetFor(episode),
                cancellationToken);
            foreach (var warning in result.Warnings)
            {
                await operations.AppendLogAsync(operationId, OperationLogLevel.Warning, LogModule, $"Prowlarr: {warning.Message} ({warning.Query})", cancellationToken);
            }

            var candidates = Evaluate(target, [episode], allWanted, episode.Key, result.Releases, state, snapshot, now);
            await LogDecisionsAsync(operations, operationId, candidates, cancellationToken);

            var accepted = candidates.Where(candidate => candidate.Decision.Grab).ToArray();
            if (accepted.Length == 0)
            {
                await monitoring.UpdateAsync(current => AnimeMonitoringEngine.MarkFailed(current, episode.Key, null, now), cancellationToken);
                await operations.MarkSucceededAsync(
                    operationId,
                    $"No accepted release among {candidates.Count} result(s); retried after backoff.",
                    CancellationToken.None);
                return false;
            }

            var episodes = accepted[0].CoveredEpisodes.Count > 0 ? accepted[0].CoveredEpisodes : [episode.Key];
            var grabbed = await SubmitAsync(target, episodes, accepted, operationId, cancellationToken);
            return grabbed is not null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is ProwlarrException or HttpRequestException or TaskCanceledException)
        {
            await monitoring.UpdateAsync(current => AnimeMonitoringEngine.MarkFailed(current, episode.Key, null, now), cancellationToken);
            await operations.MarkFailedAsync(operationId, $"Prowlarr search failed: {exception.Message}", CancellationToken.None);
            return false;
        }
    }

    // Sends the accepted candidates (best first) to SABnzbd, records the grab on the monitoring
    // state and registers the AniLingo ownership job for the release that was actually submitted.
    private async Task<AnimeSearchCandidate?> SubmitAsync(
        AnimeAcquisitionTarget target,
        IReadOnlyList<AnimeEpisodeKey> episodes,
        IReadOnlyList<AnimeSearchCandidate> candidates,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var operations = new OperationStore(db);
        var now = DateTimeOffset.UtcNow;
        SabnzbdAcquisitionResult result;
        try
        {
            result = await sabnzbd.StartAsync(
                new SabnzbdAnimeAcquisitionRequest(
                    target.Anime.Key,
                    target.Anime.Title,
                    episodes,
                    target.Profile.Id,
                    candidates
                        .Where(candidate => candidate.Release.InternalDownloadUri is not null)
                        .Select(candidate => new SabnzbdAnimeReleaseCandidate(
                            candidate.Release.Identity,
                            candidate.Release.Title,
                            candidate.Release.InternalDownloadUri!))
                        .ToArray()),
                cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException)
        {
            await monitoring.UpdateAsync(
                current => episodes.Aggregate(current, (accumulated, key) => AnimeMonitoringEngine.MarkFailed(accumulated, key, null, now)),
                cancellationToken);
            await operations.MarkFailedAsync(operationId, $"SABnzbd submission failed: {exception.Message}", CancellationToken.None);
            return null;
        }

        var acquisition = await acquisitions.GetAsync(result.AcquisitionId, cancellationToken);
        var identity = acquisition?.LatestAttempt?.ReleaseIdentity;
        var chosen = candidates.FirstOrDefault(candidate =>
            identity is not null && candidate.Release.Identity.Equals(identity, StringComparison.OrdinalIgnoreCase));

        if (!result.Submitted || chosen is null)
        {
            await monitoring.UpdateAsync(
                current => episodes.Aggregate(current, (accumulated, key) => AnimeMonitoringEngine.MarkFailed(accumulated, key, chosen?.Release.ParsedRelease.ReleaseKey, now)),
                cancellationToken);
            await operations.MarkFailedAsync(operationId, result.Message, CancellationToken.None);
            return null;
        }

        var releaseKey = chosen.Release.ParsedRelease.ReleaseKey;
        var download = result.OperationId is { } downloadId ? await operations.GetAsync(downloadId, cancellationToken) : null;
        await ownershipStore.UpdateAsync(
            current => SonarrParallelSafety.RegisterJob(
                current,
                new AcquisitionOwnership(
                    result.AcquisitionId.ToString(),
                    target.Anime.Key,
                    AcquisitionOwner.AniLingo,
                    releaseKey,
                    AcquisitionOwnershipStatus.Pending,
                    now,
                    download?.ExternalId)),
            cancellationToken);
        await monitoring.UpdateAsync(
            current => episodes.Aggregate(current, (accumulated, key) => AnimeMonitoringEngine.MarkGrabbed(accumulated, key, releaseKey, now)),
            cancellationToken);

        var message = $"Sent to SABnzbd: {chosen.Release.Title} for {SabnzbdAcquisitionService.FormatEpisodes(episodes)}.";
        await operations.AppendLogAsync(operationId, OperationLogLevel.Information, LogModule, message, cancellationToken);
        await operations.MarkSucceededAsync(operationId, message, CancellationToken.None);
        logger.LogInformation("Anime acquisition grabbed {Release} for {Anime} {Episodes}.", chosen.Release.Title, target.Anime.Key, SabnzbdAcquisitionService.FormatEpisodes(episodes));
        return chosen;
    }

    // Scores every Prowlarr result with the profile and records the reason for each decision.
    private static IReadOnlyList<AnimeSearchCandidate> Evaluate(
        AnimeAcquisitionTarget target,
        IReadOnlyList<AnimeAcquisitionEpisode> scope,
        IReadOnlyList<AnimeWantedEpisode> wanted,
        AnimeEpisodeKey? primary,
        IReadOnlyList<ProwlarrReleaseCandidate> releases,
        AnimeMonitoringState state,
        AcquisitionOwnershipSnapshot snapshot,
        DateTimeOffset now)
    {
        var byIdentity = releases
            .GroupBy(release => release.Identity, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var ranked = AnimeReleaseScorer.Rank(
            target.Profile,
            byIdentity.Values.Select(release => new AnimeReleaseCandidate(
                release.ParsedRelease,
                release.SizeBytes,
                release.Indexer,
                release.Identity)));
        var aliases = scope
            .SelectMany(episode => new[] { episode.SearchTitle }.Concat(episode.SearchAliases))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var candidates = new List<AnimeSearchCandidate>();
        foreach (var score in ranked)
        {
            var release = byIdentity[score.Candidate.SourceId!];
            var parsed = release.ParsedRelease;
            var covered = wanted
                .Where(item => Covers(parsed, item.Key))
                .Select(item => item.Key)
                .ToArray();
            var wantedEpisode = primary is not null
                ? wanted.FirstOrDefault(item => item.Key == primary)
                : covered.Length > 0
                    ? wanted.First(item => item.Key == covered[0])
                    : null;

            AnimeAutoGrabDecision decision;
            if (release.InternalDownloadUri is null ||
                !string.Equals(release.Protocol, "usenet", StringComparison.OrdinalIgnoreCase))
            {
                decision = new(false, "Not a usenet release with an NZB link; only SABnzbd downloads are supported.", score);
            }
            else if (!CompletedDownloadImportPlanner.SeriesMatches(aliases, parsed.SeriesTitle))
            {
                decision = new(false, $"Series title '{parsed.SeriesTitle}' does not match this anime.", score);
            }
            else if (covered.Length == 0 || wantedEpisode is null || (primary is not null && !covered.Contains(primary)))
            {
                decision = new(false, "Release does not cover the requested episode.", score);
            }
            else
            {
                var current = scope.FirstOrDefault(item => item.Key == wantedEpisode.Key) is { } slot
                    ? AnimeAcquisitionInventory.ToInventory(slot, target.Profile).CurrentFile
                    : null;
                decision = AnimeMonitoringEngine.EvaluateCandidate(target.Profile, wantedEpisode, score, current, state, snapshot, now);
            }

            candidates.Add(new AnimeSearchCandidate(release, score, decision, covered));
        }

        return candidates
            .OrderByDescending(candidate => candidate.Decision.Grab)
            .ThenByDescending(candidate => candidate.Score.Accepted)
            .ThenBy(candidate => candidate.Score.QualityRank)
            .ThenByDescending(candidate => candidate.Score.Score)
            .ToArray();
    }

    private static async Task LogDecisionsAsync(
        OperationStore operations,
        Guid operationId,
        IReadOnlyList<AnimeSearchCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            await operations.AppendLogAsync(operationId, OperationLogLevel.Information, LogModule, "Prowlarr returned no results.", cancellationToken);
            return;
        }

        foreach (var candidate in candidates.Take(MaxLoggedDecisions))
        {
            await operations.AppendLogAsync(
                operationId,
                candidate.Decision.Grab ? OperationLogLevel.Information : OperationLogLevel.Warning,
                LogModule,
                Describe(candidate),
                cancellationToken);
        }

        if (candidates.Count > MaxLoggedDecisions)
        {
            await operations.AppendLogAsync(
                operationId,
                OperationLogLevel.Information,
                LogModule,
                $"{candidates.Count - MaxLoggedDecisions} further result(s) were rejected or ranked lower.",
                cancellationToken);
        }
    }

    public static string Describe(AnimeSearchCandidate candidate)
    {
        var reasons = candidate.Score.RejectionReasons.Count > 0
            ? " " + string.Join(" ", candidate.Score.RejectionReasons)
            : "";
        return $"{(candidate.Decision.Grab ? "Accepted" : "Rejected")}: {candidate.Release.Title} " +
               $"[{candidate.Score.QualityKey}, score {candidate.Score.Score}, {candidate.Release.Indexer ?? "unknown indexer"}] — {candidate.Decision.Reason}{reasons}";
    }

    public static string Label(AnimeEpisodeKey key) =>
        key.AbsoluteEpisodeNumber is { } absolute && (absolute != key.EpisodeNumber || key.SeasonNumber != 1)
            ? $"S{key.SeasonNumber:00}E{key.EpisodeNumber:00} (AniList {absolute})"
            : $"S{key.SeasonNumber:00}E{key.EpisodeNumber:00}";

    private static bool Covers(AnimeReleaseInfo release, AnimeEpisodeKey key)
    {
        if (release.SeasonNumber is { } season && release.EpisodeStart is { } start && release.EpisodeEnd is { } end)
        {
            return season == key.SeasonNumber && key.EpisodeNumber >= start && key.EpisodeNumber <= end;
        }

        return key.AbsoluteEpisodeNumber is { } absolute &&
               release.AbsoluteEpisodeStart is { } absoluteStart &&
               release.AbsoluteEpisodeEnd is { } absoluteEnd &&
               absolute >= absoluteStart && absolute <= absoluteEnd;
    }

    private static ProwlarrAnimeSearchTarget SearchTargetFor(AnimeAcquisitionEpisode episode) =>
        new(
            episode.SearchTitle,
            Aliases(episode.SearchAliases, episode.SearchTitle),
            ProwlarrAnimeSearchMode.Episode,
            episode.Key.SeasonNumber,
            episode.Key.EpisodeNumber,
            episode.Key.AbsoluteEpisodeNumber);

    private static string[] Aliases(IReadOnlyList<string> aliases, string canonical) =>
        aliases
            .Where(alias => !alias.Equals(canonical, StringComparison.OrdinalIgnoreCase))
            .Take(MaxSearchAliases)
            .ToArray();

    private static ProwlarrConnection ConnectionFor(
        ProwlarrConnection connection,
        AnimeMonitoringState state,
        string animeKey) =>
        state.Anime.TryGetValue(animeKey, out var settings) && settings.IndexerIds is { Length: > 0 } indexerIds
            ? connection with { Settings = connection.Settings with { IndexerIds = indexerIds } }
            : connection;

    private async Task<int> CountActiveDownloadsAsync(
        SabnzbdAcquisitionStoreState relations,
        string animeKey,
        CancellationToken cancellationToken)
    {
        var operations = new OperationStore(db);
        var active = 0;
        foreach (var acquisition in relations.Acquisitions.Where(item =>
                     item.AnimeKey.Equals(animeKey, StringComparison.OrdinalIgnoreCase) && item.LatestAttempt is not null))
        {
            var operation = await operations.GetAsync(acquisition.LatestAttempt!.OperationId, cancellationToken);
            if (operation?.IsActive == true)
            {
                active++;
            }
        }

        return active;
    }
}

public sealed record AnimeAcquisitionPanel(
    Guid AnimeId,
    string AnimeKey,
    AnimeManagementMode Mode,
    AnimeMonitorSettings? Settings,
    string ProfileId,
    IReadOnlyList<AnimeQualityProfile> Profiles,
    int WantedCount,
    int ActiveDownloads,
    string? LastEvent,
    bool ProwlarrConfigured)
{
    public bool Monitored => Settings?.Monitored == true;
    public bool CanAcquire => Mode != AnimeManagementMode.ReadOnlyCoexistence;
}

public sealed record AnimeMonitoredRow(
    Guid AnimeId,
    string AnimeKey,
    string Title,
    AnimeManagementMode Mode,
    string ProfileId,
    int WantedCount,
    int[] IndexerIds);

public sealed record AnimeWantedRow(
    AnimeEpisodeKey Key,
    string Title,
    Guid? AnimeId,
    AnimeWantedReason Reason,
    DateTimeOffset SinceUtc,
    AnimeAcquisitionAttempt? Attempt);

public sealed record AnimeDownloadRow(
    SabnzbdAcquisition Acquisition,
    SabnzbdAcquisitionAttempt Attempt,
    OperationSnapshot Operation);

public sealed record AnimeAcquisitionOverview(
    AnimeMonitoringSchedule Schedule,
    IReadOnlyList<AnimeMonitoredRow> Monitored,
    IReadOnlyList<AnimeWantedRow> Wanted,
    IReadOnlyList<AnimeDownloadRow> ActiveDownloads,
    IReadOnlyList<AnimeImportRecord> ImportsNeedingAttention,
    IReadOnlyList<AnimeImportRecord> RecentImports,
    IReadOnlyList<OperationLogEntry> RecentDecisions,
    IReadOnlyList<AnimeMonitoringHistoryEntry> RecentHistory);
