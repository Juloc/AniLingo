using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Library;

public sealed class EpisodeModel(
    AppDbContext db,
    LearningService learningService) : PageModel
{
    public Guid EpisodeId { get; private set; }
    public Guid AnimeId { get; private set; }
    public string AnimeTitle { get; private set; } = "";
    public string EpisodeTitle { get; private set; } = "";
    public int SeasonNumber { get; private set; }
    public int EpisodeNumber { get; private set; }
    public IReadOnlyList<TermRow> Terms { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var header = await (
            from episode in db.Episodes.AsNoTracking()
            join anime in db.Anime.AsNoTracking() on episode.AnimeId equals anime.Id
            where episode.Id == id
            select new
            {
                episode.Id,
                episode.AnimeId,
                AnimeTitle = anime.Title,
                episode.Title,
                episode.SeasonNumber,
                episode.Number
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (header is null)
        {
            return NotFound();
        }

        EpisodeId = header.Id;
        AnimeId = header.AnimeId;
        AnimeTitle = header.AnimeTitle;
        EpisodeTitle = header.Title;
        SeasonNumber = header.SeasonNumber;
        EpisodeNumber = header.Number;

        Terms = await (
            from episodeTerm in db.EpisodeTerms.AsNoTracking()
            join term in db.Terms.AsNoTracking() on episodeTerm.TermId equals term.Id
            join userTermValue in db.UserTerms.AsNoTracking()
                    .Where(x => x.ProfileId == LearningProfile.DefaultId)
                on term.Id equals userTermValue.TermId into userTerms
            from userTerm in userTerms.DefaultIfEmpty()
            where episodeTerm.EpisodeId == id
            orderby episodeTerm.Occurrences descending, term.Canonical
            select new TermRow(
                term.Id,
                term.Canonical,
                term.Reading,
                term.Meaning,
                episodeTerm.Occurrences,
                userTerm == null ? null : userTerm.State))
            .ToListAsync(cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostKnownAsync(Guid id, Guid termId, CancellationToken cancellationToken)
    {
        await learningService.SetStateAsync(termId, UserTermState.Known, cancellationToken);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostLearningAsync(Guid id, Guid termId, CancellationToken cancellationToken)
    {
        await learningService.SetStateAsync(termId, UserTermState.Learning, cancellationToken);
        return RedirectToPage(new { id });
    }

    public sealed record TermRow(
        Guid TermId,
        string Canonical,
        string? Reading,
        string? Meaning,
        int Occurrences,
        UserTermState? State);
}
