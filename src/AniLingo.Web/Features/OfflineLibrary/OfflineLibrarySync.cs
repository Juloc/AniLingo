using AniLingo.Web.Data;
using AniLingo.Web.Features.Novels;
using AniLingo.Web.Features.ReaderPreferences;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.OfflineLibrary;

public sealed record OfflineLibraryProgressCheckpoint(
    Guid ClientEventId,
    Guid WorkId,
    Guid ChapterId,
    int PositionPermille,
    string? AnchorLanguage,
    int? AnchorParagraphIndex,
    int AnchorOffset,
    DateTime ClientTimestampUtc);

public enum OfflineLibraryProgressOutcome
{
    /// <summary>The checkpoint moved reading position forward (later chapter, or same chapter/later position).</summary>
    Applied,

    /// <summary>The server already holds exactly this position (a replayed request).</summary>
    Unchanged,

    /// <summary>The server already holds a later position; progress never moves backwards.</summary>
    IgnoredBehind,

    ChapterNotFound
}

public sealed record OfflineLibraryProgressResult(
    Guid ClientEventId,
    Guid WorkId,
    OfflineLibraryProgressOutcome Outcome,
    NovelProgress? Progress);

public enum OfflineBookmarkEventType
{
    /// <summary>Creates the bookmark if it does not exist yet, else edits it in place.</summary>
    Upsert,
    Remove
}

public sealed record OfflineBookmarkEvent(
    Guid ClientEventId,
    Guid BookmarkId,
    OfflineBookmarkEventType Type,
    Guid WorkId,
    Guid ChapterId,
    string? Language,
    int PositionPermille,
    int? ParagraphIndex,
    int CharacterOffset,
    string? AnchorText,
    string? Label,
    string? Style,
    string? Color,
    DateTime ClientTimestampUtc);

public enum OfflineBookmarkOutcome
{
    /// <summary>The bookmark was created or edited.</summary>
    Applied,

    /// <summary>The bookmark was removed (tombstoned).</summary>
    Removed,

    /// <summary>The server already reflects exactly this event (a replay).</summary>
    Unchanged,

    /// <summary>A newer add/edit/remove of the same bookmark already won; last-writer-wins by client timestamp.</summary>
    IgnoredStale,

    ChapterNotFound
}

public sealed record OfflineBookmarkResult(
    Guid ClientEventId,
    Guid BookmarkId,
    OfflineBookmarkOutcome Outcome);

/// <summary>
/// Pure conflict-resolution rules for offline sync (#221 part 1). Kept free of
/// EF/IO so both server tests and behavior review can reason about them
/// directly; the reconcilers below only resolve state from the database and
/// apply whatever these functions decide.
/// </summary>
public static class OfflineLibrarySyncRules
{
    /// <summary>
    /// Forward-only, like <c>OfflineProgressReconciler.Decide</c> for episode
    /// playback (#341): reading position is a (chapter number, position within
    /// chapter) tuple and can only move forward or stay put.
    /// </summary>
    public static OfflineLibraryProgressOutcome DecideProgress(
        (int ChapterNumber, int PositionPermille)? current,
        (int ChapterNumber, int PositionPermille) incoming)
    {
        if (current is not { } value)
        {
            return OfflineLibraryProgressOutcome.Applied;
        }

        if (incoming.ChapterNumber == value.ChapterNumber &&
            incoming.PositionPermille == value.PositionPermille)
        {
            return OfflineLibraryProgressOutcome.Unchanged;
        }

        return incoming.ChapterNumber > value.ChapterNumber ||
            (incoming.ChapterNumber == value.ChapterNumber && incoming.PositionPermille > value.PositionPermille)
            ? OfflineLibraryProgressOutcome.Applied
            : OfflineLibraryProgressOutcome.IgnoredBehind;
    }

    /// <summary>
    /// Last-writer-wins by client timestamp. A tombstone's deletion timestamp
    /// and a live bookmark's edit timestamp are compared on equal footing, so a
    /// stale add/edit can never resurrect a bookmark removed later, and a
    /// remove can never rewind a newer edit. Equal timestamps favor Upsert over
    /// Remove (a resurrection at the exact same instant is deterministic, not a
    /// race), and an event that exactly repeats the currently known state is
    /// reported <see cref="OfflineBookmarkOutcome.Unchanged"/> (idempotent replay).
    /// </summary>
    public static OfflineBookmarkOutcome DecideBookmark(
        DateTime? currentBookmarkSyncUpdatedAtUtc,
        DateTime? currentTombstoneDeletedAtUtc,
        OfflineBookmarkEventType type,
        DateTime incomingTimestampUtc)
    {
        var known = Later(currentBookmarkSyncUpdatedAtUtc, currentTombstoneDeletedAtUtc);
        if (known is { } timestamp && incomingTimestampUtc < timestamp)
        {
            return OfflineBookmarkOutcome.IgnoredStale;
        }

        if (type == OfflineBookmarkEventType.Remove)
        {
            return currentBookmarkSyncUpdatedAtUtc is null &&
                currentTombstoneDeletedAtUtc == incomingTimestampUtc
                ? OfflineBookmarkOutcome.Unchanged
                : OfflineBookmarkOutcome.Removed;
        }

        return currentTombstoneDeletedAtUtc is null &&
            currentBookmarkSyncUpdatedAtUtc == incomingTimestampUtc
            ? OfflineBookmarkOutcome.Unchanged
            : OfflineBookmarkOutcome.Applied;
    }

