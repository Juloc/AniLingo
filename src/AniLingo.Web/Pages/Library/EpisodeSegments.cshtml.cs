using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.MediaSegments;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Library;

public sealed record EpisodeSegmentKindRow(
    MediaSegmentKind Kind,
    string Label,
    ResolvedMediaSegment? Resolved,
    EpisodeMediaSegment? Manual,
    IReadOnlyList<EpisodeMediaSegment> Candidates);

// Owner correction surface for one episode's skip markers. Manual markers win over
// imported, provider and detector markers; removing one falls back to the next source.
public sealed class EpisodeSegmentsModel(
    AppDbContext db,
    MediaSegmentService segments,
    CurrentAccountContext currentAccount) : PageModel
{
    public Guid EpisodeId { get; private set; }
    public Guid AnimeId { get; private set; }
    public string AnimeTitle { get; private set; } = "";
    public string EpisodeTitle { get; private set; } = "";
    public int SeasonNumber { get; private set; }
    public int EpisodeNumber { get; private set; }
    public double SkipConfidenceThreshold { get; private set; }
    public bool DetectorEnabled => segments.DetectorEnabled;
    public IReadOnlyList<EpisodeSegmentKindRow> Rows { get; private set; } = [];
    public TrickplayDescriptor Trickplay { get; private set; } = TrickplayDescriptor.Unavailable;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!currentAccount.IsOwner)
        {
            return Forbid();
        }

        return await LoadAsync(id, cancellationToken) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostSaveAsync(
        Guid id,
        MediaSegmentKind kind,
        string? start,
        string? end,
        CancellationToken cancellationToken)
    {
        if (!currentAccount.IsOwner)
        {
            return Forbid();
        }

        if (!Enum.IsDefined(kind))
        {
            return BadRequest();
        }

        if (!MediaTimecode.TryParse(start, out var startMs) ||
            !MediaTimecode.TryParse(end, out var endMs))
        {
            TempData["Status"] = "Enter start and end as m:ss, h:mm:ss or seconds (for example 1:30.5).";
            return RedirectToPage(new { id });
        }

        try
        {
            var saved = await segments.SaveManualAsync(id, kind, startMs, endMs, cancellationToken);
            if (saved is null)
            {
                return NotFound();
            }
        }
        catch (ArgumentException exception)
        {
            TempData["Status"] = exception.Message;
            return RedirectToPage(new { id });
        }

        TempData["Status"] = $"{MediaSegmentPolicy.KindLabel(kind)} saved as a manual marker.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveAsync(
        Guid id,
        MediaSegmentKind kind,
        CancellationToken cancellationToken)
    {
        if (!currentAccount.IsOwner)
        {
            return Forbid();
        }

        var removed = await segments.RemoveManualAsync(id, kind, cancellationToken);
        TempData["Status"] = removed
            ? $"Manual {MediaSegmentPolicy.KindLabel(kind)} marker removed."
            : "There was no manual marker to remove.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDetectAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!currentAccount.IsOwner)
        {
            return Forbid();
        }

        var run = await segments.RunDetectorAsync(id, force: true, cancellationToken);
        TempData["Status"] = run.Outcome switch
        {
            SegmentDetectionOutcome.DetectorDisabled => "No automatic segment detector is configured.",
            SegmentDetectionOutcome.NoMedia => "This episode has no media file.",
            SegmentDetectionOutcome.NotAnalyzed => "The media file has not been analysed yet; run a library scan first.",
            _ => $"Detection finished with {run.SegmentCount} marker(s)."
        };
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRegeneratePreviewsAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!currentAccount.IsOwner)
        {
            return Forbid();
        }

        var queued = await segments.RegenerateTrickplayAsync(id, cancellationToken);
        TempData["Status"] = queued
            ? "Seek preview generation was queued."
            : "Seek previews could not be queued: the media has not been analysed yet or generation is already running.";
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var episode = await (
                from item in db.Episodes.AsNoTracking()
                join anime in db.Anime.AsNoTracking() on item.AnimeId equals anime.Id
                where item.Id == id
                select new
                {
                    item.Id,
                    item.AnimeId,
                    AnimeTitle = anime.Title,
                    item.Title,
                    item.SeasonNumber,
                    item.Number
                })
            .SingleOrDefaultAsync(cancellationToken);

        if (episode is null)
        {
            return false;
        }

        EpisodeId = episode.Id;
        AnimeId = episode.AnimeId;
        AnimeTitle = episode.AnimeTitle;
        EpisodeTitle = episode.Title;
        SeasonNumber = episode.SeasonNumber;
        EpisodeNumber = episode.Number;

        var stored = await segments.ListAsync(id, cancellationToken);
        var resolved = await segments.GetSegmentsAsync(id, cancellationToken);
        SkipConfidenceThreshold = resolved.SkipConfidenceThreshold;
        Rows =
        [
            .. MediaSegmentPolicy.Kinds.Select(kind => new EpisodeSegmentKindRow(
                kind,
                MediaSegmentPolicy.KindLabel(kind),
                resolved.Segments.FirstOrDefault(x => x.Kind == kind),
                stored.FirstOrDefault(x => x.Kind == kind && x.Source == MediaSegmentSource.Manual),
                [.. stored.Where(x => x.Kind == kind)]))
        ];
        Trickplay = await segments.GetTrickplayAsync(id, cancellationToken);
        return true;
    }
}
