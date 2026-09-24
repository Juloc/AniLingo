using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.Novels;
using AniLingo.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace AniLingo.Web.Pages.Books;

public sealed class ReadModel(
    BookCatalogService books,
    CurrentAccountContext account,
    BackgroundJobQueue jobs) : PageModel
{
    public BookReaderChapter Reader { get; private set; } = null!;
    public bool HasTranslation =>
        Reader.Translation is not null
        || Reader.SourceLanguage.Equals(
            Reader.TargetLanguage,
            StringComparison.OrdinalIgnoreCase);

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        string? lang,
        CancellationToken cancellationToken)
    {
        var target = BookLanguageCatalog.Normalize(lang);
        var reader = await books.GetReaderChapterAsync(
            id,
            account.ProfileId,
            target,
            cancellationToken);

        if (reader is null)
        {
            return NotFound();
        }

        Reader = reader;
        return Page();
    }

    public async Task<IActionResult> OnPostTranslateAsync(
        Guid id,
        string? lang,
        CancellationToken cancellationToken)
    {
        var target = BookLanguageCatalog.Normalize(lang);

        var reader = await books.GetReaderChapterAsync(
            id,
            account.ProfileId,
            target,
            cancellationToken);

        if (reader is null)
        {
            return NotFound();
        }

        if (reader.SourceLanguage.Equals(
                target,
                StringComparison.OrdinalIgnoreCase)
            || reader.Translation is not null)
        {
            return new JsonResult(new { status = "ready" });
        }

        await jobs.QueueAsync(
            async (services, workerToken) =>
            {
                var service = services.GetRequiredService<BookCatalogService>();
                await service.TranslateChapterAsync(
                    id,
                    target,
                    workerToken);
            },
            cancellationToken);

        return new JsonResult(new { status = "queued" });
    }

    public async Task<IActionResult> OnGetTranslationStatusAsync(
        Guid id,
        string? lang,
        CancellationToken cancellationToken)
    {
        var target = BookLanguageCatalog.Normalize(lang);
        var reader = await books.GetReaderChapterAsync(
            id,
            account.ProfileId,
            target,
            cancellationToken);

        if (reader is null)
        {
            return NotFound();
        }

        if (reader.SourceLanguage.Equals(
                target,
                StringComparison.OrdinalIgnoreCase))
        {
            return new JsonResult(new
            {
                status = "ready",
                paragraphs = reader.OriginalParagraphs
            });
        }

        if (reader.Translation is null)
        {
            return new JsonResult(new { status = "pending" });
        }

        return new JsonResult(new
        {
            status = "ready",
            paragraphs = reader.TranslatedParagraphs
        });
    }

    public async Task<IActionResult> OnPostProgressAsync(
        Guid id,
        int positionPermille,
        string? lang,
        CancellationToken cancellationToken)
    {
        var reader = await books.GetReaderChapterAsync(
            id,
            account.ProfileId,
            BookLanguageCatalog.Normalize(lang),
            cancellationToken);

        if (reader is null)
        {
            return NotFound();
        }

        await books.SaveProgressAsync(
            account.ProfileId,
            reader.Work.Id,
            reader.Chapter.Id,
            positionPermille,
            reader.TargetLanguage,
            cancellationToken);

        return new OkResult();
    }

    public async Task<IActionResult> OnPostBookmarkAsync(
        Guid id,
        int positionPermille,
        string? lang,
        CancellationToken cancellationToken)
    {
        var reader = await books.GetReaderChapterAsync(
            id,
            account.ProfileId,
            BookLanguageCatalog.Normalize(lang),
            cancellationToken);

        if (reader is null)
        {
            return NotFound();
        }

        var bookmark = await books.AddBookmarkAsync(
            account.ProfileId,
            reader.Work.Id,
            reader.Chapter.Id,
            positionPermille,
            reader.TargetLanguage,
            cancellationToken);

        return new JsonResult(new
        {
            bookmark.Id,
            bookmark.PositionPermille
        });
    }

    public async Task<IActionResult> OnPostRemoveBookmarkAsync(
        Guid id,
        Guid bookmarkId,
        CancellationToken cancellationToken)
    {
        await books.RemoveBookmarkAsync(
            account.ProfileId,
            bookmarkId,
            cancellationToken);

        return new OkResult();
    }
}