    private static DateTime? Later(DateTime? a, DateTime? b) =>
        a is null ? b : b is null ? a : a > b ? a : b;
}

/// <summary>
/// Replays offline reading-progress checkpoints through the canonical
/// <see cref="NovelProgressService"/>. Mirrors <c>OfflineProgressReconciler</c>
/// (episode playback, #341): the decision is pure and the write path is the
/// same one the online reader uses, so there is no second progress writer.
/// </summary>
public sealed class OfflineLibraryProgressReconciler(AppDbContext db, NovelProgressService progressService)
{
    public async Task<IReadOnlyList<OfflineLibraryProgressResult>> ReconcileAsync(
        string profileId,
        IReadOnlyList<OfflineLibraryProgressCheckpoint> checkpoints,
        CancellationToken cancellationToken)
    {
        var results = new List<OfflineLibraryProgressResult>(checkpoints.Count);

        foreach (var checkpoint in checkpoints)
        {
            var chapter = await db.NovelChapters
                .AsNoTracking()
                .Where(x => x.Id == checkpoint.ChapterId && x.WorkId == checkpoint.WorkId)
                .Select(x => new { x.Number })
                .SingleOrDefaultAsync(cancellationToken);

            if (chapter is null)
            {
                results.Add(new(checkpoint.ClientEventId, checkpoint.WorkId, OfflineLibraryProgressOutcome.ChapterNotFound, null));
                continue;
            }

            var current = await db.NovelProgress
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.ProfileId == profileId && x.WorkId == checkpoint.WorkId,
                    cancellationToken);

            (int ChapterNumber, int PositionPermille)? currentTuple = null;
            if (current is not null)
            {
                var currentChapterNumber = await db.NovelChapters
                    .AsNoTracking()
                    .Where(x => x.Id == current.ChapterId)
                    .Select(x => x.Number)
                    .SingleOrDefaultAsync(cancellationToken);
                currentTuple = (currentChapterNumber, current.PositionPermille);
            }

            var positionPermille = Math.Clamp(checkpoint.PositionPermille, 0, 1000);
            var outcome = OfflineLibrarySyncRules.DecideProgress(currentTuple, (chapter.Number, positionPermille));

            if (outcome != OfflineLibraryProgressOutcome.Applied)
            {
                results.Add(new(checkpoint.ClientEventId, checkpoint.WorkId, outcome, current));
                continue;
            }

            await progressService.SaveProgressAsync(
                profileId,
                checkpoint.ChapterId,
                positionPermille,
                checkpoint.AnchorLanguage,
                checkpoint.AnchorParagraphIndex,
                checkpoint.AnchorOffset,
                cancellationToken);

            var updated = await db.NovelProgress
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.ProfileId == profileId && x.WorkId == checkpoint.WorkId,
                    cancellationToken);

            results.Add(new(checkpoint.ClientEventId, checkpoint.WorkId, outcome, updated));
        }

        return results;
    }
}

