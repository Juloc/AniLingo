namespace Jularr.Web.Features.Media.Compatibility;

public enum MediaContainerFamily
{
    Unknown,
    Mp4,
    WebM,
    Matroska,
    Ogg,
    MpegTs,
    Avi
}

public static class MediaContainers
{
    public static MediaContainerFamily FromPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp4" or ".m4v" or ".mov" => MediaContainerFamily.Mp4,
            ".webm" => MediaContainerFamily.WebM,
            ".mkv" or ".mka" => MediaContainerFamily.Matroska,
            ".ogg" or ".ogv" => MediaContainerFamily.Ogg,
            ".ts" or ".m2ts" => MediaContainerFamily.MpegTs,
            ".avi" => MediaContainerFamily.Avi,
            _ => MediaContainerFamily.Unknown
        };

    // Reads the container from ffprobe's format_name. The Matroska demuxer reports
    // "matroska,webm" for both, so only there the extension tells WebM from Matroska.
    public static MediaContainerFamily FromProbe(string? formatName, string path)
    {
        if (string.IsNullOrWhiteSpace(formatName))
        {
            return FromPath(path);
        }

        var names = formatName.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (names.Contains("matroska", StringComparer.OrdinalIgnoreCase))
        {
            return FromPath(path) == MediaContainerFamily.WebM
                ? MediaContainerFamily.WebM
                : MediaContainerFamily.Matroska;
        }

        if (names.Contains("mp4", StringComparer.OrdinalIgnoreCase) ||
            names.Contains("mov", StringComparer.OrdinalIgnoreCase))
        {
            return MediaContainerFamily.Mp4;
        }

        if (names.Contains("ogg", StringComparer.OrdinalIgnoreCase))
        {
            return MediaContainerFamily.Ogg;
        }

        if (names.Contains("mpegts", StringComparer.OrdinalIgnoreCase))
        {
            return MediaContainerFamily.MpegTs;
        }

        return names.Contains("avi", StringComparer.OrdinalIgnoreCase)
            ? MediaContainerFamily.Avi
            : MediaContainerFamily.Unknown;
    }
}

// What a client receives when it plays a file untouched: the container, the programme video and
// the audio stream that starts without a track selection. Built from the actual streams.
public sealed record MediaPlaybackCharacteristics(
    MediaContainerFamily Container,
    string? VideoCodec,
    string? VideoCodecTag,
    string? PixelFormat,
    string? AudioCodec);

// A video codec a client decodes inside one container. PixelFormats/CodecTags, when set,
// restrict it (e.g. 8-bit 4:2:0 H.264, or HEVC only when tagged hvc1 for WebKit).
public sealed record VideoCodecSupport(
    string Codec,
    IReadOnlySet<string>? PixelFormats = null,
    IReadOnlySet<string>? CodecTags = null);

public sealed record ContainerSupport(
    MediaContainerFamily Container,
    IReadOnlyList<VideoCodecSupport> Video,
    IReadOnlySet<string> Audio);

// A Direct Play capability profile: what a browser family is known to play without help.
// Profiles list confirmed support only; hardware-dependent codecs (HEVC in Chromium) stay out
// until runtime capability detection (#403) can confirm them per device.
public sealed record PlaybackClientProfile(
    string Id,
    string Name,
    IReadOnlyList<ContainerSupport> Containers);

public static class PlaybackClientProfiles
{
    private static readonly IReadOnlySet<string> Browser420Formats = Set("yuv420p", "yuvj420p");
    private static readonly IReadOnlySet<string> Hdr420Formats = Set("yuv420p", "yuvj420p", "yuv420p10le");

    private static readonly ContainerSupport WebMSupport = new(
        MediaContainerFamily.WebM,
        [new("vp8"), new("vp9"), new("av1")],
        Set("opus", "vorbis"));

    // What every browser Jularr supports plays; the historical universal Direct Play rule.
    public static PlaybackClientProfile BrowserBaseline { get; } = new(
        "browser-baseline",
        "Every browser",
        [
            new(MediaContainerFamily.Mp4, [new("h264", Browser420Formats)], Set("aac", "mp3")),
            WebMSupport,
            new(MediaContainerFamily.Ogg, [new("theora"), new("vp8")], Set("vorbis", "opus"))
        ]);

    // Safari and installed PWAs on iPhone, iPad and macOS.
    public static PlaybackClientProfile AppleWebKit { get; } = new(
        "apple-webkit",
        "Safari (iOS, iPadOS, macOS)",
        [
            new(
                MediaContainerFamily.Mp4,
                [
                    new("h264", Browser420Formats),
                    new("hevc", Hdr420Formats, Set("hvc1"))
                ],
                Set("aac", "mp3", "ac3", "eac3", "alac", "flac"))
        ]);

