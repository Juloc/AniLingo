using AniLingo.Web.Data;
using AniLingo.Web.Features.ReaderPreferences;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Novels;

public sealed record NovelChapterAnnotations(
    IReadOnlyList<NovelBookmark> Bookmarks,
    IReadOnlyList<NovelHighlight> Highlights,
    int OtherChapterBookmarkCount,
    int OtherChapterHighlightCount);

public enum NovelNoteKind
{
    Bookmark,
    Highlight
}

public sealed record NovelWorkNote(
    Guid Id,
    Guid ChapterId,
    int ChapterNumber,
    string? ChapterTitle,
    int? VolumeNumber,
    string? VolumeTitle,
    string? Title,
    string? Detail,
    int PositionPermille,
    string? Style,
    string? Color,
    DateTime CreatedAt);

public sealed record NovelWorkNotesPage(
    NovelNoteKind Kind,
    IReadOnlyList<NovelWorkNote> Items,
    int NextOffset,
    bool HasMore);

/// <summary>Nearest bookmark before/after a position, ordered by chapter then position (wraps around).</summary>
public sealed record NovelAdjacentBookmark(
    Guid Id,
    Guid ChapterId,
    int ChapterNumber,
    string? Label,
    int PositionPermille);

/// <summary>
/// Profile-scoped bookmarks and highlights. The reader loads only the current
/// chapter's annotations; work-wide notes are paged on demand.
/// </summary>
public sealed class NovelAnnotationService(AppDbContext db)
{
    public const int MaxHighlightLength = 2000;
    public const int WorkNotesPageSize = 40;
    public const int MaxSearchQueryLength = 200;

    public async Task<NovelBookmark> AddBookmarkAsync(
        string profileId,
        Guid chapterId,
        int positionPermille,
        string? language,
        int? paragraphIndex,
        int characterOffset,
        string? label,
        string? style,
        string? color,
        CancellationToken cancellationToken)
    {
        var normalizedLanguage = NovelReadingLanguage.Normalize(language);
        var text = await NovelChapterText.LoadAsync(
            db,
            chapterId,
            normalizedLanguage,
            cancellationToken)
            ?? throw new InvalidOperationException("Novel chapter was not found.");

        var (resolvedParagraph, resolvedOffset, anchorText) =
            NovelChapterText.ResolveAnchor(
                text.Paragraphs,
                paragraphIndex,
                characterOffset);

        var bookmark = new NovelBookmark
        {
            ProfileId = profileId,
            WorkId = text.WorkId,
            ChapterId = text.ChapterId,
            PositionPermille = Math.Clamp(positionPermille, 0, 1000),
            Language = normalizedLanguage,
            ParagraphIndex = resolvedParagraph,
            CharacterOffset = resolvedOffset,
            AnchorText = anchorText,
            Label = NovelChapterText.NormalizeOptional(label, 120),
            Style = ReaderPreferenceRules.NormalizeBookmarkStyle(style),
            Color = ReaderPreferenceRules.NormalizeBookmarkColor(color)
        };

        db.NovelBookmarks.Add(bookmark);
        await db.SaveChangesAsync(cancellationToken);
        return bookmark;
    }

    public async Task<NovelBookmark?> UpdateBookmarkAppearanceAsync(
        string profileId,
        Guid bookmarkId,
        string? style,
        string? color,
        CancellationToken cancellationToken)
    {
        var bookmark = await db.NovelBookmarks
            .SingleOrDefaultAsync(
                x => x.Id == bookmarkId && x.ProfileId == profileId,
                cancellationToken);

        if (bookmark is null)
        {
            return null;
        }

        bookmark.Style = ReaderPreferenceRules.NormalizeBookmarkStyle(style);
        bookmark.Color = ReaderPreferenceRules.NormalizeBookmarkColor(color);
        await db.SaveChangesAsync(cancellationToken);
        return bookmark;
    }

    /// <summary>Renames a bookmark (or clears its name when <paramref name="label"/> is blank).</summary>
    public async Task<NovelBookmark?> UpdateBookmarkLabelAsync(
        string profileId,
        Guid bookmarkId,
        string? label,
        CancellationToken cancellationToken)
    {
        var bookmark = await db.NovelBookmarks
            .SingleOrDefaultAsync(
                x => x.Id == bookmarkId && x.ProfileId == profileId,
                cancellationToken);

        if (bookmark is null)
        {
            return null;
        }

        bookmark.Label = NovelChapterText.NormalizeOptional(label, 120);
        await db.SaveChangesAsync(cancellationToken);
        return bookmark;
    }

