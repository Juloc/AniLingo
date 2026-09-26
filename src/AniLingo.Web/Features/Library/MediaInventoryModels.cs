namespace AniLingo.Web.Features.Library;

public enum MediaAnalysisStatus
{
    // The last attempt could not run ffprobe (missing tool, timeout); retried by the next
    // reconciliation or use once MediaInventoryService.PendingRetryDelay has passed.
    Pending = 0,
    Succeeded = 1,
    // ffprobe rejected the file; kept until the source identity or probe version changes.
    Failed = 2
}

public enum MediaStreamKind
{
    Audio = 0,
    Subtitle = 1
}

// Canonical technical analysis of one MediaFile. The Source* columns record which version of the
// file (size, mtime, bounded content fingerprint) the analysis describes; the MediaFile row keeps
// the scanner-observed identity. A mismatch or a ProbeVersion change makes the analysis stale.
public sealed class MediaAnalysis
{
    public Guid MediaFileId { get; set; }
    public MediaAnalysisStatus Status { get; set; }
    public int ProbeVersion { get; set; }
    public long SourceSizeBytes { get; set; }
    public DateTime SourceLastWriteTimeUtc { get; set; }
    public string? SourceFingerprint { get; set; }
    public string? Diagnostic { get; set; }
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    public string? Container { get; set; }
    public double? DurationSeconds { get; set; }
    public string? VideoCodec { get; set; }
    public string? VideoProfile { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? PixelFormat { get; set; }
    public int? BitDepth { get; set; }
    public string? DynamicRange { get; set; }
}

public sealed class MediaAnalysisStream
{
    public Guid MediaFileId { get; set; }
    public int StreamIndex { get; set; }
    public MediaStreamKind Kind { get; set; }
    public string? Codec { get; set; }
    public string? Language { get; set; }
    public string? Title { get; set; }
    public int? Channels { get; set; }
    public string? ChannelLayout { get; set; }
    public bool IsDefault { get; set; }
    public bool IsForced { get; set; }
}

public sealed record MediaVideoInfo(
    string? Codec,
    string? Profile,
    int? Width,
    int? Height,
    string? PixelFormat,
    int? BitDepth,
    string? DynamicRange);

public sealed record MediaStreamInfo(
    int Index,
    MediaStreamKind Kind,
    string? Codec,
    string? Language,
    string? Title,
    int? Channels,
    string? ChannelLayout,
    bool IsDefault,
    bool IsForced)
{
    private static readonly HashSet<string> TextSubtitleCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "ass",
        "ssa",
        "subrip",
        "srt",
        "webvtt",
        "mov_text",
        "text"
    };

    public bool IsText =>
        Kind == MediaStreamKind.Subtitle &&
        Codec is not null &&
        TextSubtitleCodecs.Contains(Codec);
}

public sealed record MediaTechnicalInfo(
    string? Container,
    double? DurationSeconds,
    MediaVideoInfo? Video,
    IReadOnlyList<MediaStreamInfo> Streams)
{
    public IReadOnlyList<MediaStreamInfo> AudioStreams =>
        [.. Streams.Where(x => x.Kind == MediaStreamKind.Audio)];

    public IReadOnlyList<MediaStreamInfo> SubtitleStreams =>
        [.. Streams.Where(x => x.Kind == MediaStreamKind.Subtitle)];
}

// Technical is set only for Succeeded analyses.
public sealed record MediaInventoryEntry(
    Guid MediaFileId,
    MediaAnalysisStatus Status,
    int ProbeVersion,
    DateTime AnalyzedAt,
    string? Diagnostic,
    MediaTechnicalInfo? Technical);

public sealed record MediaInventoryReconciliation(
    int Unchanged,
    int Analyzed,
    int Failed,
    int Deferred);
