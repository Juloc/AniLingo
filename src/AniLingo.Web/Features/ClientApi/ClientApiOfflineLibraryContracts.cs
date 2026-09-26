using AniLingo.Web.Features.OfflineLibrary;

namespace AniLingo.Web.Features.ClientApi;

/// <summary>
/// Additive v1 contract for the offline Book/Novel library (#221 part 1): a
/// canonical versioned manifest per work, a chapter payload endpoint, an asset
/// endpoint and a batch sync endpoint for reading progress and bookmarks.
/// Reuses the same authenticated <c>/api/client/v1</c> boundary as the rest of
/// the native client API; this is the shared contract both the PWA download
/// manager (part 1) and, later, Android (part 2) consume.
/// </summary>
public static class ClientApiOfflineLibraryRoutes
{
    public static string Manifest(Guid workId) =>
        $"{ClientApiContract.BasePath}/offline-library/works/{workId:D}/manifest";

    public static string Chapter(Guid chapterId) =>
        $"{ClientApiContract.BasePath}/offline-library/chapters/{chapterId:D}";

    public static string Asset(Guid volumeId, string asset) =>
        $"{ClientApiContract.BasePath}/offline-library/assets/{volumeId:D}/{Uri.EscapeDataString(asset)}";

    public static string Sync =>
        $"{ClientApiContract.BasePath}/offline-library/sync";
}

public static class ClientApiOfflineLibraryContract
{
    public static string ProgressOutcomeName(OfflineLibraryProgressOutcome outcome) =>
        outcome switch
        {
            OfflineLibraryProgressOutcome.Applied => "applied",
            OfflineLibraryProgressOutcome.Unchanged => "unchanged",
            OfflineLibraryProgressOutcome.IgnoredBehind => "ignored_behind",
            OfflineLibraryProgressOutcome.ChapterNotFound => "chapter_not_found",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
        };

    public static string BookmarkOutcomeName(OfflineBookmarkOutcome outcome) =>
        outcome switch
        {
            OfflineBookmarkOutcome.Applied => "applied",
            OfflineBookmarkOutcome.Removed => "removed",
            OfflineBookmarkOutcome.Unchanged => "unchanged",
            OfflineBookmarkOutcome.IgnoredStale => "ignored_stale",
            OfflineBookmarkOutcome.ChapterNotFound => "chapter_not_found",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
        };

    public static OfflineBookmarkEventType ParseBookmarkEventType(string? type) =>
        string.Equals(type, "remove", StringComparison.OrdinalIgnoreCase)
            ? OfflineBookmarkEventType.Remove
            : OfflineBookmarkEventType.Upsert;
}

public sealed record ClientOfflineLibraryVolume(
    Guid VolumeId,
    int Number,
    string? Title,
    string Kind,
    string? CoverAssetUrl);

public sealed record ClientOfflineLibraryChapterRef(
    Guid ChapterId,
    Guid VolumeId,
    int Number,
    string Title,
    string Hash,
    bool HasContent,
    bool HasTranslation);

public sealed record ClientOfflineLibraryManifest(
    Guid WorkId,
    int SchemaVersion,
    string ContentVersion,
    string Title,
    string? Author,
    string? Description,
    string? CoverAssetUrl,
    DateTime IssuedAtUtc,
    IReadOnlyList<ClientOfflineLibraryVolume> Volumes,
    IReadOnlyList<ClientOfflineLibraryChapterRef> Chapters);

public sealed record ClientOfflineLibraryInlineRun(
    string Text,
    string? Ruby,
    bool Emphasis,
    bool Strong);

public sealed record ClientOfflineLibraryContentBlock(
    string Kind,
    int Level,
    IReadOnlyList<ClientOfflineLibraryInlineRun>? Runs,
    string? ImageAssetUrl,
    string? ImageAlt);

public sealed record ClientOfflineLibraryTranslation(
    string TargetLanguage,
    string Text);

public sealed record ClientOfflineLibraryChapterPayload(
    Guid ChapterId,
    Guid WorkId,
    Guid VolumeId,
    int Number,
    string Title,
    string Hash,
    string OriginalText,
    IReadOnlyList<ClientOfflineLibraryContentBlock> Blocks,
    IReadOnlyList<ClientOfflineLibraryTranslation> Translations);

public sealed record ClientOfflineLibrarySyncBatch(
    IReadOnlyList<ClientOfflineProgressEvent>? Progress,
    IReadOnlyList<ClientOfflineBookmarkEvent>? Bookmarks);

public sealed record ClientOfflineProgressEvent(
    Guid ClientEventId,
    Guid WorkId,
    Guid ChapterId,
    int PositionPermille,
    string? AnchorLanguage,
    int? AnchorParagraphIndex,
    int AnchorOffset,
    DateTime ClientTimestampUtc);

public sealed record ClientOfflineBookmarkEvent(
    Guid ClientEventId,
    Guid BookmarkId,
    string Type,
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

public sealed record ClientOfflineLibrarySyncResult(
    IReadOnlyList<ClientOfflineProgressSyncResult> Progress,
    IReadOnlyList<ClientOfflineBookmarkSyncResult> Bookmarks);

public sealed record ClientOfflineProgressSyncResult(
    Guid ClientEventId,
    Guid WorkId,
    string Outcome,
    int? PositionPermille);

public sealed record ClientOfflineBookmarkSyncResult(
    Guid ClientEventId,
    Guid BookmarkId,
    string Outcome);