    public Task RemoveBookmarkAsync(
        string profileId,
        Guid bookmarkId,
        CancellationToken cancellationToken) =>
        db.NovelBookmarks
            .Where(x => x.Id == bookmarkId && x.ProfileId == profileId)
            .ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// Saves a highlight inside one paragraph. Overlapping highlights are valid
    /// and rendered as nested segments; an exact duplicate range returns the
    /// existing highlight instead of creating a second row.
    /// </summary>
    public async Task<NovelHighlight> AddHighlightAsync(
        string profileId,
        Guid chapterId,
        string? language,
        int paragraphIndex,
        int startOffset,
        int endOffset,
        string? note,
        CancellationToken cancellationToken)
    {
        var normalizedLanguage = NovelReadingLanguage.Normalize(language);
        var text = await NovelChapterText.LoadAsync(
            db,
            chapterId,
            normalizedLanguage,
            cancellationToken)
            ?? throw new InvalidOperationException("Novel chapter was not found.");

        if (paragraphIndex < 0 || paragraphIndex >= text.Paragraphs.Count)
        {
            throw new InvalidOperationException("The selected paragraph no longer exists.");
        }

        var paragraph = text.Paragraphs[paragraphIndex];
        var start = Math.Clamp(startOffset, 0, paragraph.Length);
        var end = Math.Clamp(endOffset, 0, paragraph.Length);

        if (end <= start)
        {
            throw new InvalidOperationException("Select some text before creating a highlight.");
        }

        if (end - start > MaxHighlightLength)
        {
            throw new InvalidOperationException(
                $"A highlight can contain at most {MaxHighlightLength} characters.");
        }

        var normalizedNote = NovelChapterText.NormalizeOptional(note, 2000);
        var duplicate = await db.NovelHighlights
            .SingleOrDefaultAsync(
                x => x.ProfileId == profileId &&
                    x.ChapterId == text.ChapterId &&
                    x.Language == normalizedLanguage &&
                    x.ParagraphIndex == paragraphIndex &&
                    x.StartOffset == start &&
                    x.EndOffset == end,
                cancellationToken);

        if (duplicate is not null)
        {
            if (normalizedNote is not null && duplicate.Note != normalizedNote)
            {
                duplicate.Note = normalizedNote;
                await db.SaveChangesAsync(cancellationToken);
            }

            return duplicate;
        }

        var highlight = new NovelHighlight
        {
            ProfileId = profileId,
            WorkId = text.WorkId,
            ChapterId = text.ChapterId,
            Language = normalizedLanguage,
            ParagraphIndex = paragraphIndex,
            StartOffset = start,
            EndOffset = end,
            Text = paragraph[start..end],
            Note = normalizedNote
        };

        db.NovelHighlights.Add(highlight);
        await db.SaveChangesAsync(cancellationToken);
        return highlight;
    }

    public Task RemoveHighlightAsync(
        string profileId,
        Guid highlightId,
        CancellationToken cancellationToken) =>
        db.NovelHighlights
            .Where(x => x.Id == highlightId && x.ProfileId == profileId)
            .ExecuteDeleteAsync(cancellationToken);

    /// <summary>Edits a highlight's note in place (or clears it when <paramref name="note"/> is blank).</summary>
    public async Task<NovelHighlight?> UpdateHighlightNoteAsync(
        string profileId,
        Guid highlightId,
        string? note,
        CancellationToken cancellationToken)
    {
        var highlight = await db.NovelHighlights
            .SingleOrDefaultAsync(
                x => x.Id == highlightId && x.ProfileId == profileId,
                cancellationToken);

        if (highlight is null)
        {
            return null;
        }

        highlight.Note = NovelChapterText.NormalizeOptional(note, 2000);
        await db.SaveChangesAsync(cancellationToken);
        return highlight;
    }