    // Chrome and Edge on Windows, macOS, Linux and Android, including installed PWAs.
    public static PlaybackClientProfile Chromium { get; } = new(
        "chromium",
        "Chrome / Edge",
        [
            new(
                MediaContainerFamily.Mp4,
                [new("h264", Browser420Formats), new("vp9", Hdr420Formats), new("av1", Hdr420Formats)],
                Set("aac", "mp3", "opus", "flac")),
            WebMSupport
        ]);

    public static PlaybackClientProfile Firefox { get; } = new(
        "firefox",
        "Firefox",
        [
            new(
                MediaContainerFamily.Mp4,
                [new("h264", Browser420Formats), new("vp9", Hdr420Formats), new("av1", Hdr420Formats)],
                Set("aac", "mp3", "opus", "flac")),
            WebMSupport
        ]);

    public static IReadOnlyList<PlaybackClientProfile> All { get; } =
        [BrowserBaseline, AppleWebKit, Chromium, Firefox];

    private static HashSet<string> Set(params string[] values) =>
        new(values, StringComparer.OrdinalIgnoreCase);
}

public sealed record DirectPlayEvaluation(
    PlaybackClientProfile Profile,
    bool CanDirectPlay,
    string? Reason);

public enum MediaCompatibilityIssueKind
{
    Container,
    VideoCodec,
    PixelFormat,
    CodecTag,
    AudioCodec
}

// One reason a client cannot play the media untouched. Detail is the short technical phrase the
// optimizer journal records; runtime playback maps Kind to a machine-readable reason code.
public sealed record MediaCompatibilityIssue(
    MediaCompatibilityIssueKind Kind,
    string Detail);

// The one Direct Play rule set: the post-download optimizer asks it whether a remux would widen
// Direct Play, and runtime playback (PlaybackDecisionEngine) asks it whether the untouched file,
// or a lossless remux into another container, plays on the requesting client.
public static class MediaPlaybackCompatibility
{
    public static DirectPlayEvaluation Evaluate(
        PlaybackClientProfile profile,
        MediaPlaybackCharacteristics media)
    {
        var issues = Issues(profile, media);
        return new(profile, issues.Count == 0, issues.Count == 0 ? null : issues[0].Detail);
    }

    // Every issue, not just the first, so playback can explain all reasons at once. A missing
    // container stops the evaluation: codec support is only defined per container.
    public static IReadOnlyList<MediaCompatibilityIssue> Issues(
        PlaybackClientProfile profile,
        MediaPlaybackCharacteristics media)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(media);

        var container = profile.Containers.FirstOrDefault(x => x.Container == media.Container);
        if (container is null)
        {
            return [new(MediaCompatibilityIssueKind.Container, $"{media.Container} container")];
        }

        var issues = new List<MediaCompatibilityIssue>();
        var video = container.Video.FirstOrDefault(x =>
            string.Equals(x.Codec, media.VideoCodec, StringComparison.OrdinalIgnoreCase));
        if (video is null)
        {
            issues.Add(new(MediaCompatibilityIssueKind.VideoCodec, $"{media.VideoCodec ?? "unknown"} video"));
        }
        else if (video.PixelFormats is { } formats &&
                 (media.PixelFormat is null || !formats.Contains(media.PixelFormat)))
        {
            issues.Add(new(
                MediaCompatibilityIssueKind.PixelFormat,
                $"{media.VideoCodec} {media.PixelFormat ?? "unknown pixel format"}"));
        }
        else if (video.CodecTags is { } tags &&
                 (media.VideoCodecTag is null || !tags.Contains(media.VideoCodecTag)))
        {
            issues.Add(new(
                MediaCompatibilityIssueKind.CodecTag,
                $"{media.VideoCodec} tagged {media.VideoCodecTag ?? "unknown"}"));
        }

        if (media.AudioCodec is not null && !container.Audio.Contains(media.AudioCodec))
        {
            issues.Add(new(MediaCompatibilityIssueKind.AudioCodec, $"{media.AudioCodec} audio"));
        }

        return issues;
    }

    public static IReadOnlyList<PlaybackClientProfile> DirectPlayProfiles(
        MediaPlaybackCharacteristics media,
        IEnumerable<PlaybackClientProfile>? profiles = null) =>
        [
            .. (profiles ?? PlaybackClientProfiles.All)
                .Where(profile => Evaluate(profile, media).CanDirectPlay)
        ];
}
