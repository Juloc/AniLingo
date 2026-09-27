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

// The one Direct Play rule set: the post-download optimizer asks it whether a remux would widen
// Direct Play, and playback asks it whether the untouched file plays as is.
public static class MediaPlaybackCompatibility
{
    public static DirectPlayEvaluation Evaluate(
        PlaybackClientProfile profile,
        MediaPlaybackCharacteristics media)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(media);

        var container = profile.Containers.FirstOrDefault(x => x.Container == media.Container);
        if (container is null)
        {
            return new(profile, false, $"{media.Container} container");
        }

        var video = container.Video.FirstOrDefault(x =>
            string.Equals(x.Codec, media.VideoCodec, StringComparison.OrdinalIgnoreCase));
        if (video is null)
        {
            return new(profile, false, $"{media.VideoCodec ?? "unknown"} video");
        }

        if (video.PixelFormats is { } formats &&
            (media.PixelFormat is null || !formats.Contains(media.PixelFormat)))
        {
            return new(profile, false, $"{media.VideoCodec} {media.PixelFormat ?? "unknown pixel format"}");
        }

        if (video.CodecTags is { } tags &&
            (media.VideoCodecTag is null || !tags.Contains(media.VideoCodecTag)))
        {
            return new(profile, false, $"{media.VideoCodec} tagged {media.VideoCodecTag ?? "unknown"}");
        }

        if (media.AudioCodec is not null && !container.Audio.Contains(media.AudioCodec))
        {
            return new(profile, false, $"{media.AudioCodec} audio");
        }

        return new(profile, true, null);
    }

    public static IReadOnlyList<PlaybackClientProfile> DirectPlayProfiles(
        MediaPlaybackCharacteristics media,
        IEnumerable<PlaybackClientProfile>? profiles = null) =>
        [
            .. (profiles ?? PlaybackClientProfiles.All)
                .Where(profile => Evaluate(profile, media).CanDirectPlay)
        ];
}
