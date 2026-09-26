using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace AniLingo.Web.Pages.Books;

public sealed class LibraryModel(
    AppDbContext db,
    BookCatalogService books,
    CurrentAccountContext account,
    BackgroundJobQueue jobs) : PageModel
{
    public BookLibraryDetail Book { get; private set; } = null!;
    public string TargetLanguage { get; private set; } = "id";
    public bool SourceIsTarget { get; private set; }
    public bool IsOwner => account.IsOwner;

    /// <summary>
    /// Whole-book translation state (the per-chapter "Translated" badge, the
    /// translated-count summary and the Translate/Regenerate actions), gated
    /// the same way as the Book reader's own translation UI: through
    /// <see cref="LearningModuleResolver.ResolveTranslationEnabledAsync"/> for
    /// this work's Book scope, not by whether a translation is cached.
    /// </summary>
    public bool TranslationEnabled { get; private set; }

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
        TranslationEnabled = await ResolveTranslationEnabledAsync(id, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostTranslateBookAsync(
        Guid id,
        string? lang,
        CancellationToken cancellationToken)
    {
        if (!await ResolveTranslationEnabledAsync(id, cancellationToken))
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

    public async Task<IActionResult> OnPostRegenerateAsync(
        Guid id,
        string? lang,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        if (!await ResolveTranslationEnabledAsync(id, cancellationToken))
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
            TempData["Status"] = "The selected language is the original language.";
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

        TempData["Status"] =
            $"{BookLanguageCatalog.GetName(targetLanguage)} translation cleared and queued again.";

        return RedirectToPage(new { id, lang = targetLanguage });
    }

    public async Task<IActionResult> OnPostDeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        try
        {
            await books.DeleteImportedBookAsync(
                id,
                cancellationToken);
            TempData["Status"] =
                "Book removed from AniLingo. External source/download files were not changed.";
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

    /// <summary>
    /// Resolves the Translation capability for this book's Book/work scope.
    /// Shared with the Book reader through
    /// <see cref="LearningModuleResolver.ResolveTranslationEnabledAsync"/>.
    /// </summary>
    private Task<bool> ResolveTranslationEnabledAsync(
        Guid workId,
        CancellationToken cancellationToken) =>
        new LearningModuleResolver(db).ResolveTranslationEnabledAsync(
            account.ProfileId,
            LearningMediaType.Book,
            workId.ToString(),
            contentKey: null,
            cancellationToken);
}
