using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.OfflineLibrary;

namespace AniLingo.Web.Features.ClientApi;

/// <summary>
/// Thin adapter between the wire contract (<see cref="ClientOfflineLibraryManifest"/>
/// etc.) and the offline-library domain (Features/OfflineLibrary). Manifest,
/// chapter and asset reads are shared library content and only require
/// authentication; the sync endpoint is profile-scoped so progress and
/// bookmarks never cross profiles.
/// </summary>
public sealed class ClientApiOfflineLibraryService(
    OfflineLibraryQueries queries,
    OfflineLibraryProgressReconciler progressReconciler,
    OfflineLibraryBookmarkReconciler bookmarkReconciler,
    CurrentAccountContext currentAccount)
{
    public async Task<ClientOfflineLibraryManifest?> GetManifestAsync(
        Guid workId,
        CancellationToken cancellationToken)
    {
        var manifest = await queries.GetManifestAsync(workId, cancellationToken);
        return manifest is null ? null : ToClientManifest(manifest);
    }

    public async Task<ClientOfflineLibraryChapterPayload?> GetChapterAsync(
        Guid chapterId,
        CancellationToken cancellationToken)
    {
        var payload = await queries.GetChapterPayloadAsync(chapterId, cancellationToken);
        return payload is null ? null : ToClientChapter(payload);
    }

    public string? ResolveAssetPath(Guid volumeId, string asset) =>
        queries.ResolveAssetPath(volumeId, asset);

    public async Task<ClientOfflineLibrarySyncResult> SyncAsync(
        ClientOfflineLibrarySyncBatch batch,
        CancellationToken cancellationToken)
    {
        var profileId = currentAccount.ProfileId;

        var progressEvents = (batch.Progress ?? [])
            .Select(x => new OfflineLibraryProgressCheckpoint(
                x.ClientEventId,
                x.WorkId,
                x.ChapterId,
                x.PositionPermille,
                x.AnchorLanguage,
                x.AnchorParagraphIndex,
                x.AnchorOffset,
                DateTime.SpecifyKind(x.ClientTimestampUtc, DateTimeKind.Utc)))
            .ToArray();

        var bookmarkEvents = (batch.Bookmarks ?? [])
            .Select(x => new OfflineBookmarkEvent(
                x.ClientEventId,
                x.BookmarkId,
                ClientApiOfflineLibraryContract.ParseBookmarkEventType(x.Type),
                x.WorkId,
                x.ChapterId,
                x.Language,
                x.PositionPermille,
                x.ParagraphIndex,
                x.CharacterOffset,
                x.AnchorText,
                x.Label,
                x.Style,
                x.Color,
                DateTime.SpecifyKind(x.ClientTimestampUtc, DateTimeKind.Utc)))
            .ToArray();

        var progressResults = await progressReconciler.ReconcileAsync(profileId, progressEvents, cancellationToken);
        var bookmarkResults = await bookmarkReconciler.ReconcileAsync(profileId, bookmarkEvents, cancellationToken);

        return new ClientOfflineLibrarySyncResult(
            progressResults
                .Select(x => new ClientOfflineProgressSyncResult(
                    x.ClientEventId,
                    x.WorkId,
                    ClientApiOfflineLibraryContract.ProgressOutcomeName(x.Outcome),
                    x.Progress?.PositionPermille))
                .ToArray(),
            bookmarkResults
                .Select(x => new ClientOfflineBookmarkSyncResult(
                    x.ClientEventId,
                    x.BookmarkId,
                    ClientApiOfflineLibraryContract.BookmarkOutcomeName(x.Outcome)))
                .ToArray());
    }

    private static ClientOfflineLibraryManifest ToClientManifest(OfflineLibraryManifest manifest) =>
        new(
            manifest.WorkId,
            manifest.SchemaVersion,
            manifest.ContentVersion,
            manifest.Title,
            manifest.Author,
            manifest.Description,
            manifest.CoverAssetUrl,
            manifest.IssuedAtUtc,
            manifest.Volumes
                .Select(x => new ClientOfflineLibraryVolume(x.VolumeId, x.Number, x.Title, x.Kind, x.CoverAssetUrl))
                .ToArray(),
            manifest.Chapters
                .Select(x => new ClientOfflineLibraryChapterRef(
                    x.ChapterId, x.VolumeId, x.Number, x.Title, x.Hash, x.HasContent, x.HasTranslation))
                .ToArray());

    private static ClientOfflineLibraryChapterPayload ToClientChapter(OfflineLibraryChapterPayload payload) =>
        new(
            payload.ChapterId,
            payload.WorkId,
            payload.VolumeId,
            payload.Number,
            payload.Title,
            payload.Hash,
            payload.OriginalText,
            payload.Blocks
                .Select(block => new ClientOfflineLibraryContentBlock(
                    block.Kind,
                    block.Level,
                    block.Runs?.Select(run => new ClientOfflineLibraryInlineRun(run.Text, run.Ruby, run.Emphasis, run.Strong)).ToArray(),
                    block.ImageAssetUrl,
                    block.ImageAlt))
                .ToArray(),
            payload.Translations
                .Select(x => new ClientOfflineLibraryTranslation(x.TargetLanguage, x.Text))
                .ToArray());
}
