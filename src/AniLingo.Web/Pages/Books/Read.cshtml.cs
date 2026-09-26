using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Novels;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Features.ReaderCore;
using AniLingo.Web.Features.ReaderPreferences;
using AniLingo.Web.Data;
using AniLingo.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace AniLingo.Web.Pages.Books;

public sealed class ReadModel(
    BookCatalogService books,
    CurrentAccountContext account,
    BackgroundJobQueue jobs,
    AppDbContext db) : PageModel
{
    public BookReaderChapter Reader { get; private set; } = null!;
    public ReaderDocumentDescriptor ReaderDocument { get; private set; } = null!;
    public ReaderSettingsSnapshot ReaderSettings { get; private set; } = null!;
    public IReadOnlyList<BookReaderHighlightItem> CurrentHighlights { get; private set; } = [];
    public int? RequestedPositionPermille { get; private set; }
    public string? RequestedView { get; private set; }
    public bool IsOwner => account.IsOwner;

    public bool HasTranslation =>
        Reader.Translation is not null
        || Reader.SourceLanguage.Equals(
            Reader.TargetLanguage,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whole-chapter AI translation, resolved through the canonical Learning
    /// hierarchy (Book media type → work → chapter) rather than the presence
    /// of a cached translation. When it resolves off, cached translated text
    /// is withheld from the response and the translate/status handlers refuse.
    /// </summary>
    public bool TranslationEnabled { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        string? lang,
        int? pos,
        string? view,
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
        RequestedPositionPermille = pos is null
            ? null
            : Math.Clamp(pos.Value, 0, 1000);
        RequestedView = NormalizeRequestedView(view);
        ReaderDocument = ReaderDocumentDescriptor.Create(
            reader.Work.Id,
            ReaderContentType.Book,
            reader.Work.MetadataTitle ?? reader.Work.Title,
            ReaderPreferenceRules.ParseGenres(reader.Work.MetadataGenresJson),
            languages:
            [
                new(reader.SourceLanguage, BookLanguageCatalog.GetName(reader.SourceLanguage)),
                new(reader.TargetLanguage, BookLanguageCatalog.GetName(reader.TargetLanguage))
            ]);

        ReaderSettings = await ReaderPreferenceStore.GetAsync(
            db,
            account.ProfileId,
            reader.Work.Id,
            reader.Work.MetadataGenresJson,
            ReaderContentType.Book,
            cancellationToken);
        CurrentHighlights = await BookReaderAnnotationStore.GetChapterHighlightsAsync(
            db,
            account.ProfileId,
            reader.Chapter.Id,
            cancellationToken);
        TranslationEnabled = await ResolveTranslationEnabledAsync(
            reader.Work.Id,
            reader.Chapter.Id,
            cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostReaderSettingsAsync(
        Guid id,
        string? lang,
        string? scope,
        string? changedKey,
        string? genre,
        int genrePriority,
        bool resetField,
        bool resetScope,
        ReaderSettingsInput input,
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

        try
        {
            var scopeKey = ReaderPreferenceScopes.ResolveTarget(
                scope,
                ReaderContentType.Book,
                reader.Work.Id,
                genre,
                genrePriority == 0
                    ? ReaderPreferenceScopes.DefaultGenrePriority
                    : genrePriority);

            if (resetScope)
            {
                await ReaderPreferenceStore.ResetScopeAsync(
                    db,
                    account.ProfileId,
                    scopeKey,
                    cancellationToken);
            }
            else if (resetField)
            {
                if (string.IsNullOrWhiteSpace(changedKey))
                {
                    return BadRequest("Reader setting key is required.");
                }

                await ReaderPreferenceStore.ResetScopeFieldAsync(
                    db,
                    account.ProfileId,
                    scopeKey,
                    changedKey,
                    cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(changedKey))
            {
                await ReaderPreferenceStore.SaveScopeFieldAsync(
                    db,
                    account.ProfileId,
                    scopeKey,
                    changedKey,
                    input,
                    cancellationToken);
            }
            else
            {
                await ReaderPreferenceStore.SaveScopeAsync(
                    db,
                    account.ProfileId,
                    scopeKey,
                    scopeKey.StartsWith("work:", StringComparison.OrdinalIgnoreCase)
                        ? reader.Work.Id
                        : null,
                    input,
                    cancellationToken);
            }

            var settings = await ReaderPreferenceStore.GetAsync(
                db,
                account.ProfileId,
                reader.Work.Id,
                reader.Work.MetadataGenresJson,
                ReaderContentType.Book,
                cancellationToken);

            return new JsonResult(new { settings });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    public async Task<IActionResult> OnPostResetReaderSettingsAsync(
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

        await ReaderPreferenceStore.ResetBookAsync(
            db,
            account.ProfileId,
            reader.Work.Id,
            cancellationToken);

        var settings = await ReaderPreferenceStore.GetAsync(
            db,
            account.ProfileId,
            reader.Work.Id,
            reader.Work.MetadataGenresJson,
            ReaderContentType.Book,
            cancellationToken);

        return new JsonResult(new { settings });
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

        if (!await ResolveTranslationEnabledAsync(reader.Work.Id, reader.Chapter.Id, cancellationToken))
        {
            return Forbid();
        }

        if (reader.SourceLanguage.Equals(
                target,
                StringComparison.OrdinalIgnoreCase)
            || reader.Translation is not null)
        {
            return new JsonResult(new { status = "ready" });
        }

        await jobs.QueueAsync(
            new OperationDescriptor(
                "book-chapter-translation",
                "Translation",
                "Translate book chapter",
                $"Target language: {target}",
                account.ProfileId,
                OperationLane.Normal,
                Retryable: true),
            async (operation, services, workerToken) =>
            {
                await operation.ReportAsync(
                    5,
                    "Translating chapter.",
                    cancellationToken: workerToken);

                var service = services.GetRequiredService<BookCatalogService>();
                await service.TranslateChapterAsync(
                    id,
                    target,
                    workerToken);

                await operation.ReportAsync(
                    100,
                    "Chapter translation completed.",
                    cancellationToken: workerToken);
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

        if (!await ResolveTranslationEnabledAsync(reader.Work.Id, reader.Chapter.Id, cancellationToken))
        {
            return Forbid();
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

    public async Task<IActionResult> OnGetChaptersAsync(
        Guid id,
        string? q,
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

        var chapters = await BookReaderAnnotationStore.GetChaptersAsync(
            db,
            reader.Work.Id,
            q,
            cancellationToken);

        return new JsonResult(new
        {
            currentChapterId = reader.Chapter.Id,
            chapters
        });
    }

    public async Task<IActionResult> OnGetAnnotationsAsync(
        Guid id,
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

        var annotations = await BookReaderAnnotationStore.GetAnnotationsAsync(
            db,
            account.ProfileId,
            reader.Work.Id,
            cancellationToken);

        return new JsonResult(annotations);
    }

    public async Task<IActionResult> OnPostHighlightAsync(
        Guid id,
        string? lang,
        string? anchorLanguage,
        int paragraphIndex,
        int startOffset,
        int endOffset,
        string? note,
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

        try
        {
            var highlight = await BookReaderAnnotationStore.AddHighlightAsync(
                db,
                account.ProfileId,
                reader,
                anchorLanguage,
                paragraphIndex,
                startOffset,
                endOffset,
                note,
                cancellationToken);

            return new JsonResult(highlight);
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
        var removed = await BookReaderAnnotationStore.RemoveHighlightAsync(
            db,
            account.ProfileId,
            highlightId,
            cancellationToken);

        return removed
            ? new OkResult()
            : NotFound();
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
        string? anchorLanguage,
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
            string.Equals(
                anchorLanguage,
                "original",
                StringComparison.OrdinalIgnoreCase)
                ? "original"
                : reader.TargetLanguage,
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

    private static string? NormalizeRequestedView(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "original" or "translated" or "both"
            ? normalized
            : null;
    }

    /// <summary>
    /// Resolves the Translation capability for this chapter's scope (profile →
    /// Book media type → work → chapter) so cached translated text and the
    /// translate/status handlers follow the canonical Learning hierarchy
    /// instead of only checking whether a translation happens to be cached.
    /// </summary>
    private async Task<bool> ResolveTranslationEnabledAsync(
        Guid workId,
        Guid chapterId,
        CancellationToken cancellationToken)
    {
        var settings = await new LearningConfigurationStore(db).ResolveAsync(
            account.ProfileId,
            new LearningScopeContext(
                LearningMediaType.Book,
                WorkKey: workId.ToString(),
                ContentKey: chapterId.ToString()),
            cancellationToken);

        return settings.IsEnabled(LearningCapability.Translation);
    }
}
