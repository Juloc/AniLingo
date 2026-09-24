using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace AniLingo.Web.Pages.Books;

public sealed class LibraryModel(
    BookCatalogService books,
    CurrentAccountContext account,
    BackgroundJobQueue jobs) : PageModel
{
    public BookLibraryDetail Book { get; private set; } = null!;
    public string TargetLanguage { get; private set; } = "id";
    public bool SourceIsTarget { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        string? lang,
        CancellationToken cancellationToken)
    {
        TargetLanguage = BookLanguageCatalog.Normalize(lang);
        var detail = await books.GetLibraryBookAsync(
            id,
            account.ProfileId,
            TargetLanguage,
            cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        Book = detail;
        SourceIsTarget = GetSourceLanguage(Book.Work)
            .Equals(
                TargetLanguage,
                StringComparison.OrdinalIgnoreCase);
        return Page();
    }

    public async Task<IActionResult> OnPostTranslateBookAsync(
        Guid id,
        string? lang,
        CancellationToken cancellationToken)
    {
        var targetLanguage = BookLanguageCatalog.Normalize(lang);

        var detail = await books.GetLibraryBookAsync(
            id,
            account.ProfileId,
            targetLanguage,
            cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        var sourceLanguage = GetSourceLanguage(detail.Work);
        if (sourceLanguage.Equals(
                targetLanguage,
                StringComparison.OrdinalIgnoreCase))
        {
            TempData["Status"] = "The book is already in that language.";
            return RedirectToPage(new { id, lang = targetLanguage });
        }

        await jobs.QueueAsync(
            new OperationDescriptor(
                "book-translation",
                "Translation",
                "Translate book",
                detail.Work.MetadataTitle ?? detail.Work.Title,
                account.ProfileId,
                OperationLane.Normal,
                Retryable: true),
            async (operation, services, workerToken) =>
            {
                await operation.ReportAsync(
                    5,
                    $"Translating book to {BookLanguageCatalog.GetName(targetLanguage)}.",
                    cancellationToken: workerToken);

                var service = services.GetRequiredService<BookCatalogService>();
                await service.TranslateBookAsync(
                    id,
                    targetLanguage,
                    workerToken);

                await operation.ReportAsync(
                    100,
                    "Book translation completed.",
                    cancellationToken: workerToken);
            },
            cancellationToken);

        TempData["Status"] =
            $"{BookLanguageCatalog.GetName(targetLanguage)} translation queued. You can start reading immediately; completed chapters appear as they finish.";

        return RedirectToPage(new { id, lang = targetLanguage });
    }

    private static string GetSourceLanguage(
        AniLingo.Web.Features.Novels.NovelWork work)
    {
        if (!string.IsNullOrWhiteSpace(work.Format)
            && work.Format.StartsWith(
                "EPUB:",
                StringComparison.OrdinalIgnoreCase))
        {
            var value = work.Format[5..].Trim();
            if (value.Length > 0)
            {
                return value;
            }
        }

        return "en";
    }
}
