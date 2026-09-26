namespace AniLingo.Web.Features.MediaSegments;

public sealed record MediaSegmentDetectionRequest(
    Guid EpisodeId,
    Guid AnimeId,
    string MediaPath,
    string MediaIdentity,
    double? DurationSeconds);

public sealed record DetectedMediaSegment(
    MediaSegmentKind Kind,
    long StartMs,
    long EndMs,
    double Confidence);

// Automatic detection hook. Results are stored with method/version/confidence
// and can be rebuilt; they never outrank manual, imported or provider markers.
// A detector must not modify source media.
public interface IMediaSegmentDetector
{
    string Method { get; }

    string Version { get; }

    Task<IReadOnlyList<DetectedMediaSegment>> DetectAsync(
        MediaSegmentDetectionRequest request,
        CancellationToken cancellationToken);
}

// Default until fingerprint-based OP/ED detection exists: no marker rather than a guess.
public sealed class NoOpMediaSegmentDetector : IMediaSegmentDetector
{
    public string Method => "none";

    public string Version => "0";

    public Task<IReadOnlyList<DetectedMediaSegment>> DetectAsync(
        MediaSegmentDetectionRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DetectedMediaSegment>>([]);
}