    /// <summary>
    /// Initial reader payload: annotations of the current chapter plus
    /// aggregate counts for the rest of the work (no work-wide rows).
    /// </summary>
    public async Task<NovelChapterAnnotations> GetChapterAnnotationsAsync(
        string profileId,
        Guid workId,
        Guid chapterId,
        CancellationToken cancellationToken)
    {
        var bookmarks = await db.NovelBookmarks
            .AsNoTracking()
            .Where(x => x.ProfileId == profileId && x.ChapterId == chapterId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var highlights = await db.NovelHighlights
            .AsNoTracking()
            .Where(x => x.ProfileId == profileId && x.ChapterId == chapterId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var counts = await db.NovelWorks
            .AsNoTracking()
            .Where(x => x.Id == workId)
            .Select(x => new
            {
                Bookmarks = db.NovelBookmarks.Count(bookmark =>
                    bookmark.ProfileId == profileId &&
                    bookmark.WorkId == workId &&
                    bookmark.ChapterId != chapterId),
                Highlights = db.NovelHighlights.Count(highlight =>
                    highlight.ProfileId == profileId &&
                    highlight.WorkId == workId &&
                    highlight.ChapterId != chapterId)
            })
            .SingleOrDefaultAsync(cancellationToken);

        return new NovelChapterAnnotations(
            bookmarks,
            highlights,
            counts?.Bookmarks ?? 0,
            counts?.Highlights ?? 0);
    }

    /// <summary>
    /// One bounded page of the profile's notes in other chapters of the work,
    /// newest first.
    /// </summary>
    public async Task<NovelWorkNotesPage> GetWorkNotesAsync(
        string profileId,
        Guid workId,
        Guid excludeChapterId,
        NovelNoteKind kind,
        int offset,
        CancellationToken cancellationToken)
    {
        offset = Math.Max(0, offset);
        const int take = WorkNotesPageSize + 1;

        var rows = kind == NovelNoteKind.Bookmark
            ? await BookmarkNotes(
                    db.NovelBookmarks.AsNoTracking().Where(x =>
                        x.ProfileId == profileId &&
                        x.WorkId == workId &&
                        x.ChapterId != excludeChapterId))
                .Skip(offset)
                .Take(take)
                .ToListAsync(cancellationToken)
            : await HighlightNotes(
                    db.NovelHighlights.AsNoTracking().Where(x =>
                        x.ProfileId == profileId &&
                        x.WorkId == workId &&
                        x.ChapterId != excludeChapterId))
                .Skip(offset)
                .Take(take)
                .ToListAsync(cancellationToken);

        var hasMore = rows.Count > WorkNotesPageSize;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return new NovelWorkNotesPage(
            kind,
            rows,
            offset + rows.Count,
            hasMore);
    }

    /// <summary>
    /// One bounded, profile-scoped page of notes across the whole work whose
    /// label/text or note contains <paramref name="query"/>. Server-side and
    /// paged like <see cref="GetWorkNotesAsync"/>; the current chapter is
    /// included so search covers every annotation of the work.
    /// </summary>
    public async Task<NovelWorkNotesPage> SearchWorkNotesAsync(
        string profileId,
        Guid workId,
        NovelNoteKind kind,
        string query,
        int offset,
        CancellationToken cancellationToken)
    {
        offset = Math.Max(0, offset);
        const int take = WorkNotesPageSize + 1;

        var needle = query.Trim();
        if (needle.Length > MaxSearchQueryLength)
        {
            needle = needle[..MaxSearchQueryLength];
        }

        var pattern = "%" + needle
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";

        var rows = kind == NovelNoteKind.Bookmark
            ? await BookmarkNotes(
                    db.NovelBookmarks.AsNoTracking().Where(x =>
                        x.ProfileId == profileId &&
                        x.WorkId == workId &&
                        (EF.Functions.Like(x.Label, pattern, "\\") ||
                            EF.Functions.Like(x.AnchorText, pattern, "\\"))))
                .Skip(offset)
                .Take(take)
                .ToListAsync(cancellationToken)
            : await HighlightNotes(
                    db.NovelHighlights.AsNoTracking().Where(x =>
                        x.ProfileId == profileId &&
                        x.WorkId == workId &&
                        (EF.Functions.Like(x.Text, pattern, "\\") ||
                            EF.Functions.Like(x.Note, pattern, "\\"))))
                .Skip(offset)
                .Take(take)
                .ToListAsync(cancellationToken);

        var hasMore = rows.Count > WorkNotesPageSize;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return new NovelWorkNotesPage(
            kind,
            rows,
            offset + rows.Count,
            hasMore);
    }

    /// <summary>
    /// The nearest bookmark before/after a chapter+position in the whole work,
    /// ordered by chapter number then position; wraps around at either end.
    /// Bounded to a single row.
    /// </summary>
    public async Task<NovelAdjacentBookmark?> GetAdjacentBookmarkAsync(
        string profileId,
        Guid workId,
        int currentChapterNumber,
        int currentPositionPermille,
        bool forward,
        CancellationToken cancellationToken)
    {
        // Filtering/ordering happens on this anonymous projection, not on the
        // final NovelAdjacentBookmark record: EF Core cannot translate a
        // Where/OrderBy that reads properties back off a constructed record.
        var bookmarks =
            from bookmark in db.NovelBookmarks.AsNoTracking()
            join chapter in db.NovelChapters.AsNoTracking()
                on bookmark.ChapterId equals chapter.Id
            where bookmark.ProfileId == profileId && bookmark.WorkId == workId
            select new
            {
                bookmark.Id,
                bookmark.ChapterId,
                ChapterNumber = chapter.Number,
                bookmark.Label,
                bookmark.PositionPermille
            };

        var row = forward
            ? await bookmarks
                .Where(x =>
                    x.ChapterNumber > currentChapterNumber ||
                    (x.ChapterNumber == currentChapterNumber && x.PositionPermille > currentPositionPermille))
                .OrderBy(x => x.ChapterNumber)
                .ThenBy(x => x.PositionPermille)
                .FirstOrDefaultAsync(cancellationToken)
            : await bookmarks
                .Where(x =>
                    x.ChapterNumber < currentChapterNumber ||
                    (x.ChapterNumber == currentChapterNumber && x.PositionPermille < currentPositionPermille))
                .OrderByDescending(x => x.ChapterNumber)
                .ThenByDescending(x => x.PositionPermille)
                .FirstOrDefaultAsync(cancellationToken);

        row ??= forward
            ? await bookmarks
                .OrderBy(x => x.ChapterNumber)
                .ThenBy(x => x.PositionPermille)
                .FirstOrDefaultAsync(cancellationToken)
            : await bookmarks
                .OrderByDescending(x => x.ChapterNumber)
                .ThenByDescending(x => x.PositionPermille)
                .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new NovelAdjacentBookmark(row.Id, row.ChapterId, row.ChapterNumber, row.Label, row.PositionPermille);
    }

    /// <summary>
    /// Projects filtered bookmarks to work notes with chapter/volume labels
    /// attached, newest first. Ordering happens before the projection so EF
    /// Core can translate it; ordering the already-projected record fails.
    /// </summary>
    private IQueryable<NovelWorkNote> BookmarkNotes(IQueryable<NovelBookmark> bookmarks) =>
        from bookmark in bookmarks
        join chapter in db.NovelChapters.AsNoTracking()
            on bookmark.ChapterId equals chapter.Id
        join volume in db.NovelVolumes.AsNoTracking()
            on chapter.VolumeId equals volume.Id
        orderby bookmark.CreatedAt descending
        select new NovelWorkNote(
            bookmark.Id,
            bookmark.ChapterId,
            chapter.Number,
            chapter.Title,
            volume.Kind == NovelVolumeKinds.Epub ? (int?)volume.Number : null,
            volume.Kind == NovelVolumeKinds.Epub ? volume.Title : null,
            bookmark.Label,
            bookmark.AnchorText,
            bookmark.PositionPermille,
            bookmark.Style,
            bookmark.Color,
            bookmark.CreatedAt);

    /// <summary>
    /// Projects filtered highlights to work notes with chapter/volume labels
    /// attached, newest first (see <see cref="BookmarkNotes"/> for why the
    /// ordering happens before the projection).
    /// </summary>
    private IQueryable<NovelWorkNote> HighlightNotes(IQueryable<NovelHighlight> highlights) =>
        from highlight in highlights
        join chapter in db.NovelChapters.AsNoTracking()
            on highlight.ChapterId equals chapter.Id
        join volume in db.NovelVolumes.AsNoTracking()
            on chapter.VolumeId equals volume.Id
        orderby highlight.CreatedAt descending
        select new NovelWorkNote(
            highlight.Id,
            highlight.ChapterId,
            chapter.Number,
            chapter.Title,
            volume.Kind == NovelVolumeKinds.Epub ? (int?)volume.Number : null,
            volume.Kind == NovelVolumeKinds.Epub ? volume.Title : null,
            highlight.Text,
            highlight.Note,
            0,
            null,
            null,
            highlight.CreatedAt);
}
