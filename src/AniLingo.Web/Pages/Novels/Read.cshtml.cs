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
    public IReadOnlyList<string> JapaneseParagraphs { get; private set; } = [];
    public IReadOnlyList<string> GermanParagraphs { get; private set; } = [];
    public IReadOnlyList<NovelAnimeMapping> AnimeMappings { get; private set; } = [];
    public IReadOnlyList<NovelBookmark> Bookmarks { get; private set; } = [];
    public IReadOnlyList<NovelHighlight> Highlights { get; private set; } = [];
    public IReadOnlyList<NovelChapterItem> Chapters { get; private set; } = [];
    public NovelProgress? Progress { get; private set; }
    public NovelBookmark? JumpBookmark { get; private set; }
    public Guid? PreviousChapterId { get; private set; }
    public Guid? NextChapterId { get; private set; }
    public bool IsOwner => account.IsOwner;

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        Guid? bookmark,
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

        var workDetail = await novels.GetWorkAsync(
            Work.Id,
            cancellationToken);
        Chapters = workDetail?.Chapters ?? [];

        Translation = await translations.GetCachedAsync(
            id,
            "de",
            cancellationToken);

        JapaneseParagraphs = NovelTextLayout.SplitParagraphs(Chapter.OriginalText);
        GermanParagraphs = NovelTextLayout.SplitParagraphs(Translation?.Text);

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

        Progress = progress?.ChapterId == Chapter.Id ? progress : null;

        Bookmarks = await novels.GetBookmarksAsync(
            account.ProfileId,
            Work.Id,
            cancellationToken);

        Highlights = await novels.GetHighlightsAsync(
            account.ProfileId,
            Work.Id,
            cancellationToken);

        if (bookmark is Guid bookmarkId)
        {
            JumpBookmark = Bookmarks.FirstOrDefault(
                x => x.Id == bookmarkId && x.ChapterId == Chapter.Id);
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
            if (IsFetchRequest())
            {
                return new JsonResult(new { status = "ready" });
            }

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

        if (IsFetchRequest())
        {
            return new JsonResult(new { status = "queued" });
        }

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
        string? anchorLanguage,
        int? anchorParagraphIndex,
        int anchorOffset,
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
            anchorLanguage ?? "ja",
            anchorParagraphIndex,
            anchorOffset,
            cancellationToken);

        return new OkResult();
    }

    public async Task<IActionResult> OnPostBookmarkAsync(
        Guid id,
        int positionPermille,
        string? language,
        int? paragraphIndex,
        int characterOffset,
        string? label,
        CancellationToken cancellationToken)
    {
        try
        {
            var bookmark = await novels.AddBookmarkAsync(
                account.ProfileId,
                id,
                positionPermille,
                language ?? "ja",
                paragraphIndex,
                characterOffset,
                label,
                cancellationToken);

            return new JsonResult(new
            {
                bookmark.Id,
                bookmark.ChapterId,
                bookmark.PositionPermille,
                bookmark.Language,
                bookmark.ParagraphIndex,
                bookmark.CharacterOffset,
                bookmark.AnchorText,
                bookmark.Label
            });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    public async Task<IActionResult> OnPostRemoveBookmarkAsync(
        Guid id,
        Guid bookmarkId,
        CancellationToken cancellationToken)
    {
        await novels.RemoveBookmarkAsync(
            account.ProfileId,
            bookmarkId,
            cancellationToken);

        return IsFetchRequest()
            ? new OkResult()
            : RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostHighlightAsync(
        Guid id,
        string? language,
        int paragraphIndex,
        int startOffset,
        int endOffset,
        string? note,
        CancellationToken cancellationToken)
    {
        try
        {
            var highlight = await novels.AddHighlightAsync(
                account.ProfileId,
                id,
                language ?? "ja",
                paragraphIndex,
                startOffset,
                endOffset,
                note,
                cancellationToken);

            return new JsonResult(new
            {
                highlight.Id,
                highlight.ChapterId,
                highlight.Language,
                highlight.ParagraphIndex,
                highlight.StartOffset,
                highlight.EndOffset,
                highlight.Text,
                highlight.Note
            });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    public async Task<IActionResult> OnPostRemoveHighlightAsync(
        Guid id,
        Guid highlightId,
        CancellationToken cancellationToken)
    {
        await novels.RemoveHighlightAsync(
            account.ProfileId,
            highlightId,
            cancellationToken);

        return IsFetchRequest()
            ? new OkResult()
            : RedirectToPage(new { id });
    }

    private bool IsFetchRequest() =>
        string.Equals(
            Request.Headers["X-Requested-With"].ToString(),
            "fetch",
            StringComparison.OrdinalIgnoreCase);
}
