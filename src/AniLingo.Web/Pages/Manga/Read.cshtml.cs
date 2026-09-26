using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Localization;
using AniLingo.Web.Features.Manga;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Manga;

public sealed class ReadModel(
    AppDbContext db,
    CurrentAccountContext account) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public MangaChapterRead Chapter { get; private set; } = null!;
    public IReadOnlyList<MangaChapterItem> Chapters { get; private set; } = [];
    public IReadOnlyList<MangaBookmarkItem> Bookmarks { get; private set; } = [];
    public Guid? PreviousChapterId { get; private set; }
    public Guid? NextChapterId { get; private set; }
    public int InitialPage { get; private set; }
    public MangaReaderPreset ReaderSettings { get; private set; } = null!;
    public string ProfileId => account.ProfileId;

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        int? page,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var repository = new MangaRepository(db);
        var chapter = await repository.GetChapterAsync(id, cancellationToken);
        if (chapter is null)
        {
            return NotFound();
        }

        Chapter = chapter;
        Chapters = await repository.GetChaptersAsync(
            chapter.SeriesId,
            cancellationToken);
        Bookmarks = await repository.GetBookmarksAsync(
            account.ProfileId,
            chapter.SeriesId,
            cancellationToken);

        (PreviousChapterId, NextChapterId) =
            await repository.GetAdjacentChapterIdsAsync(
                chapter.SeriesId,
                chapter.Number,
                cancellationToken);

        var progress = await repository.GetProgressAsync(
            account.ProfileId,
            chapter.SeriesId,
            cancellationToken);

        // Local-only: the reader never waits on AniList. Remote progress and
        // the Operations-backed sync are on the series page's lazy card.
        ReaderSettings = await MangaReaderPreferenceStore.GetAsync(
            db,
            account.ProfileId,
            chapter.SeriesId,
            cancellationToken);

        var requested = page
            ?? (progress?.ChapterId == chapter.Id ? progress.PageIndex : 0);

        InitialPage = Math.Clamp(
            requested,
            0,
            Math.Max(0, chapter.PageCount - 1));

        return Page();
    }

    public async Task<IActionResult> OnPostPreferenceAsync(
        Guid id,
        string? scope,
        string? mode,
        CancellationToken cancellationToken)
    {
        var repository = new MangaRepository(db);
        var chapter = await repository.GetChapterAsync(id, cancellationToken);
        if (chapter is null)
        {
            return NotFound();
        }

        var seriesId = string.Equals(
            scope,
            "media",
            StringComparison.OrdinalIgnoreCase)
            ? (Guid?)null
            : chapter.SeriesId;

        await MangaReaderPreferenceStore.SaveModeAsync(
            db,
            account.ProfileId,
            seriesId,
            mode,
            cancellationToken);

        return new OkResult();
    }

    public async Task<IActionResult> OnPostResetPreferenceAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var repository = new MangaRepository(db);
        var chapter = await repository.GetChapterAsync(id, cancellationToken);
        if (chapter is null)
        {
            return NotFound();
        }

        await MangaReaderPreferenceStore.ResetSeriesAsync(
            db,
            account.ProfileId,
            chapter.SeriesId,
            cancellationToken);

        var settings = await MangaReaderPreferenceStore.GetAsync(
            db,
            account.ProfileId,
            chapter.SeriesId,
            cancellationToken);

        return new JsonResult(new { mode = settings.UiMode });
    }

    public async Task<IActionResult> OnGetPageAsync(
        Guid id,
        int page,
        CancellationToken cancellationToken)
    {
        var repository = new MangaRepository(db);
        var item = await repository.GetPageAsync(
            id,
            page,
            cancellationToken);

        if (item is null)
        {
            return NotFound();
        }

        var root = Path.GetFullPath(MangaImportService.CacheRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var cachedPath = Path.GetFullPath(item.CachedPath);

        if (!cachedPath.StartsWith(root, StringComparison.Ordinal) ||
            !System.IO.File.Exists(cachedPath))
        {
            return NotFound();
        }

        return new PhysicalFileResult(cachedPath, item.MimeType)
        {
            EnableRangeProcessing = true
        };
    }

    public async Task<IActionResult> OnPostProgressAsync(
        Guid id,
        int pageIndex,
        CancellationToken cancellationToken)
    {
        var repository = new MangaRepository(db);
        var chapter = await repository.GetChapterAsync(id, cancellationToken);
        if (chapter is null)
        {
            return NotFound();
        }

        await repository.SaveProgressAsync(
            account.ProfileId,
            chapter,
            pageIndex,
            cancellationToken);

        return new OkResult();
    }

    public async Task<IActionResult> OnPostBookmarkAsync(
        Guid id,
        int pageIndex,
        string? label,
        CancellationToken cancellationToken)
    {
        var repository = new MangaRepository(db);
        var chapter = await repository.GetChapterAsync(id, cancellationToken);
        if (chapter is null)
        {
            return NotFound();
        }

        var bookmark = await repository.AddBookmarkAsync(
            account.ProfileId,
            chapter,
            pageIndex,
            label,
            cancellationToken);

        return new JsonResult(new
        {
            bookmark.Id,
            bookmark.ChapterId,
            bookmark.PageIndex,
            bookmark.Label
        });
    }

    public async Task<IActionResult> OnPostRemoveBookmarkAsync(
        Guid id,
        Guid bookmarkId,
        CancellationToken cancellationToken)
    {
        var repository = new MangaRepository(db);
        await repository.RemoveBookmarkAsync(
            account.ProfileId,
            bookmarkId,
            cancellationToken);
        return new OkResult();
    }
}
