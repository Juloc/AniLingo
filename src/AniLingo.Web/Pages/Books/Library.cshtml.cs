using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.Localization;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace AniLingo.Web.Pages.Books;

public sealed class LibraryModel(
    BookCatalogService books,
    CurrentAccountContext account,
    BackgroundJobQueue jobs,
    AppDbContext db) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public BookLibraryDetail Book { get; private set; } = null!;
    public string TargetLanguage { get; private set; } = "id";
    public bool SourceIsTarget { get; private set; }
    public bool IsOwner => account.IsOwner;

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        string? lang,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
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
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
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
            TempData["Status"] = ui["books.library.alreadyInLanguage"];
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

        TempData["Status"] = ui.Format(
            "books.library.translationQueued",
            ("language", BookLanguageCatalog.GetName(targetLanguage)));

        return RedirectToPage(new { id, lang = targetLanguage });
    }

    public async Task<IActionResult> OnPostRegenerateAsync(
        Guid id,
        string? lang,
        CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (!account.IsOwner)
        {
            return Forbid();
        }

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
            TempData["Status"] = ui["books.library.isOriginalLanguage"];
            return RedirectToPage(new { id, lang = targetLanguage });
        }

        await books.ClearBookTranslationsAsync(
            id,
            targetLanguage,
            cancellationToken);

        await jobs.QueueAsync(
            async (services, workerToken) =>
            {
                var service = services.GetRequiredService<BookCatalogService>();
                await service.TranslateBookAsync(
                    id,
                    targetLanguage,
                    workerToken);
            },
            cancellationToken);

        TempData["Status"] = ui.Format(
            "books.library.regenerateQueued",
            ("language", BookLanguageCatalog.GetName(targetLanguage)));

        return RedirectToPage(new { id, lang = targetLanguage });
    }

    public async Task<IActionResult> OnPostDeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (!account.IsOwner)
        {
            return Forbid();
        }

        try
        {
            await books.DeleteImportedBookAsync(
                id,
                cancellationToken);
            TempData["Status"] = ui["books.library.removed"];
            return RedirectToPage("/Books");
        }
        catch (InvalidOperationException exception)
        {
            TempData["Status"] = exception.Message;
            return RedirectToPage(new { id });
        }
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
