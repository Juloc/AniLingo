using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Metadata;
using Jularr.Web.Features.Presentation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Pages.Library;

/// <summary>
/// Owner-only editor for a work's presentation groups (#524, epic #510): create, reorder, delete
/// groups and assign internal-episode ranges, with a preview before apply. Linked from the anime
/// detail page's owner-only Manage sheet; it never appears in the consumer layout. Editing here only
/// changes derived display state — never episode identity, file paths or provider mappings.
/// </summary>
public sealed class PresentationGroupsModel(
    AppDbContext db,
    AnimeMetadataService metadataService,
    CurrentAccountContext currentAccount) : PageModel
{
    private const PresentationMediaType Media = PresentationMediaType.Anime;

    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public Guid AnimeId { get; private set; }
    public string AnimeTitle { get; private set; } = "";
    public IReadOnlyList<EpisodeRef> Episodes { get; private set; } = [];
    public int EpisodeCount => Episodes.Count;
    public int FirstEpisode { get; private set; }
    public int LastEpisode { get; private set; }
    public IReadOnlyList<PresentationSection<EpisodeRef>> Preview { get; private set; } = [];
    public bool PreviewIsUnsaved { get; private set; }
    public string? StatusMessage => TempData["PresentationStatus"] as string;

    [BindProperty]
    public List<GroupFormInput> Groups { get; set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!currentAccount.IsOwner)
        {
            return Forbid();
        }

        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var groups = await Store().ListForWorkAsync(Media, id, cancellationToken);
        Groups = groups
            .Select(group => new GroupFormInput
            {
                Name = group.Name,
                Order = group.SortOrder + 1,
                Ranges = group.Ranges
                    .Select(range => new RangeFormInput { Start = range.Low, End = range.High })
                    .ToList()
            })
            .ToList();
        EnsureEditorSlots();

        Preview = Arrange(ToDrafts());
        PreviewIsUnsaved = false;
        return Page();
    }

    public async Task<IActionResult> OnPostPreviewAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!currentAccount.IsOwner)
        {
            return Forbid();
        }

        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var drafts = ToDrafts();
        Preview = Arrange(drafts);
        PreviewIsUnsaved = true;
        EnsureEditorSlots();
        return Page();
    }

    public async Task<IActionResult> OnPostApplyAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!currentAccount.IsOwner)
        {
            return Forbid();
        }

        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var drafts = ToDrafts();
        await Store().ReplaceForWorkAsync(Media, id, drafts, cancellationToken);
        TempData["PresentationStatus"] = drafts.Count == 0
            ? Ui["library.presentation.clearedStatus"]
            : Ui.Format("library.presentation.savedStatus", ("count", drafts.Count));
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        AnimeId = id;

        var anime = await db.Anime
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (anime is null)
        {
            return false;
        }

        var metadata = await metadataService.GetAsync(id, cancellationToken);
        AnimeTitle = metadata?.PreferredTitle ?? anime.Title;

        Episodes = await db.Episodes
            .AsNoTracking()
            .Where(x => x.AnimeId == id)
            .OrderBy(x => x.SeasonNumber)
            .ThenBy(x => x.Number)
            .Select(x => new EpisodeRef(x.Id, x.SeasonNumber, x.Number, x.Title))
            .ToListAsync(cancellationToken);

        FirstEpisode = Episodes.Count == 0 ? 0 : Episodes.Min(x => x.Number);
        LastEpisode = Episodes.Count == 0 ? 0 : Episodes.Max(x => x.Number);
        return true;
    }

    private PresentationGroupStore Store() => new(db);

    // Form rows -> saved-ready drafts: drop deleted/blank groups, drop rangeless groups, order by
    // the owner's group order, and default a range's end to its start when left empty.
    private List<PresentationGroupDraft> ToDrafts() =>
        Groups
            .Where(group => !group.Delete && !string.IsNullOrWhiteSpace(group.Name))
            .OrderBy(group => group.Order)
            .Select(group => new PresentationGroupDraft(
                group.Name!.Trim(),
                group.Ranges
                    .Where(range => range.Start is int start && start > 0)
                    .Select(range => new PresentationRange(range.Start!.Value, range.End ?? range.Start!.Value))
                    .ToList()))
            .Where(draft => draft.Ranges.Count > 0)
            .ToList();

    private IReadOnlyList<PresentationSection<EpisodeRef>> Arrange(IReadOnlyList<PresentationGroupDraft> drafts)
    {
        var now = DateTime.UtcNow;
        var groups = drafts
            .Select((draft, index) => new PresentationGroup(
                Guid.NewGuid(), Media, AnimeId, draft.Name, index, draft.Ranges, now, now))
            .ToList();
        return PresentationGrouping.Arrange(
            groups,
            Episodes,
            episode => episode.Number,
            Ui["library.presentation.otherHeading"]);
    }

    // Keep the form editable across round-trips: remove deleted rows and truly-empty groups, then
    // leave exactly one trailing blank range per group and one trailing blank group to add more.
    private void EnsureEditorSlots()
    {
        Groups.RemoveAll(group => group.Delete);
        Groups.RemoveAll(group =>
            string.IsNullOrWhiteSpace(group.Name)
            && group.Ranges.TrueForAll(range => range.Start is null && range.End is null));

        var nextOrder = Groups.Count == 0 ? 1 : Groups.Max(group => group.Order) + 1;
        foreach (var group in Groups)
        {
            group.Ranges.RemoveAll(range => range.Start is null && range.End is null);
            group.Ranges.Add(new RangeFormInput());
        }

        Groups.Add(new GroupFormInput { Order = nextOrder, Ranges = [new RangeFormInput()] });
    }

    public sealed record EpisodeRef(Guid Id, int SeasonNumber, int Number, string Title);

    public sealed class GroupFormInput
    {
        public string? Name { get; set; }
        public int Order { get; set; }
        public bool Delete { get; set; }
        public List<RangeFormInput> Ranges { get; set; } = [];
    }

    public sealed class RangeFormInput
    {
        public int? Start { get; set; }
        public int? End { get; set; }
    }
}
