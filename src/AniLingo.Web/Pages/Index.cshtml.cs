using AniLingo.Web.Data;
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
        var now = DateTimeOffset.UtcNow;

        DueReviews = await db.UserTerms.AsNoTracking().CountAsync(
            x => x.ProfileId == LearningProfile.DefaultId
                && x.State == UserTermState.Learning
                && x.NextReviewAt != null
                && x.NextReviewAt <= now,
            cancellationToken);

        AnimeCount = await db.Anime.AsNoTracking().CountAsync(cancellationToken);
        EpisodeCount = await db.Episodes.AsNoTracking().CountAsync(cancellationToken);

        RecentEpisodes = await (
            from episode in db.Episodes.AsNoTracking()
            join anime in db.Anime.AsNoTracking() on episode.AnimeId equals anime.Id
            orderby episode.DiscoveredAt descending
            select new HomeEpisode(
                episode.Id,
                anime.Title,
                episode.SeasonNumber,
                episode.Number,
                db.EpisodeTerms.Count(x => x.EpisodeId == episode.Id),
                (
                    from episodeTerm in db.EpisodeTerms
                    join userTerm in db.UserTerms on episodeTerm.TermId equals userTerm.TermId
                    where episodeTerm.EpisodeId == episode.Id
                        && userTerm.ProfileId == LearningProfile.DefaultId
                        && userTerm.State == UserTermState.Known
                    select episodeTerm.TermId
                ).Count()))
            .Take(10)
            .ToListAsync(cancellationToken);
    }

    public sealed record HomeEpisode(
        Guid Id,
        string AnimeTitle,
        int SeasonNumber,
        int Number,
        int TotalTerms,
        int KnownTerms)
    {
        public int PreparationPercent => TotalTerms == 0
            ? 0
            : (int)Math.Round((double)KnownTerms / TotalTerms * 100);
    }
}
