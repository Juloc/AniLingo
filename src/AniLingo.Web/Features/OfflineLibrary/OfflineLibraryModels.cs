namespace AniLingo.Web.Features.OfflineLibrary;

/// <summary>
/// Tombstone of a bookmark removed through offline sync. Kept so a stale
/// "add"/"edit" replay arriving after a newer removal can never resurrect the
/// bookmark (last-writer-wins by client timestamp, mirroring
/// <c>OfflineProgressReconciler</c>'s monotonic rule for playback progress).
/// One row per bookmark id; re-adding the same id later replaces it.
/// </summary>
public sealed class NovelBookmarkTombstone
{
    /// <summary>Same id the live bookmark had; a primary key, not a foreign key (the bookmark is gone).</summary>
    public Guid BookmarkId { get; set; }
    public string ProfileId { get; set; } = "";
    public Guid WorkId { get; set; }

    /// <summary>Client-asserted last-writer-wins timestamp of the removal.</summary>
    public DateTime DeletedAtUtc { get; set; }

    /// <summary>When the server first recorded this tombstone (for future pruning).</summary>
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>One volume of a work as it appears in the offline manifest.</summary>
public sealed record OfflineLibraryVolume(
    Guid VolumeId,
    int Number,
    string? Title,
    string Kind,
    string? CoverAssetUrl);

/// <summary>One chapter's identity, order and differential version.</summary>
public sealed record OfflineLibraryChapterRef(
    Guid ChapterId,
    Guid VolumeId,
    int Number,
    string Title,
    string Hash,
    bool HasContent,
    bool HasTranslation);

/// <summary>
/// Canonical versioned offline package manifest for one work: enough for a
/// client to plan a differential download (per-chapter hash comparison) and
/// render a library/detail "offline available" state without contacting the
/// server again until a chapter hash changes.
/// </summary>
public sealed record OfflineLibraryManifest(
    Guid WorkId,
    int SchemaVersion,
    string ContentVersion,
    string Title,
    string? Author,
    string? Description,
    string? CoverAssetUrl,
    DateTime IssuedAtUtc,
    IReadOnlyList<OfflineLibraryVolume> Volumes,
    IReadOnlyList<OfflineLibraryChapterRef> Chapters);

/// <summary>One inline text/heading/image block, wire-shaped for offline storage (mirrors <c>NovelContentBlock</c>).</summary>
public sealed record OfflineLibraryContentBlock(
    string Kind,
    int Level,
    IReadOnlyList<OfflineLibraryInlineRun>? Runs,
    string? ImageAssetUrl,
    string? ImageAlt);

public sealed record OfflineLibraryInlineRun(
    string Text,
    string? Ruby,
    bool Emphasis,
    bool Strong);

/// <summary>One cached translation of a chapter.</summary>
public sealed record OfflineLibraryTranslation(
    string TargetLanguage,
    string Text);

/// <summary>
/// Full content of one chapter for offline storage: original text/blocks plus
/// every cached translation, so the client never needs to pick a language up
/// front. Referenced images are content-addressed asset URLs the client fetches
/// and stores itself (never embedded as bytes here).
/// </summary>
public sealed record OfflineLibraryChapterPayload(
    Guid ChapterId,
    Guid WorkId,
    Guid VolumeId,
    int Number,
    string Title,
    string Hash,
    string OriginalText,
    IReadOnlyList<OfflineLibraryContentBlock> Blocks,
    IReadOnlyList<OfflineLibraryTranslation> Translations);

/// <summary>View model for the reusable "Offline speichern" partial (Pages/Shared/_OfflineLibraryAction.cshtml).</summary>
public sealed record OfflineLibraryActionViewModel(Guid WorkId, string Title);
