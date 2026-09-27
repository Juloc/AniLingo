using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.ChapterArtwork;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Operations;
using Jularr.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Pages.Books;

/// <summary>Owner page for the generated chapter artwork of one book or light novel.</summary>
public sealed class ChapterArtModel(
    AppDbContext db,
    CurrentAccountContext account,
    ChapterArtworkService artwork,
    ChapterArtworkStore store,
    BackgroundJobQueue jobs) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public Guid WorkId { get; private set; }
    public string WorkTitle { get; private set; } = "";
    public string BackUrl { get; private set; } = "/Books";
    public ChapterArtworkAvailability Availability { get; private set; } = new(false, false, null);
    public ChapterArtworkWorkSettings Settings { get; private set; } = ChapterArtworkWorkSettings.Default(Guid.Empty);
    public IReadOnlyList<ChapterRow> Chapters { get; private set; } = [];

    public sealed record ChapterRow(
        int Number,
        string Title,
        ChapterArtworkItem? Accepted,
        IReadOnlyList<ChapterArtworkItem> Previews,
        ChapterArtworkItem? Active,
        ChapterArtworkItem? Failed);

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        if (!account.IsOwner)
        {
            return Forbid();
        }

        var work = await db.NovelWorks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (work is null)
        {
            return NotFound();
        }

        WorkId = work.Id;
        WorkTitle = work.MetadataTitle ?? work.Title;
        BackUrl = work.SourceProvider == BookCatalogService.ImportedBookProvider
            ? $"/Books/Library/{work.Id}"
            : $"/Novels/Work/{work.Id}";
        Availability = await artwork.GetAvailabilityAsync(account.ProfileId, work.Id, cancellationToken);
        Settings = await store.GetWorkSettingsAsync(work.Id, cancellationToken);

        var items = (await artwork.ListWorkAsync(work.Id, cancellationToken))
            .ToLookup(x => x.ChapterNumber);
        Chapters = (await db.NovelChapters
                .AsNoTracking()
                .Where(x => x.WorkId == work.Id)
                .OrderBy(x => x.Number)
                .Select(x => new { x.Number, x.Title })
                .ToListAsync(cancellationToken))
            .Select(chapter =>
            {
                var chapterItems = items[chapter.Number].ToArray();
                return new ChapterRow(
                    chapter.Number,
                    chapter.Title,
                    chapterItems.FirstOrDefault(x => x.Status == ChapterArtworkStatus.Accepted),
                    chapterItems.Where(x => x.Status == ChapterArtworkStatus.Preview).ToArray(),
                    chapterItems.FirstOrDefault(x => x.IsActive),
                    chapterItems.LastOrDefault(x => x.Status is ChapterArtworkStatus.Failed or ChapterArtworkStatus.Cancelled));
            })
            .ToArray();

        return Page();
    }

    public async Task<IActionResult> OnPostGenerateAsync(
        Guid id,
        int chapter,
        bool regenerate,
        CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        if (!account.IsOwner)
        {
            return Forbid();
        }

        var profileId = account.ProfileId;
        var request = await artwork.RequestAsync(id, chapter, profileId, regenerate, cancellationToken);
        TempData["Status"] = request.Outcome switch
        {
            ChapterArtworkRequestOutcome.Queued => ui["chapterArt.queued"],
            ChapterArtworkRequestOutcome.AlreadyExists => ui["chapterArt.exists"],
            ChapterArtworkRequestOutcome.AlreadyRunning => ui["chapterArt.running"],
            ChapterArtworkRequestOutcome.ChapterNotFound => ui["chapterArt.chapterMissing"],
            _ => ReasonText(ui, request.Reason)
        };

        if (request.Outcome == ChapterArtworkRequestOutcome.Queued)
        {
            var ids = request.ArtworkIds;
            var operationId = await jobs.QueueAsync(
                new OperationDescriptor(
                    ChapterArtworkService.Operation,
                    "Books",
                    "Generate chapter artwork",
                    $"Chapter {chapter}",
                    profileId,
                    OperationLane.Normal,
                    Retryable: false),
                async (_, services, workerToken) =>
                {
                    var service = ChapterArtworkService.ForProfile(services, profileId);
                    await service.GenerateAsync(ids, workerToken);
                },
                cancellationToken);
            await artwork.AttachOperationAsync(ids, operationId, cancellationToken);
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAcceptAsync(Guid id, Guid artworkId, CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        if (!account.IsOwner)
        {
            return Forbid();
        }

        TempData["Status"] = await artwork.AcceptAsync(artworkId, cancellationToken)
            ? ui["chapterArt.acceptedStatus"]
            : ReasonText(ui, ChapterArtworkUnavailableReasons.StorageUnavailable);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveAsync(Guid id, Guid artworkId, CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        if (!account.IsOwner)
        {
            return Forbid();
        }

        try
        {
            await artwork.RemoveAsync(artworkId, cancellationToken);
            TempData["Status"] = ui["chapterArt.removedStatus"];
        }
        catch (InvalidOperationException)
        {
            TempData["Status"] = ReasonText(ui, ChapterArtworkUnavailableReasons.StorageUnavailable);
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id, Guid artworkId, CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        if (await artwork.GetAsync(artworkId, cancellationToken) is { OperationId: Guid operationId })
        {
            await jobs.CancelAsync(operationId, cancellationToken);
        }

        await artwork.CancelAsync(artworkId, cancellationToken);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSettingsAsync(
        Guid id,
        string? enabled,
        string? style,
        string? seriesStyle,
        bool useChapterTitles,
        CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        if (!account.IsOwner)
        {
            return Forbid();
        }

        await store.SaveWorkSettingsAsync(
            new ChapterArtworkWorkSettings(
                id,
                enabled switch { "on" => true, "off" => false, _ => null },
                string.IsNullOrWhiteSpace(style) ? null : ChapterArtworkNames.ParseStyle(style),
                seriesStyle,
                useChapterTitles),
            cancellationToken);

        TempData["Status"] = ui["chapterArt.settingsSaved"];
        return RedirectToPage(new { id });
    }

    /// <summary>Serves an image: accepted artwork to every reader who has it enabled, previews to the owner.</summary>
    public async Task<IActionResult> OnGetImageAsync(
        Guid id,
        Guid artworkId,
        int? w,
        CancellationToken cancellationToken)
    {
        var item = await artwork.GetAsync(artworkId, cancellationToken);
        if (item is null || item.WorkId != id)
        {
            return NotFound();
        }

        if (item.Status != ChapterArtworkStatus.Accepted)
        {
            if (!account.IsOwner || item.Status != ChapterArtworkStatus.Preview)
            {
                return NotFound();
            }
        }
        else if ((await artwork.GetAvailabilityAsync(account.ProfileId, id, cancellationToken)) is { CanView: false }
            && !account.IsOwner)
        {
            return NotFound();
        }

        var file = await artwork.OpenImageAsync(item, w ?? 1280, cancellationToken);
        if (file is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, max-age=604800";
        return PhysicalFile(file.Path, file.MediaType);
    }

    public static string ImageUrl(ChapterArtworkItem item, int width) =>
        $"/Books/ChapterArt/{item.WorkId}?handler=Image&artworkId={item.Id}&w={width}&v={item.ContentHash?[..8]}";

    public static string ReasonText(UiTextBundle ui, string? reason) =>
        reason switch
        {
            ChapterArtworkUnavailableReasons.DisabledGlobally => ui["chapterArt.reason.disabledGlobally"],
            ChapterArtworkUnavailableReasons.DisabledForProfile => ui["chapterArt.reason.disabledForProfile"],
            ChapterArtworkUnavailableReasons.DisabledForBook => ui["chapterArt.reason.disabledForBook"],
            ChapterArtworkUnavailableReasons.StorageNotConfigured => ui["chapterArt.reason.storageNotConfigured"],
            ChapterArtworkUnavailableReasons.StorageUnavailable => ui["chapterArt.reason.storageUnavailable"],
            Features.Ai.AiImageUnavailableReasons.NoImageModel => ui["chapterArt.reason.noImageModel"],
            _ => ui["chapterArt.reason.noImageProvider"]
        };
}
