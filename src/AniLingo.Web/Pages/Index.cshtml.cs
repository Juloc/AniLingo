using AniLingo.Web.Data;
using AniLingo.Web.Features.Artwork;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Localization;
using AniLingo.Web.Features.Progress;
using AniLingo.Web.Features.Reading;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages;

public sealed class IndexModel(AppDbContext db, CurrentAccountContext currentAccount) : PageModel
{
    private readonly EpisodeProgressService progress = new(db, currentAccount);

    public int DueReviews { get; private set; }
    public int AnimeCount { get; private set; }
    public int EpisodeCount { get; private set; }
    public IReadOnlyList<HomeEpisode> RecentEpisodes { get; private set; } = [];
    public IReadOnlyList<ContinueWatchingItem> ContinueWatching { get; private set; } = [];

    /// <summary>
    /// Most recently read unfinished Novels, Books and Manga of the current
    /// profile, newest first, each with its exact reader resume URL.
    /// </summary>
    public IReadOnlyList<ContinueReadingItem> ContinueReading { get; private set; } = [];
    public IReadOnlyList<PlaybackHistoryItem> PlaybackHistory { get; private set; } = [];
    public int PlaybackHistoryLimit => EpisodeProgressService.HistoryLimit;
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    /// <summary>
    /// Due-review widget: resolved HomeWidget and Reviews capabilities at profile
    /// scope. HomeWidget is off by default in every mode, including Study; the
    /// user opts in through Learning settings.
    /// </summary>
    public bool ShowLearningHomeWidget { get; private set; }

    /// <summary>
    /// Resolved ContentMetrics capability for the Anime media type. Controls
    /// preparation percentages on recently discovered episode cards.
    /// </summary>
    public bool ShowContentMetrics { get; private set; }

    public async Task<IActionResult> OnPostClearHistoryAsync(CancellationToken cancellationToken)
    {
        await progress.ClearHistoryAsync(cancellationToken);

        var ui = await new UiTranslationCatalogStore(db).LoadProfileBundleAsync(
            currentAccount.ProfileId,
            cancellationToken);
        TempData["Status"] = ui["home.history.cleared"];
        return RedirectToPage();
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await new UiTranslationCatalogStore(db).LoadProfileBundleAsync(
            currentAccount.ProfileId,
            cancellationToken);

        ContinueWatching = await progress.GetContinueWatchingAsync(
            cancellationToken: cancellationToken);
        PlaybackHistory = await progress.GetHistoryAsync(cancellationToken);
        ContinueReading = await new ContinueReadingQuery(db).GetAsync(
            currentAccount.ProfileId,
            cancellationToken: cancellationToken);

        var configuration = new LearningConfigurationStore(db);
        var profileLearning = await configuration.ResolveProfileAsync(
            currentAccount.ProfileId,
            cancellationToken);
        var animeLearning = await configuration.ResolveAsync(
            currentAccount.ProfileId,
            new LearningScopeContext(LearningMediaType.Anime),
            cancellationToken);

        ShowLearningHomeWidget =
            profileLearning.IsEnabled(LearningCapability.HomeWidget)
            && profileLearning.IsEnabled(LearningCapability.Reviews);
        ShowContentMetrics =
            animeLearning.IsEnabled(LearningCapability.ContentMetrics);

        var now = DateTime.UtcNow;

        if (ShowLearningHomeWidget)
        {
            DueReviews = await LearningQueries
                .DueCards(db, currentAccount.ProfileId, now)
                .CountAsync(cancellationToken);
        }

        AnimeCount = await db.Anime.AsNoTracking().CountAsync(cancellationToken);
        EpisodeCount = await db.Episodes.AsNoTracking().CountAsync(cancellationToken);

        var recentEpisodes = await (
            from episode in db.Episodes.AsNoTracking()
            join anime in db.Anime.AsNoTracking() on episode.AnimeId equals anime.Id
            join metadataValue in db.AnimeMetadata.AsNoTracking()
                on anime.Id equals metadataValue.AnimeId into metadataRows
            from metadata in metadataRows.DefaultIfEmpty()
            orderby episode.DiscoveredAt descending
            select new HomeEpisode(
                episode.Id,
                anime.Id,
                metadata == null ? anime.Title : metadata.PreferredTitle,
                episode.SeasonNumber,
                episode.Number,
                0,
                0,
                metadata == null ? null : metadata.CoverImageUrl))
            .Take(10)
            .ToListAsync(cancellationToken);

        // Vocabulary coverage is only computed when the resolved Anime scope
        // shows content metrics; otherwise Home never touches learning tables.
        var coverage = ShowContentMetrics
            ? await LoadCoverageAsync(
                recentEpisodes.Select(x => x.Id).ToArray(),
                cancellationToken)
            : new Dictionary<Guid, (int Total, int Prepared)>();

        RecentEpisodes = recentEpisodes
            .Select(row =>
            {
                coverage.TryGetValue(row.Id, out var totals);
                return row with
                {
                    TotalOccurrences = totals.Total,
                    PreparedOccurrences = totals.Prepared,
                    CoverImageUrl = AnimeArtworkStore.ResolvePosterUrl(
                        row.AnimeId,
                        row.CoverImageUrl)
                };
            })
            .ToArray();
    }

    private async Task<Dictionary<Guid, (int Total, int Prepared)>> LoadCoverageAsync(
        Guid[] episodeIds,
        CancellationToken cancellationToken)
    {
        if (episodeIds.Length == 0)
        {
            return [];
        }

        var totals = await db.EpisodeTerms
            .AsNoTracking()
            .Where(x => episodeIds.Contains(x.EpisodeId))
            .GroupBy(x => x.EpisodeId)
            .Select(group => new
            {
                EpisodeId = group.Key,
                Total = group.Sum(x => x.Occurrences)
            })
            .ToDictionaryAsync(x => x.EpisodeId, x => x.Total, cancellationToken);

        var prepared = await (
            from episodeTerm in db.EpisodeTerms.AsNoTracking()
            join state in LearningQueries.TermStates(db, currentAccount.ProfileId)
                    .Where(x =>
                        x.State == UserTermState.Known
                        || x.State == UserTermState.Learning)
                on episodeTerm.TermId equals state.TermId
            where episodeIds.Contains(episodeTerm.EpisodeId)
            group episodeTerm by episodeTerm.EpisodeId
            into episodeGroup
            select new
            {
                EpisodeId = episodeGroup.Key,
                Prepared = episodeGroup.Sum(x => x.Occurrences)
            })
            .ToDictionaryAsync(x => x.EpisodeId, x => x.Prepared, cancellationToken);

        return totals.ToDictionary(
            x => x.Key,
            x => (x.Value, prepared.GetValueOrDefault(x.Key)));
    }

    public sealed record HomeEpisode(
        Guid Id,
        Guid AnimeId,
        string AnimeTitle,
        int SeasonNumber,
        int Number,
        int TotalOccurrences,
        int PreparedOccurrences,
        string? CoverImageUrl)
    {
        public int PreparationPercent => TotalOccurrences == 0
            ? 0
            : (int)Math.Floor((double)PreparedOccurrences / TotalOccurrences * 100);
    }
}
