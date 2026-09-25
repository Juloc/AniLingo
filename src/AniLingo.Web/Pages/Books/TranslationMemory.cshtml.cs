using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Books;

public sealed class TranslationMemoryModel(
    AppDbContext db,
    CurrentAccountContext account,
    IConfiguration configuration) : PageModel
{
    public Guid WorkId { get; private set; }
    public string WorkTitle { get; private set; } = "";
    public string TargetLanguage { get; private set; } = "id";
    public BookTranslationBible? Bible { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        string? lang,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        var work = await GetWorkAsync(
            id,
            cancellationToken);
        if (work is null)
        {
            return NotFound();
        }

        WorkId = work.Id;
        WorkTitle = work.MetadataTitle ?? work.Title;
        TargetLanguage = BookLanguageCatalog.Normalize(lang);

        Bible = await CreateStore().LoadAsync(
            work.Id,
            TargetLanguage,
            cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostOverviewAsync(
        Guid id,
        string? lang,
        string? narrativePerspective,
        string? overallStyle,
        string? register,
        string? audience,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        if (await GetWorkAsync(id, cancellationToken) is null)
        {
            return NotFound();
        }

        var target = BookLanguageCatalog.Normalize(lang);
        var bible = await CreateStore().UpdateOverviewAsync(
            id,
            target,
            narrativePerspective,
            overallStyle,
            register,
            audience,
            cancellationToken);

        TempData["Status"] = bible is null
            ? "Translation memory does not exist yet. Translate a chapter first."
            : "Book Bible style updated.";

        return RedirectToPage(
            new { id, lang = target });
    }

    public async Task<IActionResult> OnPostTermAsync(
        Guid id,
        string? lang,
        string source,
        string target,
        string? category,
        string? notes,
        bool locked,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        if (await GetWorkAsync(id, cancellationToken) is null)
        {
            return NotFound();
        }

        var targetLanguage = BookLanguageCatalog.Normalize(lang);

        try
        {
            var bible = await CreateStore().UpsertTermAsync(
                id,
                targetLanguage,
                source,
                target,
                category,
                notes,
                locked,
                cancellationToken);

            TempData["Status"] = bible is null
                ? "Translation memory does not exist yet. Translate a chapter first."
                : locked
                    ? "Glossary term saved and locked."
                    : "Glossary term saved.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage(
            new { id, lang = targetLanguage });
    }

    public async Task<IActionResult> OnPostRemoveTermAsync(
        Guid id,
        string? lang,
        string source,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        if (await GetWorkAsync(id, cancellationToken) is null)
        {
            return NotFound();
        }

        var target = BookLanguageCatalog.Normalize(lang);
        var bible = await CreateStore().RemoveTermAsync(
            id,
            target,
            source,
            cancellationToken);

        TempData["Status"] = bible is null
            ? "Translation memory does not exist."
            : "Glossary term removed.";

        return RedirectToPage(
            new { id, lang = target });
    }

    public async Task<IActionResult> OnPostEntityAsync(
        Guid id,
        string? lang,
        string sourceName,
        string targetName,
        string? type,
        string? description,
        string? pronouns,
        string? relationships,
        string? voiceNotes,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        if (await GetWorkAsync(id, cancellationToken) is null)
        {
            return NotFound();
        }

        var target = BookLanguageCatalog.Normalize(lang);

        try
        {
            var bible = await CreateStore().UpsertEntityAsync(
                id,
                target,
                sourceName,
                targetName,
                type,
                description,
                pronouns,
                relationships,
                voiceNotes,
                cancellationToken);

            TempData["Status"] = bible is null
                ? "Translation memory does not exist yet. Translate a chapter first."
                : "Entity translation memory saved.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage(
            new { id, lang = target });
    }

    public async Task<IActionResult> OnPostRemoveEntityAsync(
        Guid id,
        string? lang,
        string sourceName,
        string type,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        if (await GetWorkAsync(id, cancellationToken) is null)
        {
            return NotFound();
        }

        var target = BookLanguageCatalog.Normalize(lang);
        var bible = await CreateStore().RemoveEntityAsync(
            id,
            target,
            sourceName,
            type,
            cancellationToken);

        TempData["Status"] = bible is null
            ? "Translation memory does not exist."
            : "Entity removed from translation memory.";

        return RedirectToPage(
            new { id, lang = target });
    }

    public async Task<IActionResult> OnPostResetAsync(
        Guid id,
        string? lang,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        if (await GetWorkAsync(id, cancellationToken) is null)
        {
            return NotFound();
        }

        var target = BookLanguageCatalog.Normalize(lang);
        await CreateStore().ResetAsync(
            id,
            target,
            cancellationToken);

        TempData["Status"] =
            "Book Bible reset. Existing translated chapters were not deleted.";

        return RedirectToPage(
            new { id, lang = target });
    }

    private Task<Features.Novels.NovelWork?> GetWorkAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        db.NovelWorks
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == id
                    && x.SourceProvider == BookCatalogService.ImportedBookProvider,
                cancellationToken);

    private BookTranslationMemoryStore CreateStore()
    {
        var configured = configuration[
            "Books:Translation:MemoryPath"]?.Trim();

        return new BookTranslationMemoryStore(
            string.IsNullOrWhiteSpace(configured)
                ? BookTranslationMemoryStore.DefaultRoot
                : configured);
    }
}
