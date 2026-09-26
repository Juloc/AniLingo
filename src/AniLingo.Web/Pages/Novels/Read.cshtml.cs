using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Novels;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Features.ReaderCore;
using AniLingo.Web.Features.ReaderPreferences;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Novels;

public sealed record NovelReaderAnchor(
    string Language,
    int? ParagraphIndex,
    int Offset,
    int PositionPermille,
    string? AnchorText,
    bool Forced);

/// <summary>
/// Novel reader page adapter. GET renders only cached content: a chapter
/// without downloaded text shows a preparation state whose action queues the
/// download through Operations instead of fetching from the provider inline.
/// </summary>
public sealed class ReadModel(
    NovelCatalogQueries catalog,
    NovelAnnotationService annotations,
    NovelProgressService progress,
    NovelImportService imports,
    NovelTranslationService translations,
    NovelMappingService mappings,
    NovelJobs jobs,
    AppDbContext db,
    CurrentAccountContext account,
    OperationRunner operations) : PageModel
{
    public NovelReaderChapter Chapter { get; private set; } = null!;
    public IReadOnlyList<string> JapaneseParagraphs { get; private set; } = [];
    public IReadOnlyList<string> GermanParagraphs { get; private set; } = [];
    /// <summary>Japanese content blocks: paragraphs, headings and illustrations.</summary>
    public IReadOnlyList<NovelReaderBlock> JapaneseBlocks { get; private set; } = [];
    public IReadOnlyList<NovelAnimeMapping> AnimeMappings { get; private set; } = [];
    public NovelChapterAnnotations Annotations { get; private set; } =
        new([], [], 0, 0);
    public NovelProgress? Progress { get; private set; }
    public NovelReaderAnchor InitialAnchor { get; private set; } =
        new(NovelReadingLanguage.Japanese, null, 0, 0, null, false);
    public ReaderDocumentDescriptor ReaderDocument { get; private set; } = null!;
    public ReaderSettingsSnapshot ReaderSettings { get; private set; } = null!;
    public OperationSnapshot? Preparation { get; private set; }
    public Guid? ReturnBookmarkId { get; private set; }
    public Guid? ReturnHighlightId { get; private set; }
    public bool IsOwner => account.IsOwner;

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        Guid? bookmark,
        Guid? highlight,
        Guid? prepare,
        CancellationToken cancellationToken)
    {
        var chapter = await catalog.GetReaderChapterAsync(id, cancellationToken);
        if (chapter is null)
        {
            return NotFound();
        }

        Chapter = chapter;
        ReturnBookmarkId = bookmark;
        ReturnHighlightId = highlight;

        if (!chapter.HasContent)
        {
            Preparation = await GetPreparationAsync(prepare, cancellationToken);
            return Page();
        }

        var contentType = ReaderContentTypes.FromNovelMetadata(
            chapter.WorkFormat,
            chapter.SourceProvider);
        ReaderDocument = ReaderDocumentDescriptor.Create(
            chapter.WorkId,
            contentType,
            chapter.WorkTitle,
            ReaderPreferenceRules.ParseGenres(chapter.GenresJson));

        ReaderSettings = await ReaderPreferenceStore.GetAsync(
            db,
            account.ProfileId,
            chapter.WorkId,
            chapter.GenresJson,
            contentType,
            cancellationToken);

        JapaneseParagraphs = NovelTextLayout.SplitParagraphs(chapter.OriginalText);
        GermanParagraphs = NovelTextLayout.SplitParagraphs(chapter.TranslationText);
        JapaneseBlocks = NovelChapterDocument.BuildReaderBlocks(
            chapter.OriginalText,
            chapter.ContentJson);

        AnimeMappings = await mappings.GetForChapterAsync(
            chapter.WorkId,
            chapter.Number,
            cancellationToken);

        var workProgress = await progress.GetProgressAsync(
            account.ProfileId,
            chapter.WorkId,
            cancellationToken);
        Progress = workProgress?.ChapterId == chapter.Id ? workProgress : null;

        Annotations = await annotations.GetChapterAnnotationsAsync(
            account.ProfileId,
            chapter.WorkId,
            chapter.Id,
            cancellationToken);

        InitialAnchor = ResolveInitialAnchor(bookmark, highlight);
        return Page();
    }

    public async Task<IActionResult> OnGetChaptersAsync(
        Guid id,
        string? q,
        int? after,
        int? before,
        CancellationToken cancellationToken)
    {
        var context = await catalog.GetChapterContextAsync(id, cancellationToken);
        if (context is null)
        {
            return NotFound();
        }

        var window = await catalog.GetChapterWindowAsync(
            context.WorkId,
            context.Number,
            q,
            after,
            before,
            NovelCatalogQueries.MaxChapterWindow,
            cancellationToken);

        return new JsonResult(window);
    }

    public async Task<IActionResult> OnGetWorkNotesAsync(
        Guid id,
        string? kind,
        int offset,
        CancellationToken cancellationToken)
    {
        var noteKind = kind?.Trim().ToLowerInvariant() switch
        {
            "bookmarks" => NovelNoteKind.Bookmark,
            "highlights" => NovelNoteKind.Highlight,
            _ => (NovelNoteKind?)null
        };

        if (noteKind is null)
        {
            return BadRequest("Unknown note kind.");
        }

        var context = await catalog.GetChapterContextAsync(id, cancellationToken);
        if (context is null)
        {
            return NotFound();
        }

        var page = await annotations.GetWorkNotesAsync(
            account.ProfileId,
            context.WorkId,
            context.ChapterId,
            noteKind.Value,
            offset,
            cancellationToken);

        return new JsonResult(new
        {
            items = page.Items,
            nextOffset = page.NextOffset,
            hasMore = page.HasMore
        });
    }

    public async Task<IActionResult> OnPostPrepareChapterAsync(
        Guid id,
        Guid? bookmark,
        Guid? highlight,
        CancellationToken cancellationToken)
    {
        var context = await catalog.GetChapterContextAsync(id, cancellationToken);
        if (context is null)
        {
            return NotFound();
        }

        Guid? operationId = null;
        if (!context.HasContent)
        {
            operationId = await jobs.QueueChapterDownloadAsync(
                $"{context.WorkTitle} · Chapter {context.Number}",
                [context.ChapterId],
                account.ProfileId,
                cancellationToken);
        }

        return RedirectToPage(new { id, bookmark, highlight, prepare = operationId });
    }

    public async Task<IActionResult> OnGetChapterStatusAsync(
        Guid id,
        Guid? prepare,
        CancellationToken cancellationToken)
    {
        if (await catalog.HasContentAsync(id, cancellationToken))
        {
            return new JsonResult(new { ready = true });
        }

        var preparation = await GetPreparationAsync(prepare, cancellationToken);
        return new JsonResult(new
        {
            ready = false,
            status = preparation?.Status.ToString().ToLowerInvariant(),
            message = preparation?.Status is OperationStatus.Failed or OperationStatus.Interrupted
                ? "Das Kapitel konnte nicht heruntergeladen werden."
                : null
        });
    }

    public async Task<IActionResult> OnPostTranslateAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        var context = await catalog.GetChapterContextAsync(id, cancellationToken);
        if (context is null)
        {
            return NotFound();
        }

        var cached = await translations.GetCachedAsync(
            id,
            NovelReadingLanguage.German,
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

        await jobs.QueueTranslationAsync(
            id,
            $"{context.WorkTitle} · Chapter {context.Number}",
            account.ProfileId,
            cancellationToken);

        if (IsFetchRequest())
        {
            return new JsonResult(new { status = "queued" });
        }

        TempData["Status"] = "German AI translation queued.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnGetTranslationStatusAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var cached = await translations.GetCachedAsync(
            id,
            NovelReadingLanguage.German,
            cancellationToken);

        if (cached is null)
        {
            return new JsonResult(new { status = "pending" });
        }

        return new JsonResult(new
        {
            status = "ready",
            paragraphs = NovelTextLayout.SplitParagraphs(cached.Text)
        });
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
            await operations.RunAsync(
                new OperationDescriptor(
                    "novel-chapter-refresh",
                    "Novels",
                    "Refresh novel chapter source",
                    ProfileId: account.ProfileId,
                    Lane: OperationLane.Normal,
                    IsDownload: true,
                    Retryable: false),
                async (operation, token) =>
                {
                    await operation.ReportAsync(
                        10,
                        "Refreshing novel chapter source.",
                        cancellationToken: token);

                    await imports.DownloadChapterContentAsync(
                        id,
                        forceRefresh: true,
                        token);
                },
                "Novel chapter source refreshed.",
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
        try
        {
            await progress.SaveProgressAsync(
                account.ProfileId,
                id,
                positionPermille,
                anchorLanguage,
                anchorParagraphIndex,
                anchorOffset,
                cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }

        return new OkResult();
    }

    public async Task<IActionResult> OnPostBookmarkAsync(
        Guid id,
        int positionPermille,
        string? language,
        int? paragraphIndex,
        int characterOffset,
        string? label,
        string? style,
        string? color,
        CancellationToken cancellationToken)
    {
        try
        {
            var bookmark = await annotations.AddBookmarkAsync(
                account.ProfileId,
                id,
                positionPermille,
                language,
                paragraphIndex,
                characterOffset,
                label,
                style,
                color,
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
                bookmark.Label,
                bookmark.Style,
                bookmark.Color
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
        await annotations.RemoveBookmarkAsync(
            account.ProfileId,
            bookmarkId,
            cancellationToken);

        return IsFetchRequest()
            ? new OkResult()
            : RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostReaderSettingsAsync(
        Guid id,
        string? scope,
        string? changedKey,
        string? genre,
        int genrePriority,
        bool resetField,
        bool resetScope,
        ReaderSettingsInput input,
        CancellationToken cancellationToken)
    {
        var context = await catalog.GetChapterContextAsync(id, cancellationToken);
        if (context is null)
        {
            return NotFound();
        }

        try
        {
            var contentType = ReaderContentTypes.FromNovelMetadata(
                context.WorkFormat,
                context.SourceProvider);
            var scopeKey = ReaderPreferenceScopes.ResolveTarget(
                scope,
                contentType,
                context.WorkId,
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
                    return BadRequest("A reader setting key is required.");
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
                        ? context.WorkId
                        : null,
                    input,
                    cancellationToken);
            }

            var settings = await ReaderPreferenceStore.GetAsync(
                db,
                account.ProfileId,
                context.WorkId,
                context.GenresJson,
                contentType,
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
        CancellationToken cancellationToken)
    {
        var context = await catalog.GetChapterContextAsync(id, cancellationToken);
        if (context is null)
        {
            return NotFound();
        }

        await ReaderPreferenceStore.ResetBookAsync(
            db,
            account.ProfileId,
            context.WorkId,
            cancellationToken);

        var contentType = ReaderContentTypes.FromNovelMetadata(
            context.WorkFormat,
            context.SourceProvider);
        var settings = await ReaderPreferenceStore.GetAsync(
            db,
            account.ProfileId,
            context.WorkId,
            context.GenresJson,
            contentType,
            cancellationToken);

        return new JsonResult(new { settings });
    }

    public async Task<IActionResult> OnPostBookmarkAppearanceAsync(
        Guid id,
        Guid bookmarkId,
        string? style,
        string? color,
        CancellationToken cancellationToken)
    {
        var bookmark = await annotations.UpdateBookmarkAppearanceAsync(
            account.ProfileId,
            bookmarkId,
            style,
            color,
            cancellationToken);

        if (bookmark is null)
        {
            return NotFound();
        }

        return new JsonResult(new
        {
            bookmark.Id,
            bookmark.Style,
            bookmark.Color
        });
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
            var highlight = await annotations.AddHighlightAsync(
                account.ProfileId,
                id,
                language,
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
        await annotations.RemoveHighlightAsync(
            account.ProfileId,
            highlightId,
            cancellationToken);

        return IsFetchRequest()
            ? new OkResult()
            : RedirectToPage(new { id });
    }

    private NovelReaderAnchor ResolveInitialAnchor(Guid? bookmarkId, Guid? highlightId)
    {
        var hasTranslation = GermanParagraphs.Count > 0;

        if (bookmarkId is Guid requestedBookmark &&
            Annotations.Bookmarks.FirstOrDefault(x => x.Id == requestedBookmark) is { } bookmark)
        {
            return new NovelReaderAnchor(
                bookmark.Language,
                bookmark.ParagraphIndex,
                bookmark.CharacterOffset,
                bookmark.PositionPermille,
                bookmark.AnchorText,
                Forced: true);
        }

        if (highlightId is Guid requestedHighlight &&
            Annotations.Highlights.FirstOrDefault(x => x.Id == requestedHighlight) is { } highlight)
        {
            var paragraphs = highlight.Language == NovelReadingLanguage.German
                ? GermanParagraphs
                : JapaneseParagraphs;
            var anchorText = highlight.ParagraphIndex < paragraphs.Count
                ? NovelTextLayout.CreateAnchorText(paragraphs[highlight.ParagraphIndex])
                : null;

            return new NovelReaderAnchor(
                highlight.Language,
                highlight.ParagraphIndex,
                highlight.StartOffset,
                0,
                anchorText,
                Forced: true);
        }

        if (Progress is not null)
        {
            return new NovelReaderAnchor(
                Progress.AnchorLanguage,
                Progress.AnchorParagraphIndex,
                Progress.AnchorOffset,
                Progress.PositionPermille,
                Progress.AnchorText,
                Forced: false);
        }

        return new NovelReaderAnchor(
            hasTranslation ? NovelReadingLanguage.German : NovelReadingLanguage.Japanese,
            null,
            0,
            0,
            null,
            Forced: false);
    }

    private async Task<OperationSnapshot?> GetPreparationAsync(
        Guid? operationId,
        CancellationToken cancellationToken)
    {
        if (operationId is not Guid id)
        {
            return null;
        }

        var snapshot = await new OperationStore(db).GetAsync(id, cancellationToken);
        return snapshot is not null &&
            snapshot.Kind == NovelJobs.ChapterDownloadKind &&
            snapshot.ProfileId == account.ProfileId
                ? snapshot
                : null;
    }

    private bool IsFetchRequest() =>
        string.Equals(
            Request.Headers["X-Requested-With"].ToString(),
            "fetch",
            StringComparison.OrdinalIgnoreCase);
}
