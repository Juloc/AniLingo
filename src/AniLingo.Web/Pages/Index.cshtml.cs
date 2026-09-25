using AniLingo.Web.Data;
using AniLingo.Web.Features.Artwork;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages;

public sealed class IndexModel(AppDbContext db, CurrentAccountContext currentAccount) : PageModel
{
    public int DueReviews { get; private set; }
    public int AnimeCount { get; private set; }
    public int EpisodeCount { get; private set; }
    public IReadOnlyList<HomeEpisode> RecentEpisodes { get; private set; } = [];
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public bool ShowLearningHomeWidget { get; private set; }
    public bool ShowContentMetrics { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await new UiTranslationCatalogStore(db).LoadProfileBundleAsync(
            currentAccount.ProfileId,
            cancellationToken);

        var configuration = new LearningConfigurationStore(db);
        var profileLearning = await configuration.ResolveProfileAsync(
            currentAccount.ProfileId,
            cancellationToken);
        var animeLearning = await configuration.ResolveAsync(
            currentAccount.ProfileId,
            new LearningScopeContext(LearningMediaType.Anime),
            cancellationToken);

        ShowLearningHomeWidget =
            profileLearning.IsEnabled(LearningCapability.HomeWidget);
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

        var occurrenceTotals =
            from episodeTerm in db.EpisodeTerms.AsNoTracking()
            group episodeTerm by episodeTerm.EpisodeId
            into episodeGroup
            select new
            {
                EpisodeId = episodeGroup.Key,
                TotalOccurrences = episodeGroup.Sum(x => (int?)x.Occurrences)
            };

        var preparedTotals =
            from episodeTerm in db.EpisodeTerms.AsNoTracking()
            join state in LearningQueries.TermStates(db, currentAccount.ProfileId)
                    .Where(x =>
                        x.State == UserTermState.Known
                        || x.State == UserTermState.Learning)
                on episodeTerm.TermId equals state.TermId
            group episodeTerm by episodeTerm.EpisodeId
            into episodeGroup
            select new
            {
                EpisodeId = episodeGroup.Key,
                PreparedOccurrences = episodeGroup.Sum(x => (int?)x.Occurrences)
            };

        var recentEpisodes = await (
            from episode in db.Episodes.AsNoTracking()
            join anime in db.Anime.AsNoTracking() on episode.AnimeId equals anime.Id
            join metadataValue in db.AnimeMetadata.AsNoTracking()
                on anime.Id equals metadataValue.AnimeId into metadataRows
            from metadata in metadataRows.DefaultIfEmpty()
            join occurrenceValue in occurrenceTotals
                on episode.Id equals occurrenceValue.EpisodeId into occurrenceRows
            from occurrences in occurrenceRows.DefaultIfEmpty()
            join preparedValue in preparedTotals
                on episode.Id equals preparedValue.EpisodeId into preparedRows
            from prepared in preparedRows.DefaultIfEmpty()
            orderby episode.DiscoveredAt descending
            select new HomeEpisode(
                episode.Id,
                anime.Id,
                metadata == null ? anime.Title : metadata.PreferredTitle,
                episode.SeasonNumber,
                episode.Number,
                occurrences.TotalOccurrences ?? 0,
                prepared.PreparedOccurrences ?? 0,
                metadata == null ? null : metadata.CoverImageUrl))
            .Take(10)
            .ToListAsync(cancellationToken);

        RecentEpisodes = recentEpisodes
            .Select(row => row with
            {
                CoverImageUrl = AnimeArtworkStore.ResolvePosterUrl(
                    row.AnimeId,
                    row.CoverImageUrl)
            })
            .ToArray();
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