/// <summary>
/// Replays offline bookmark add/edit/remove events with last-writer-wins
/// conflict resolution and tombstones (see <see cref="OfflineLibrarySyncRules.DecideBookmark"/>).
/// The bookmark id is client-supplied so the same bookmark can be referenced
/// by later edits/removals before the server has ever seen it.
/// </summary>
public sealed class OfflineLibraryBookmarkReconciler(AppDbContext db)
{
    public async Task<IReadOnlyList<OfflineBookmarkResult>> ReconcileAsync(
        string profileId,
        IReadOnlyList<OfflineBookmarkEvent> events,
        CancellationToken cancellationToken)
    {
        var results = new List<OfflineBookmarkResult>(events.Count);

        foreach (var bookmarkEvent in events)
        {
            var chapterExists = await db.NovelChapters
                .AsNoTracking()
                .AnyAsync(
                    x => x.Id == bookmarkEvent.ChapterId && x.WorkId == bookmarkEvent.WorkId,
                    cancellationToken);

            if (!chapterExists)
            {
                results.Add(new(bookmarkEvent.ClientEventId, bookmarkEvent.BookmarkId, OfflineBookmarkOutcome.ChapterNotFound));
                continue;
            }

            var bookmark = await db.NovelBookmarks
                .SingleOrDefaultAsync(
                    x => x.Id == bookmarkEvent.BookmarkId && x.ProfileId == profileId,
                    cancellationToken);
            var tombstone = await db.NovelBookmarkTombstones
                .SingleOrDefaultAsync(
                    x => x.BookmarkId == bookmarkEvent.BookmarkId && x.ProfileId == profileId,
                    cancellationToken);

            var outcome = OfflineLibrarySyncRules.DecideBookmark(
                bookmark?.SyncUpdatedAt,
                tombstone?.DeletedAtUtc,
                bookmarkEvent.Type,
                bookmarkEvent.ClientTimestampUtc);

            switch (outcome)
            {
                case OfflineBookmarkOutcome.Applied:
                    await ApplyUpsertAsync(profileId, bookmark, tombstone, bookmarkEvent, cancellationToken);
                    break;

                case OfflineBookmarkOutcome.Removed:
                    await ApplyRemoveAsync(profileId, bookmark, tombstone, bookmarkEvent, cancellationToken);
                    break;
            }

            results.Add(new(bookmarkEvent.ClientEventId, bookmarkEvent.BookmarkId, outcome));
        }

        return results;
    }

    private async Task ApplyUpsertAsync(
        string profileId,
        NovelBookmark? bookmark,
        NovelBookmarkTombstone? tombstone,
        OfflineBookmarkEvent bookmarkEvent,
        CancellationToken cancellationToken)
    {
        var language = NovelReadingLanguage.Normalize(bookmarkEvent.Language);
        var text = await NovelChapterText.LoadAsync(db, bookmarkEvent.ChapterId, language, cancellationToken);
        var (paragraphIndex, offset, resolvedAnchorText) = text is null
            ? (bookmarkEvent.ParagraphIndex, Math.Max(0, bookmarkEvent.CharacterOffset), (string?)null)
            : NovelChapterText.ResolveAnchor(text.Paragraphs, bookmarkEvent.ParagraphIndex, bookmarkEvent.CharacterOffset);

        if (bookmark is null)
        {
            bookmark = new NovelBookmark
            {
                Id = bookmarkEvent.BookmarkId,
                ProfileId = profileId,
                WorkId = bookmarkEvent.WorkId
            };
            db.NovelBookmarks.Add(bookmark);
        }

        bookmark.ChapterId = bookmarkEvent.ChapterId;
        bookmark.PositionPermille = Math.Clamp(bookmarkEvent.PositionPermille, 0, 1000);
        bookmark.Language = language;
        bookmark.ParagraphIndex = paragraphIndex;
        bookmark.CharacterOffset = offset;
        bookmark.AnchorText = resolvedAnchorText
            ?? NovelChapterText.NormalizeOptional(bookmarkEvent.AnchorText, NovelTextLayout.AnchorTextLimit);
        bookmark.Label = NovelChapterText.NormalizeOptional(bookmarkEvent.Label, 120);
        bookmark.Style = ReaderPreferenceRules.NormalizeBookmarkStyle(bookmarkEvent.Style);
        bookmark.Color = ReaderPreferenceRules.NormalizeBookmarkColor(bookmarkEvent.Color);
        bookmark.SyncUpdatedAt = bookmarkEvent.ClientTimestampUtc;
        bookmark.ClientEventId = bookmarkEvent.ClientEventId;

        if (tombstone is not null)
        {
            db.NovelBookmarkTombstones.Remove(tombstone);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyRemoveAsync(
        string profileId,
        NovelBookmark? bookmark,
        NovelBookmarkTombstone? tombstone,
        OfflineBookmarkEvent bookmarkEvent,
        CancellationToken cancellationToken)
    {
        if (bookmark is not null)
        {
            db.NovelBookmarks.Remove(bookmark);
        }

        if (tombstone is null)
        {
            db.NovelBookmarkTombstones.Add(new NovelBookmarkTombstone
            {
                BookmarkId = bookmarkEvent.BookmarkId,
                ProfileId = profileId,
                WorkId = bookmarkEvent.WorkId,
                DeletedAtUtc = bookmarkEvent.ClientTimestampUtc
            });
        }
        else
        {
            tombstone.DeletedAtUtc = bookmarkEvent.ClientTimestampUtc;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
