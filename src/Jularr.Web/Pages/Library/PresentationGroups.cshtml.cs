using System.Globalization;
using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Manga;
using Jularr.Web.Features.Metadata;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.Presentation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Pages.Library;

/// <summary>
/// Owner-only editor for a work's display-only presentation groups. It uses the one shared
/// presentation-group owner for anime, manga, and light novels, without changing their files,
/// stable item identity, or provider mappings.
/// </summary>
public sealed class PresentationGroupsModel(
    AppDbContext db,
    AnimeMetadataService metadataService,
    NovelCatalogQueries novelCatalog,
    CurrentAccountContext currentAccount) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public PresentationMediaType Media { get; private set; }
    public string MediaRouteValue => PresentationMediaTypes.ToStorage(Media);
    public Guid WorkId { get; private set; }
    public string WorkTitle { get; private set; } = "";
    public string BackUrl { get; private set; } = "";
    public bool UsesVolumeUnits { get; private set; }
    public IReadOnlyList<PresentationItem> Items { get; private set; } = [];
    public int GroupableItemCount => Items.Count(item => item.Unit is not null);
    public int GroupableUnitCount => Items.Where(item => item.Unit is not null).Select(item => item.Unit!.Value).Distinct().Count();
    public int FirstUnit { get; private set; }
    public int LastUnit { get; private set; }
    public IReadOnlyList<PresentationSection<PresentationItem>> Preview { get; private set; } = [];
    public bool PreviewIsUnsaved { get; private set; }
    public string? StatusMessage => TempData["PresentationStatus"] as string;
    public string Units => UsesVolumeUnits ? Ui["library.presentation.volumes"] : Media == PresentationMediaType.Novel ? Ui["library.presentation.chapters"] : Ui["library.presentation.episodes"];
    public string PreviewItemUnits => Media == PresentationMediaType.Manga ? Ui["library.presentation.chapters"] : Units;

    [BindProperty]
    public List<GroupFormInput> Groups { get; set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, string? media, CancellationToken cancellationToken)
    {
        if (!currentAccount.IsOwner)
        {
            return Forbid();
        }

        if (!TryGetMediaType(media, out var mediaType) || !await LoadAsync(id, mediaType, cancellationToken))
        {
            return NotFound();
        }

        var groups = await new PresentationGroupStore(db).ListForWorkAsync(Media, id, cancellationToken);
        Groups = groups.Select(group => new GroupFormInput
        {
            Name = group.Name,
            Order = group.SortOrder + 1,
            Ranges = group.Ranges.Select(range => new RangeFormInput { Start = range.Low, End = range.High }).ToList()
        }).ToList();
        EnsureEditorSlots();

        Preview = Arrange(ToDrafts());
        PreviewIsUnsaved = false;
        return Page();
    }

    public async Task<IActionResult> OnPostPreviewAsync(Guid id, string? media, CancellationToken cancellationToken)
    {
        if (!currentAccount.IsOwner)
        {
            return Forbid();
        }

        if (!TryGetMediaType(media, out var mediaType) || !await LoadAsync(id, mediaType, cancellationToken))
        {
            return NotFound();
        }

        Preview = Arrange(ToDrafts());
        PreviewIsUnsaved = true;
        EnsureEditorSlots();
        return Page();
    }

    public async Task<IActionResult> OnPostApplyAsync(Guid id, string? media, CancellationToken cancellationToken)
    {
        if (!currentAccount.IsOwner)
        {
            return Forbid();
        }

        if (!TryGetMediaType(media, out var mediaType) || !await LoadAsync(id, mediaType, cancellationToken))
        {
            return NotFound();
        }

        var drafts = ToDrafts();
        await new PresentationGroupStore(db).ReplaceForWorkAsync(Media, id, drafts, cancellationToken);
        TempData["PresentationStatus"] = drafts.Count == 0 ? Ui["library.presentation.clearedStatus"] : Ui.Format("library.presentation.savedStatus", ("count", drafts.Count));
        return RedirectToPage(new { id, media = MediaRouteValue });
    }

    private async Task<bool> LoadAsync(Guid id, PresentationMediaType mediaType, CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        Media = mediaType;
        WorkId = id;

        switch (mediaType)
        {
            case PresentationMediaType.Anime:
                var anime = await db.Anime.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
                if (anime is null)
                {
                    return false;
                }

                var metadata = await metadataService.GetAsync(id, cancellationToken);
                WorkTitle = metadata?.PreferredTitle ?? anime.Title;
                BackUrl = $"/Library/Anime/{id}";
                Items = await db.Episodes
                    .AsNoTracking()
                    .Where(item => item.AnimeId == id)
                    .OrderBy(item => item.SeasonNumber)
                    .ThenBy(item => item.Number)
                    .Select(item => new PresentationItem(item.Number, $"E{item.Number:00}", $"S{item.SeasonNumber:00} · {item.Title}"))
                    .ToListAsync(cancellationToken);
                break;

            case PresentationMediaType.Manga:
                var manga = await new MangaRepository(db).GetSeriesAsync(id, cancellationToken);
                if (manga is null)
                {
                    return false;
                }

                WorkTitle = manga.Title;
                BackUrl = $"/Manga/Series/{id}";
                UsesVolumeUnits = true;
                Items = manga.Chapters.Select(chapter => new PresentationItem(chapter.VolumeNumber, chapter.VolumeNumber is int volume ? Ui.Format("manga.series.volume", ("volume", volume)) : "—", chapter.Title)).ToArray();
                break;

            case PresentationMediaType.Novel:
                var novel = await novelCatalog.GetWorkDetailAsync(id, cancellationToken);
                if (novel is null)
                {
                    return false;
                }

                WorkTitle = novel.Work.MetadataTitle ?? novel.Work.Title;
                BackUrl = $"/Novels/Work/{id}";
                UsesVolumeUnits = novel.Volumes.Any(volume => volume.IsEpub);
                Items = UsesVolumeUnits
                    ? novel.Volumes
                        .Where(volume => volume.IsEpub)
                        .Select(volume => new PresentationItem(volume.Number, volume.Number.ToString(CultureInfo.InvariantCulture), volume.Title ?? Ui.Format("novels.work.volumeLabel", ("number", volume.Number))))
                        .ToArray()
                    : novel.Chapters.Select(chapter => new PresentationItem(chapter.Number, chapter.Number.ToString(CultureInfo.InvariantCulture), chapter.Title)).ToArray();
                break;

            default:
                return false;
        }

        var units = Items.Where(item => item.Unit is not null).Select(item => item.Unit!.Value).ToArray();
        FirstUnit = units.Length == 0 ? 0 : units.Min();
        LastUnit = units.Length == 0 ? 0 : units.Max();
        return true;
    }

    private static bool TryGetMediaType(string? media, out PresentationMediaType mediaType)
    {
        mediaType = media?.ToLowerInvariant() switch
        {
            null or "anime" => PresentationMediaType.Anime,
            "manga" => PresentationMediaType.Manga,
            "novel" => PresentationMediaType.Novel,
            _ => PresentationMediaType.Book
        };

        return mediaType != PresentationMediaType.Book;
    }

    private List<PresentationGroupDraft> ToDrafts() =>
        Groups
            .Where(group => !group.Delete && !string.IsNullOrWhiteSpace(group.Name))
            .OrderBy(group => group.Order)
            .Select(group => new PresentationGroupDraft(
                group.Name!.Trim(),
                group.Ranges.Where(range => range.Start is int start && start > 0).Select(range => new PresentationRange(range.Start!.Value, range.End ?? range.Start!.Value)).ToList()))
            .Where(draft => draft.Ranges.Count > 0)
            .ToList();

    private IReadOnlyList<PresentationSection<PresentationItem>> Arrange(IReadOnlyList<PresentationGroupDraft> drafts)
    {
        var now = DateTime.UtcNow;
        var groups = drafts.Select((draft, index) => new PresentationGroup(Guid.NewGuid(), Media, WorkId, draft.Name, index, draft.Ranges, now, now)).ToList();
        return PresentationGrouping.Arrange(groups, Items, item => item.Unit, Ui.Format("library.presentation.otherHeading", ("units", Units)));
    }

    private void EnsureEditorSlots()
    {
        Groups.RemoveAll(group => group.Delete || string.IsNullOrWhiteSpace(group.Name) && group.Ranges.TrueForAll(range => range.Start is null && range.End is null));

        var nextOrder = Groups.Count == 0 ? 1 : Groups.Max(group => group.Order) + 1;
        foreach (var group in Groups)
        {
            group.Ranges.RemoveAll(range => range.Start is null && range.End is null);
            group.Ranges.Add(new RangeFormInput());
        }

        Groups.Add(new GroupFormInput { Order = nextOrder, Ranges = [new RangeFormInput()] });
    }

    public sealed record PresentationItem(int? Unit, string UnitLabel, string Title);

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
