using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Playback;
using AniLingo.Web.Features.Progress;
using AniLingo.Web.Features.Storage;

namespace AniLingo.Web.Features.ClientApi;

public enum OfflineDownloadLookupStatus
{
    Ready,
    EpisodeNotFound,
    MediaNotFound,
    StorageUnavailable
}

public sealed record OfflineDownloadLookup(
    OfflineDownloadLookupStatus Status,
    ClientOfflineDownloadDescriptor? Descriptor = null,
    ClientMediaAvailability? Availability = null);

/// <summary>
/// Builds offline download descriptors from the canonical player bootstrap,
/// the on-disk media identity and the active learning cue set. Only reads
/// source media; nothing is copied or written on the server.
/// </summary>
public sealed class ClientApiOfflineService(
    ClientApiService clientApi,
    PlaybackService playbackService,
    EpisodeProgressService progressService)
{
    public async Task<OfflineDownloadLookup> GetDownloadAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var player = await clientApi.GetPlayerAsync(episodeId, cancellationToken);
        if (player is null)
        {
            return new(OfflineDownloadLookupStatus.EpisodeNotFound);
        }

        var media = player.Media;
        if (media is null)
        {
            return new(OfflineDownloadLookupStatus.MediaNotFound);
        }

        if (media.Availability.State != ClientApiMappings.AvailabilityStateName(
                StorageAvailabilityState.Available))
        {
            return new(
                OfflineDownloadLookupStatus.StorageUnavailable,
                Availability: media.Availability);
        }

        var stream = await playbackService.GetOriginalContentAsync(
            media.MediaFileId,
            cancellationToken);
        var file = stream is null ? null : new FileInfo(stream.SourcePath);
        if (file is not { Exists: true })
        {
            return new(OfflineDownloadLookupStatus.MediaNotFound);
        }

        var fingerprint = await MediaInventoryService.TryComputeFingerprintAsync(
            file.FullName,
            cancellationToken);
        if (fingerprint is null)
        {
            return new(
                OfflineDownloadLookupStatus.StorageUnavailable,
                Availability: media.Availability with { State = "source_unreachable", Retryable = true });
        }

        var cues = player.ActiveLearningSubtitleTrackId is { } trackId
            ? await clientApi.GetCuesAsync(episodeId, trackId, null, null, cancellationToken)
            : null;
        var progress = await progressService.GetAsync(episodeId, cancellationToken)
            ?? new EpisodeProgressSnapshot(episodeId, 0, null, false, null);

        return new(
            OfflineDownloadLookupStatus.Ready,
            BuildDescriptor(
                player,
                media,
                file,
                fingerprint,
                cues ?? new ClientCueResponse(null, null, null, []),
                progress,
                DateTime.UtcNow));
    }

    public static ClientOfflineDownloadDescriptor BuildDescriptor(
        ClientPlayerBootstrap player,
        ClientPlayerMedia media,
        FileInfo file,
        string fingerprint,
        ClientCueResponse learningCues,
        EpisodeProgressSnapshot progress,
        DateTime issuedAtUtc) =>
        new(
            ClientApiContract.ApiVersion,
            issuedAtUtc,
            player.Episode,
            new ClientOfflineMedia(
                media.MediaFileId,
                media.FileName,
                media.ContentType,
                file.Length,
                media.DurationMs,
                media.VideoCodec,
                media.PixelFormat,
                media.AudioCodec,
                ClientApiOfflineRoutes.Content(media.MediaFileId),
                ClientApiOfflineContract.EntityTag(file.Length, file.LastWriteTimeUtc),
                fingerprint,
                ClientApiOfflineContract.FingerprintAlgorithm),
            player.AudioTracks,
            player.SubtitleTracks,
            player.DefaultAudioTrackId,
            player.DefaultSubtitleTrackId,
            learningCues,
            ClientApiMappings.ToClientEpisodeProgress(progress));
}
