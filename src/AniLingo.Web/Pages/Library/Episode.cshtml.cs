using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Playback;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Library;

public sealed class EpisodeModel(
    AppDbContext db,
    LearningService learningService,
    EpisodePreparationService preparationService,
    PlaybackService playbackService) : PageModel
{
    public Guid EpisodeId { get; private set; }
    public Guid AnimeId { get; private set; }
    public string AnimeTitle { get; private set; } = "";
    public string EpisodeTitle { get; private set; } = "";
    public int SeasonNumber { get; private set; }
    public int EpisodeNumber { get; private set; }
    public EpisodePreparationSnapshot Preparation { get; private set; } = EpisodePreparationSnapshot.Empty;
    public EpisodePlaybackSnapshot Playback { get; private set; } = EpisodePlaybackSnapshot.Empty;
    public IReadOnlyList<EpisodePreparationTerm> Terms => Preparation.Terms;

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
        Preparation = await preparationService.GetAsync(id, header.AnimeId, cancellationToken);
        Playback = await playbackService.GetSnapshotAsync(id, cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnGetMediaAsync(Guid id, CancellationToken cancellationToken)
    {
        var stream = await playbackService.GetStreamAsync(id, cancellationToken);
        if (stream is null || !System.IO.File.Exists(stream.Path))
        {
            return NotFound();
        }

        return new PhysicalFileResult(stream.Path, stream.ContentType)
        {
            EnableRangeProcessing = true,
            LastModified = stream.LastModified
        };
    }

    public async Task<IActionResult> OnPostPreparePlaybackAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await playbackService.QueuePreparationAsync(id, cancellationToken);
        return RedirectToPage(new { id });
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

    public async Task<IActionResult> OnPostPrepareAsync(Guid id, CancellationToken cancellationToken)
    {
        var preparedCount = await preparationService.PrepareToTargetAsync(id, cancellationToken);
        if (preparedCount is null)
        {
            return NotFound();
        }

        return RedirectToPage("/Learn/Index");
    }
}
