using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Library;

public sealed class AnimeModel(AppDbContext db) : PageModel
{
    public string AnimeTitle { get; private set; } = "";
    public IReadOnlyList<EpisodeRow> Episodes { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var anime = await db.Anime.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (anime is null)
        {
            return NotFound();
        }

        AnimeTitle = anime.Title;

        Episodes = await db.Episodes
            .AsNoTracking()
            .Where(x => x.AnimeId == id)
            .OrderBy(x => x.SeasonNumber)
            .ThenBy(x => x.Number)
            .Select(episode => new EpisodeRow(
                episode.Id,
                episode.SeasonNumber,
                episode.Number,
                episode.Title,
                db.EpisodeTerms.Count(x => x.EpisodeId == episode.Id),
                (
                    from episodeTerm in db.EpisodeTerms
                    join userTerm in db.UserTerms on episodeTerm.TermId equals userTerm.TermId
                    where episodeTerm.EpisodeId == episode.Id
                        && userTerm.ProfileId == LearningProfile.DefaultId
                        && userTerm.State == UserTermState.Known
                    select episodeTerm.TermId
                ).Count(),
                db.SubtitleTracks.Count(x => x.EpisodeId == episode.Id && x.Language == "ja")))
            .ToListAsync(cancellationToken);

        return Page();
    }

    public sealed record EpisodeRow(
        Guid Id,
        int SeasonNumber,
        int Number,
        string Title,
        int TotalTerms,
        int KnownTerms,
        int JapaneseSubtitleTracks)
    {
        public int PreparationPercent => TotalTerms == 0
            ? 0
            : (int)Math.Round((double)KnownTerms / TotalTerms * 100);
    }
}
