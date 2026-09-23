using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Novels;
using AniLingo.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace AniLingo.Web.Pages.Novels;

public sealed class ReadModel(
    NovelService novels,
    NovelTranslationService translations,
    NovelMappingService mappings,
    BackgroundJobQueue jobs,
    CurrentAccountContext account) : PageModel
{
    public NovelWork Work { get; private set; } = null!;
    public NovelChapter Chapter { get; private set; } = null!;
    public NovelTranslation? Translation { get; private set; }
    public IReadOnlyList<NovelAnimeMapping> AnimeMappings { get; private set; } = [];
    public Guid? PreviousChapterId { get; private set; }
    public Guid? NextChapterId { get; private set; }
    public int ProgressPermille { get; private set; }
    public bool IsOwner => account.IsOwner;

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            await novels.EnsureChapterContentAsync(
                id,
                forceRefresh: false,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            TempData["Status"] = exception.Message;
            return RedirectToPage("/Novels");
        }

        var result = await novels.GetChapterAsync(id, cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        Work = result.Value.Work;
        Chapter = result.Value.Chapter;
        Translation = await translations.GetCachedAsync(
            id,
            "de",
            cancellationToken);

        AnimeMappings = await mappings.GetForChapterAsync(
            Work.Id,
            Chapter.Number,
            cancellationToken);

        (PreviousChapterId, NextChapterId) =
            await novels.GetAdjacentChapterIdsAsync(
                Work.Id,
                Chapter.Number,
                cancellationToken);

        var progress = await novels.GetProgressAsync(
            account.ProfileId,
            Work.Id,
            cancellationToken);

        if (progress?.ChapterId == Chapter.Id)
        {
            ProgressPermille = progress.PositionPermille;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostTranslateAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        var result = await novels.GetChapterAsync(id, cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        var cached = await translations.GetCachedAsync(
            id,
            "de",
            cancellationToken);

        if (cached is not null)
        {
            TempData["Status"] = "German translation is already cached.";
            return RedirectToPage(new { id });
        }

        await jobs.QueueAsync(
            async (services, workerToken) =>
            {
                var service = services.GetRequiredService<NovelTranslationService>();
                await service.TranslateChapterAsync(id, "de", workerToken);
            },
            cancellationToken);

        TempData["Status"] = "German AI translation queued.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRefreshSourceAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        try
        {
            await novels.EnsureChapterContentAsync(
                id,
                forceRefresh: true,
                cancellationToken);
            TempData["Status"] = "Japanese source refreshed. A changed source invalidates the old translation automatically.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostProgressAsync(
        Guid id,
        int positionPermille,
        CancellationToken cancellationToken)
    {
        var result = await novels.GetChapterAsync(id, cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        await novels.SaveProgressAsync(
            account.ProfileId,
            result.Value.Work.Id,
            result.Value.Chapter.Id,
            positionPermille,
            cancellationToken);

        return new OkResult();
    }

}