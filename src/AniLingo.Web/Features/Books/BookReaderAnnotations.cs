using AniLingo.Web.Data;
using AniLingo.Web.Features.Novels;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Books;

public sealed record BookReaderChapterNavItem(
    Guid Id,
    int Number,
    string Title);

public sealed record BookReaderBookmarkItem(
    Guid Id,
    Guid ChapterId,
    int ChapterNumber,
    string ChapterTitle,
    int PositionPermille,
    string Language,
    string? Label,
    DateTime CreatedAt);

public sealed record BookReaderHighlightItem(
    Guid Id,
    Guid ChapterId,
    int ChapterNumber,
    string ChapterTitle,
    string Language,
    int ParagraphIndex,
    int StartOffset,
    int EndOffset,
    string Text,
    string? Note,
    DateTime CreatedAt);

public sealed record BookReaderAnnotations(
    IReadOnlyList<BookReaderBookmarkItem> Bookmarks,
    IReadOnlyList<BookReaderHighlightItem> Highlights);

public static class BookReaderAnnotationStore
{
    public static Task<List<BookReaderChapterNavItem>> GetChaptersAsync(
        AppDbContext db,
        Guid workId,
        string? query,
        CancellationToken cancellationToken)
    {
        var chapters = db.NovelChapters
            .AsNoTracking()
            .Where(x => x.WorkId == workId);

        var normalized = query?.Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            chapters = chapters.Where(x =>
                x.Title.Contains(normalized));
        }

