using AniLingo.Web.Features.Progress;

namespace AniLingo.Web.Features.ClientApi;

/// <summary>
/// Additive v1 contract for bounded offline playback on native clients (#225).
/// The server stays the canonical owner of media identity and playback
/// progress; a client keeps only a verified local media copy plus a queue of
/// offline checkpoints that is replayed through <see cref="Progress"/>.
/// </summary>
public static class ClientApiOfflineRoutes
{
    public static string Download(Guid episodeId) =>
        $"{ClientApiRoutes.Episode(episodeId)}/offline-download";

    public static string Content(Guid mediaFileId) =>
        $"{ClientApiContract.BasePath}/offline/media/{mediaFileId:D}/content";

    public static string Progress =>
        $"{ClientApiContract.BasePath}/offline/progress";
}

public static class ClientApiOfflineContract
{
    /// <summary>Upper bound of checkpoints accepted by one reconciliation request.</summary>
    public const int MaxProgressBatchItems = 100;

    /// <summary>
    /// SHA-256 over the little-endian file length, the first 64 KiB and the last
    /// 64 KiB. Same bounded fingerprint as the persisted media inventory.
    /// </summary>
    public const string FingerprintAlgorithm = "sha256-length-head-tail-64k";

    /// <summary>
    /// Strong validator of one on-disk version of a media file. It changes when
    /// the file length or modification time changes, so a resumed range request
    /// (<c>If-Range</c>) can never splice bytes of two different file versions.
    /// </summary>
    public static string EntityTag(long sizeBytes, DateTime lastWriteTimeUtc) =>
        $"\"{sizeBytes:x}-{lastWriteTimeUtc.ToUniversalTime().Ticks:x}\"";

    public static string OutcomeName(OfflineProgressOutcome outcome) =>
        outcome switch
        {
            OfflineProgressOutcome.Applied => "applied",
            OfflineProgressOutcome.Completed => "completed",
            OfflineProgressOutcome.Unchanged => "unchanged",
            OfflineProgressOutcome.IgnoredBehind => "ignored_behind",
            OfflineProgressOutcome.IgnoredWatched => "ignored_watched",
            OfflineProgressOutcome.IgnoredAccidentalStart => "ignored_accidental_start",
            OfflineProgressOutcome.EpisodeNotFound => "episode_not_found",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
        };
}

/// <summary>
/// Everything a native client needs to download, verify and later play one
/// episode without server connectivity. Never contains host filesystem paths.
/// </summary>
public sealed record ClientOfflineDownloadDescriptor(
    int ApiVersion,
    DateTime IssuedAtUtc,
    ClientPlayerEpisode Episode,
    ClientOfflineMedia Media,
    IReadOnlyList<ClientMediaTrack> AudioTracks,
    IReadOnlyList<ClientMediaTrack> SubtitleTracks,
    string? DefaultAudioTrackId,
    string? DefaultSubtitleTrackId,
    ClientCueResponse LearningCues,
    ClientEpisodeProgress Progress);

public sealed record ClientOfflineMedia(
    Guid MediaFileId,
    string FileName,
    string ContentType,
    long SizeBytes,
    long? DurationMs,
    string? VideoCodec,
    string? PixelFormat,
    string? AudioCodec,
    string ContentUrl,
    string ETag,
    string Fingerprint,
    string FingerprintAlgorithm);

public sealed record ClientOfflineProgressBatch(
    IReadOnlyList<ClientOfflineProgressItem>? Items);

public sealed record ClientOfflineProgressItem(
    Guid EpisodeId,
    long PositionMs,
    long? DurationMs,
    bool Completed);

public sealed record ClientOfflineProgressResponse(
    IReadOnlyList<ClientOfflineProgressResult> Results);

public sealed record ClientOfflineProgressResult(
    Guid EpisodeId,
    string Outcome,
    ClientEpisodeProgress? Progress);
