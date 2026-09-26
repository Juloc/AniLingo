using AniLingo.Web.Data;
using AniLingo.Web.Features.Artwork;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Metadata;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Features.Progress;
using AniLingo.Web.Features.Tracking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Library;

public sealed class AnimeModel(
    AppDbContext db,
    AnimeMetadataService metadataService,
    CurrentAccountContext currentAccount,
    OperationRunner operations,
    EpisodeProgressService episodeProgressService,
    AniListAccountService aniListAccountService) : PageModel
{
    public Guid AnimeId { get; private set; }
    public string AnimeTitle { get; private set; } = "";
    public string LocalAnimeTitle { get; private set; } = "";
    public AnimeMetadata? Metadata { get; private set; }
    public string? CoverImageUrl { get; private set; }
    public string? BannerImageUrl { get; private set; }
    public string SearchQuery { get; private set; } = "";
    public string? MetadataError { get; private set; }
    public IReadOnlyList<AnimeMetadataCandidate> SearchResults { get; private set; } = [];
    public IReadOnlyList<AnimeEpisodeMetadataMapping> EpisodeMappings { get; private set; } = [];
    public IReadOnlyList<EpisodeRow> Episodes { get; private set; } = [];
    public IReadOnlyList<SeasonRow> Seasons { get; private set; } = [];
    public int SuggestedMappingSeason { get; private set; }
    public int SuggestedMappingEpisodeStart { get; private set; } = 1;
    public bool IsOwner => currentAccount.IsOwner;
    public bool ShowContentMetrics { get; private set; }
    public ExternalProgressSummary? ExternalProgress { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        string? q,
        CancellationToken cancellationToken)
    {
        var anime = await db.Anime
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (anime is null)
        {
            return NotFound();
        }

        AnimeId = anime.Id;
        LocalAnimeTitle = anime.Title;
        Metadata = await metadataService.GetAsync(id, cancellationToken);
        AnimeTitle = Metadata?.PreferredTitle ?? anime.Title;
        CoverImageUrl = AnimeArtworkStore.ResolvePosterUrl(
            id,
            Metadata?.CoverImageUrl);
        BannerImageUrl = AnimeArtworkStore.ResolveFanartUrl(
            id,
            Metadata?.BannerImageUrl);
        SearchQuery = string.IsNullOrWhiteSpace(q) ? anime.Title : q.Trim();

        var learning = await new LearningConfigurationStore(db).ResolveAsync(
            currentAccount.ProfileId,
            new LearningScopeContext(
                LearningMediaType.Anime,
                WorkKey: id.ToString()),
            cancellationToken);
        ShowContentMetrics =
            learning.IsEnabled(LearningCapability.ContentMetrics);

        if (TempData.TryGetValue("MetadataError", out var metadataError))
        {
            MetadataError = metadataError?.ToString();
        }

        if (IsOwner && !string.IsNullOrWhiteSpace(q))
        {
            try
            {
                SearchResults = await metadataService.SearchAsync(
                    AniListMetadataProvider.ProviderKey,
                    SearchQuery,
                    8,
                    cancellationToken);
            }
            catch (MetadataProviderException exception)
            {
                MetadataError = exception.Message;
            }
        }

        var episodeRows = await db.Episodes
            .AsNoTracking()
            .Where(x => x.AnimeId == id)
            .OrderBy(x => x.SeasonNumber)
            .ThenBy(x => x.Number)
            .Select(episode => new
            {
                episode.Id,
                episode.SeasonNumber,
                episode.Number,
                episode.Title,
                JapaneseSubtitleTracks = db.SubtitleTracks.Count(
                    x => x.EpisodeId == episode.Id && x.Language == "ja")
            })
            .ToListAsync(cancellationToken);

        if (IsOwner)
        {
            try
            {
                EpisodeMappings = await metadataService.GetEpisodeMappingsAsync(
                    id,
                    cancellationToken);
            }
            catch (AniListAccountException exception)
            {
                MetadataError ??= exception.Message;
                EpisodeMappings = [];
            }

            Seasons = episodeRows
                .GroupBy(x => x.SeasonNumber)
                .Select(group => new SeasonRow(
                    group.Key,
                    group.Min(x => x.Number),
                    group.Max(x => x.Number),
                    group.Count()))
                .ToArray();

            var firstUnmapped = episodeRows.FirstOrDefault(episode =>
                episode.Number > 0 &&
                !EpisodeMappings.Any(mapping =>
                    mapping.Contains(episode.SeasonNumber, episode.Number)));

            if (firstUnmapped is not null)
            {
                SuggestedMappingSeason = firstUnmapped.SeasonNumber;
                SuggestedMappingEpisodeStart = firstUnmapped.Number;
            }
            else if (episodeRows.Count > 0)
            {
                SuggestedMappingSeason = episodeRows[0].SeasonNumber;
                SuggestedMappingEpisodeStart = Math.Max(1, episodeRows[0].Number);
            }
        }

        var episodeIds = episodeRows.Select(x => x.Id).ToArray();
        List<CoverageRow> coverageRows;

        if (!ShowContentMetrics || episodeIds.Length == 0)
        {
            coverageRows = [];
        }
        else
        {
            coverageRows = await (
                from episodeTerm in db.EpisodeTerms.AsNoTracking()
                join stateValue in LearningQueries.TermStates(db, currentAccount.ProfileId)
                    on episodeTerm.TermId equals stateValue.TermId into states
                from state in states.DefaultIfEmpty()
                where episodeIds.Contains(episodeTerm.EpisodeId)
                select new CoverageRow(
                    episodeTerm.EpisodeId,
                    episodeTerm.Occurrences,
                    state == null ? null : state.State))
                .ToListAsync(cancellationToken);
        }

        var coverageByEpisode = coverageRows
            .GroupBy(x => x.EpisodeId)
            .ToDictionary(
                group => group.Key,
                group => new Coverage(
                    group.Count(),
                    group.Sum(x => x.Occurrences),
                    group.Where(x => x.State is UserTermState.Known or UserTermState.Learning)
                        .Sum(x => x.Occurrences)));

        var progressByEpisode = await episodeProgressService.GetForAnimeAsync(
            id,
            cancellationToken);

        Episodes = episodeRows
            .Select(episode =>
            {
                var coverage = coverageByEpisode.GetValueOrDefault(episode.Id, Coverage.Empty);
                var progress = progressByEpisode.GetValueOrDefault(episode.Id);

                return new EpisodeRow(
                    episode.Id,
                    episode.SeasonNumber,
                    episode.Number,
                    episode.Title,
                    coverage.TotalTerms,
                    coverage.TotalOccurrences,
                    coverage.PreparedOccurrences,
                    episode.JapaneseSubtitleTracks,
                    progress?.IsCompleted == true,
                    progress is { IsCompleted: false, ResumePositionMs: > 0 }
                        ? progress.Percent
                        : null);
            })
            .ToArray();

        // Local-only: remote AniList progress is loaded after first paint
        // through OnGetExternalProgressAsync.
        ExternalProgress = await aniListAccountService.GetAnimeProgressSummaryAsync(
            id,
            cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnGetExternalProgressAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!await db.Anime.AsNoTracking().AnyAsync(x => x.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var state = await aniListAccountService.GetAnimeProgressStateAsync(
            id,
            cancellationToken);

        Response.Headers.CacheControl = "no-store";
        return Partial(
            "_ExternalProgressState",
            new ExternalProgressRemoteView(
                ExternalProgressMediaKind.Anime,
                state,
                "SyncAniList"));
    }

    public async Task<IActionResult> OnPostSyncAniListAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await operations.RunAsync(
            new OperationDescriptor(
                "anilist-anime-progress-sync",
                "AniList",
                "Sync anime progress",
                ProfileId: currentAccount.ProfileId,
                Lane: OperationLane.Normal,
                Retryable: false),
            (_, token) => aniListAccountService.SyncAnimeProgressAsync(
                id,
                token),
            "Anime progress sync completed.",
            cancellationToken);

        TempData["Status"] = result.Message;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostWatchedAsync(
        Guid id,
        Guid episodeId,
        bool watched,
        CancellationToken cancellationToken)
    {
        var belongsToAnime = await db.Episodes
            .AsNoTracking()
            .AnyAsync(x => x.Id == episodeId && x.AnimeId == id, cancellationToken);

        if (!belongsToAnime)
        {
            return NotFound();
        }

        await episodeProgressService.SetWatchedAsync(
            episodeId,
            watched,
            cancellationToken);

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostMatchMetadataAsync(
        Guid id,
        string provider,
        string externalId,
        CancellationToken cancellationToken)
    {
        if (!IsOwner)
        {
            return Forbid();
        }

        try
        {
            await operations.RunAsync(
                new OperationDescriptor(
                    "anime-metadata-match",
                    "Anime",
                    "Match anime metadata",
                    ProfileId: currentAccount.ProfileId,
                    Lane: OperationLane.Normal,
                    Retryable: false),
                async (_, token) =>
                {
                    var result = await metadataService.MatchAsync(
                        id,
                        provider,
                        externalId,
                        token);

                    if (!result.Success)
                    {
                        throw new InvalidOperationException(
                            result.Error ?? "Anime metadata could not be matched.");
                    }
                },
                "Anime metadata matched.",
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is MetadataProviderException or InvalidOperationException)
        {
            TempData["MetadataError"] = exception.Message;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostMatchEpisodeRangeAsync(
        Guid id,
        int seasonNumber,
        int localEpisodeStart,
        int? localEpisodeEnd,
        int remoteEpisodeStart,
        string provider,
        string externalId,
        CancellationToken cancellationToken)
    {
        if (!IsOwner)
        {
            return Forbid();
        }

        try
        {
            await operations.RunAsync(
                new OperationDescriptor(
                    "anime-episode-range-match",
                    "Anime",
                    "Match anime episode range",
                    ProfileId: currentAccount.ProfileId,
                    Lane: OperationLane.Normal,
                    Retryable: false),
                async (_, token) =>
                {
                    var result = await metadataService.MatchEpisodeRangeAsync(
                        id,
                        seasonNumber,
                        localEpisodeStart,
                        localEpisodeEnd,
                        remoteEpisodeStart,
                        provider,
                        externalId,
                        token);

                    if (!result.Success)
                    {
                        throw new InvalidOperationException(
                            result.Error ?? "Episode range could not be matched.");
                    }
                },
                "Anime episode range matched.",
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is MetadataProviderException or InvalidOperationException)
        {
            TempData["MetadataError"] = exception.Message;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveEpisodeMappingAsync(
        Guid id,
        Guid mappingId,
        CancellationToken cancellationToken)
    {
        if (!IsOwner)
        {
            return Forbid();
        }

        try
        {
            if (!await metadataService.RemoveEpisodeMappingAsync(
                    id,
                    mappingId,
                    cancellationToken))
            {
                TempData["MetadataError"] = "Episode mapping was not found.";
            }
        }
        catch (AniListAccountException exception)
        {
            TempData["MetadataError"] = exception.Message;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRefreshMetadataAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!IsOwner)
        {
            return Forbid();
        }

        try
        {
            await operations.RunAsync(
                new OperationDescriptor(
                    "anime-metadata-refresh",
                    "Anime",
                    "Refresh anime metadata",
                    ProfileId: currentAccount.ProfileId,
                    Lane: OperationLane.Normal,
                    Retryable: false),
                async (_, token) =>
                {
                    if (!await metadataService.RefreshAsync(id, token))
                    {
                        throw new InvalidOperationException(
                            "Metadata could not be refreshed.");
                    }
                },
                "Anime metadata refreshed.",
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is MetadataProviderException or InvalidOperationException)
        {
            TempData["MetadataError"] = exception.Message;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveMetadataAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!IsOwner)
        {
            return Forbid();
        }

        await metadataService.RemoveAsync(id, cancellationToken);
        return RedirectToPage(new { id });
    }

    private sealed record CoverageRow(
        Guid EpisodeId,
        int Occurrences,
        UserTermState? State);

    private sealed record Coverage(
        int TotalTerms,
        int TotalOccurrences,
        int PreparedOccurrences)
    {
        public static Coverage Empty { get; } = new(0, 0, 0);
    }

    public sealed record SeasonRow(
        int Number,
        int FirstEpisode,
        int LastEpisode,
        int EpisodeCount);

    public sealed record EpisodeRow(
        Guid Id,
        int SeasonNumber,
        int Number,
        string Title,
        int TotalTerms,
        int TotalOccurrences,
        int PreparedOccurrences,
        int JapaneseSubtitleTracks,
        bool IsWatched,
        int? ResumePercent)
    {
        public int PreparationPercent => TotalOccurrences == 0
            ? 0
            : (int)Math.Floor((double)PreparedOccurrences / TotalOccurrences * 100);
    }
}