        return chapters
            .OrderBy(x => x.Number)
            .Take(500)
            .Select(x => new BookReaderChapterNavItem(
                x.Id,
                x.Number,
                x.Title))
            .ToListAsync(cancellationToken);
    }

    public static async Task<BookReaderAnnotations> GetAnnotationsAsync(
        AppDbContext db,
        string profileId,
        Guid workId,
        CancellationToken cancellationToken)
    {
        var bookmarks = await (
            from bookmark in db.NovelBookmarks.AsNoTracking()
            join chapter in db.NovelChapters.AsNoTracking()
                on bookmark.ChapterId equals chapter.Id
            where bookmark.ProfileId == profileId
                && bookmark.WorkId == workId
            orderby chapter.Number, bookmark.PositionPermille, bookmark.CreatedAt
            select new BookReaderBookmarkItem(
                bookmark.Id,
                bookmark.ChapterId,
                chapter.Number,
                chapter.Title,
                bookmark.PositionPermille,
                bookmark.Language,
                bookmark.Label,
                bookmark.CreatedAt)
        ).ToListAsync(cancellationToken);

        var highlights = await (
            from highlight in db.NovelHighlights.AsNoTracking()
            join chapter in db.NovelChapters.AsNoTracking()
                on highlight.ChapterId equals chapter.Id
            where highlight.ProfileId == profileId
                && highlight.WorkId == workId
            orderby chapter.Number,
                highlight.ParagraphIndex,
                highlight.StartOffset,
                highlight.CreatedAt
            select new BookReaderHighlightItem(
                highlight.Id,
                highlight.ChapterId,
                chapter.Number,
                chapter.Title,
                highlight.Language,
                highlight.ParagraphIndex,
                highlight.StartOffset,
                highlight.EndOffset,
                highlight.Text,
                highlight.Note,
                highlight.CreatedAt)
        ).ToListAsync(cancellationToken);

        return new BookReaderAnnotations(
            bookmarks,
            highlights);
    }

    public static async Task<List<BookReaderHighlightItem>> GetChapterHighlightsAsync(
        AppDbContext db,
        string profileId,
        Guid chapterId,
        CancellationToken cancellationToken)
    {
        return await (
            from highlight in db.NovelHighlights.AsNoTracking()
            join chapter in db.NovelChapters.AsNoTracking()
                on highlight.ChapterId equals chapter.Id
            where highlight.ProfileId == profileId
                && highlight.ChapterId == chapterId
            orderby highlight.ParagraphIndex,
                highlight.StartOffset,
                highlight.CreatedAt
            select new BookReaderHighlightItem(
                highlight.Id,
                highlight.ChapterId,
                chapter.Number,
                chapter.Title,
                highlight.Language,
                highlight.ParagraphIndex,
                highlight.StartOffset,
                highlight.EndOffset,
                highlight.Text,
                highlight.Note,
                highlight.CreatedAt)
        ).ToListAsync(cancellationToken);
    }

    public static async Task<BookReaderHighlightItem> AddHighlightAsync(
        AppDbContext db,
        string profileId,
        BookReaderChapter reader,
        string? language,
        int paragraphIndex,
        int startOffset,
        int endOffset,
        string? note,
        CancellationToken cancellationToken)
    {
        var normalizedLanguage = NormalizeLanguage(
            language,
            reader.TargetLanguage);

        IReadOnlyList<string> paragraphs;
        if (normalizedLanguage == "original")
        {
            paragraphs = reader.OriginalParagraphs;
        }
        else
        {
            if (reader.Translation is null)
            {
                throw new InvalidOperationException(
                    "The selected translation is not available yet.");
            }

            paragraphs = reader.TranslatedParagraphs;
        }

        if (paragraphIndex < 0
            || paragraphIndex >= paragraphs.Count)
        {
            throw new InvalidOperationException(
                "The selected paragraph no longer exists.");
        }

        var paragraph = paragraphs[paragraphIndex];
        var start = Math.Clamp(
            startOffset,
            0,
            paragraph.Length);
        var end = Math.Clamp(
            endOffset,
            0,
            paragraph.Length);

        if (end <= start)
        {
            throw new InvalidOperationException(
                "Select some text before creating a highlight.");
        }

        if (end - start > 2000)
        {
            throw new InvalidOperationException(
                "A highlight can contain at most 2000 characters.");
        }

        var cleanNote = CleanOptional(
            note,
            2000);

        var highlight = new NovelHighlight
        {
            ProfileId = profileId,
            WorkId = reader.Work.Id,
            ChapterId = reader.Chapter.Id,
            Language = normalizedLanguage,
            ParagraphIndex = paragraphIndex,
            StartOffset = start,
            EndOffset = end,
            Text = paragraph[start..end],
            Note = cleanNote,
            CreatedAt = DateTime.UtcNow
        };

        db.NovelHighlights.Add(highlight);
        await db.SaveChangesAsync(cancellationToken);

        return new BookReaderHighlightItem(
            highlight.Id,
            highlight.ChapterId,
            reader.Chapter.Number,
            reader.Chapter.Title,
            highlight.Language,
            highlight.ParagraphIndex,
            highlight.StartOffset,
            highlight.EndOffset,
            highlight.Text,
            highlight.Note,
            highlight.CreatedAt);
    }

    public static async Task<bool> RemoveHighlightAsync(
        AppDbContext db,
        string profileId,
        Guid highlightId,
        CancellationToken cancellationToken)
    {
        var deleted = await db.NovelHighlights
            .Where(x =>
                x.Id == highlightId
                && x.ProfileId == profileId)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted > 0;
    }

    public static async Task<bool> RemoveBookmarkAsync(
        AppDbContext db,
        string profileId,
        Guid bookmarkId,
        CancellationToken cancellationToken)
    {
        var deleted = await db.NovelBookmarks
            .Where(x =>
                x.Id == bookmarkId
                && x.ProfileId == profileId)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted > 0;
    }

    private static string NormalizeLanguage(
        string? value,
        string targetLanguage)
    {
        if (string.Equals(
                value?.Trim(),
                "original",
                StringComparison.OrdinalIgnoreCase))
        {
            return "original";
        }

        return BookLanguageCatalog.Normalize(
            value,
            targetLanguage);
    }

    private static string? CleanOptional(
        string? value,
        int maxLength)
    {
        var clean = value?.Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return null;
        }

        return clean.Length <= maxLength
            ? clean
            : clean[..maxLength];
    }
}
