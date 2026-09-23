using AniLingo.Web.Data;
using AniLingo.Web.Features.Artwork;
using AniLingo.Web.Features.Learning;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages;

public sealed class IndexModel(AppDbContext db) : PageModel
{
    public int DueReviews { get; private set; }
    public int AnimeCount { get; private set; }
    public int EpisodeCount { get; private set; }
    public IReadOnlyList<HomeEpisode> RecentEpisodes { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        DueReviews = await db.UserTerms.AsNoTracking().CountAsync(
            x => x.ProfileId == LearningProfile.DefaultId
                && x.State == UserTermState.Learning
                && x.NextReviewAt != null
                && x.NextReviewAt <= now,
            cancellationToken);

        AnimeCount = await db.Anime.AsNoTracking().CountAsync(cancellationToken);
        EpisodeCount = await db.Episodes.AsNoTracking().CountAsync(cancellationToken);

        var occurrenceTotals =
            from episodeTerm in db.EpisodeTerms.AsNoTracking()
            group episodeTerm by episodeTerm.EpisodeId
            into episodeGroup
            select new
            {
                EpisodeId = episodeGroup.Key,
                TotalOccurrences = episodeGroup.Sum(x => x.Occurrences)
            };

        var preparedTotals =
            from episodeTerm in db.EpisodeTerms.AsNoTracking()
            join userTerm in db.UserTerms.AsNoTracking()
                    .Where(x =>
                        x.ProfileId == LearningProfile.DefaultId
                        && (x.State == UserTermState.Known
                            || x.State == UserTermState.Learning))
                on episodeTerm.TermId equals userTerm.TermId
            group episodeTerm by episodeTerm.EpisodeId
            into episodeGroup
            select new
            {
                EpisodeId = episodeGroup.Key,
                PreparedOccurrences = episodeGroup.Sum(x => x.Occurrences)
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
                occurrences == null ? 0 : occurrences.TotalOccurrences,
                prepared == null ? 0 : prepared.PreparedOccurrences,
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
