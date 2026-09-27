using System.Text.Json;
using System.Text.Json.Serialization;
using Jularr.Web.Features.Media.Compatibility;

namespace Jularr.Web.Features.Playback.Decision;

// Enum values travel as snake_case strings ("direct_stream", "confirmed") in every client contract.
public sealed class SnakeCaseEnumConverter<TEnum>()
    : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false)
    where TEnum : struct, Enum;

/// <summary>
/// How sure a client is that it handles a feature. Confirmed comes from a decoding API that
/// was asked about the exact configuration (MediaCapabilities, a native decoder query);
/// Inferred from weaker signals (canPlayType "probably", a known browser family); Unknown
/// means the client could not tell. The order matters: higher is more certain.
/// </summary>
[JsonConverter(typeof(SnakeCaseEnumConverter<PlaybackCapabilitySupport>))]
public enum PlaybackCapabilitySupport
{
    Unsupported = 0,
    Unknown = 1,
    Inferred = 2,
    Confirmed = 3
}

public static class PlaybackCapabilitySupportExtensions
{
    /// <summary>Inferred or confirmed support is enough to rely on; unknown is not.</summary>
    public static bool IsUsable(this PlaybackCapabilitySupport support) =>
        support >= PlaybackCapabilitySupport.Inferred;

    public static PlaybackCapabilitySupport Weakest(
        this PlaybackCapabilitySupport left,
        PlaybackCapabilitySupport right) =>
        left <= right ? left : right;
}

public sealed record ClientVideoCodecCapability(
    string Codec,
    PlaybackCapabilitySupport Support,
    IReadOnlyList<int>? BitDepths = null,
    int? MaxHeight = null,
    IReadOnlyList<string>? CodecTags = null,
    bool? Smooth = null,
    bool? PowerEfficient = null);

public sealed record ClientAudioCodecCapability(
    string Codec,
    PlaybackCapabilitySupport Support,
    int? MaxChannels = null);

public sealed record ClientContainerCapability(
    string Container,
    PlaybackCapabilitySupport Support,
    IReadOnlyList<ClientVideoCodecCapability>? Video = null,
    IReadOnlyList<ClientAudioCodecCapability>? Audio = null);

public sealed record ClientHdrCapabilities(
    PlaybackCapabilitySupport Display = PlaybackCapabilitySupport.Unknown,
    PlaybackCapabilitySupport Hdr10 = PlaybackCapabilitySupport.Unknown,
    PlaybackCapabilitySupport Hdr10Plus = PlaybackCapabilitySupport.Unknown,
    PlaybackCapabilitySupport Hlg = PlaybackCapabilitySupport.Unknown,
    PlaybackCapabilitySupport DolbyVision = PlaybackCapabilitySupport.Unknown);

/// <summary>
/// How a client can receive a stream the server produces. ProgressiveMp4 is one fragmented
/// MP4 response played straight from a URL; Hls is a playlist of fMP4 segments played natively
/// (Safari, Media3) — web clients never need a script HLS player for it.
/// </summary>
public sealed record ClientDeliveryCapabilities(
    PlaybackCapabilitySupport ProgressiveMp4 = PlaybackCapabilitySupport.Unknown,
    PlaybackCapabilitySupport Hls = PlaybackCapabilitySupport.Unknown,
    PlaybackCapabilitySupport MediaSource = PlaybackCapabilitySupport.Unknown,
    PlaybackCapabilitySupport FragmentedMp4 = PlaybackCapabilitySupport.Unknown);

/// <summary>
/// Text is a client that renders plain text cues itself (the web player overlays them from the
/// cues API); StyledAss renders ASS styling; Image renders bitmap subtitles such as PGS.
/// </summary>
public sealed record ClientSubtitleCapabilities(
    PlaybackCapabilitySupport Text = PlaybackCapabilitySupport.Unknown,
    PlaybackCapabilitySupport StyledAss = PlaybackCapabilitySupport.Unknown,
    PlaybackCapabilitySupport Image = PlaybackCapabilitySupport.Unknown);

/// <summary>
/// Platform features the client can use. AudioTrackSelection means the client switches between
/// audio tracks of one untouched file itself; without it a non-default track needs a remux.
/// </summary>
public sealed record ClientPlatformFeatures(
    PlaybackCapabilitySupport PictureInPicture = PlaybackCapabilitySupport.Unknown,
    PlaybackCapabilitySupport MediaSession = PlaybackCapabilitySupport.Unknown,
    PlaybackCapabilitySupport Fullscreen = PlaybackCapabilitySupport.Unknown,
    PlaybackCapabilitySupport OrientationLock = PlaybackCapabilitySupport.Unknown,
    PlaybackCapabilitySupport WakeLock = PlaybackCapabilitySupport.Unknown,
    PlaybackCapabilitySupport AudioTrackSelection = PlaybackCapabilitySupport.Unknown);

public sealed record ClientDisplayInfo(
    int? Width = null,
    int? Height = null,
    double? PixelRatio = null);

/// <summary>Who is asking: web, pwa, android, android_tv or another native client.</summary>
public sealed record ClientIdentity(
    string Kind,
    string? Name = null,
    string? Version = null,
    string? AppVersion = null);

/// <summary>
/// Everything a client reports about what it can play. The same document comes from the
/// browser/PWA probe and from native clients; the server decides from it and never from an
/// OS or user-agent label, except to infer a document for a client that sent none.
/// </summary>
public sealed record ClientPlaybackCapabilities(
    int SchemaVersion,
    ClientIdentity Client,
    IReadOnlyList<ClientContainerCapability> Containers,
    ClientHdrCapabilities? Hdr = null,
    ClientDeliveryCapabilities? Delivery = null,
    ClientSubtitleCapabilities? Subtitles = null,
    ClientPlatformFeatures? Features = null,
    ClientDisplayInfo? Display = null,
    bool Inferred = false)
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxContainers = 12;
    public const int MaxCodecsPerContainer = 24;

    [JsonIgnore]
    public ClientHdrCapabilities HdrOrDefault => Hdr ?? new();

    [JsonIgnore]
    public ClientDeliveryCapabilities DeliveryOrDefault => Delivery ?? new();

    [JsonIgnore]
    public ClientSubtitleCapabilities SubtitlesOrDefault => Subtitles ?? new();

    [JsonIgnore]
    public ClientPlatformFeatures FeaturesOrDefault => Features ?? new();

    public ClientContainerCapability? Container(MediaContainerFamily family) =>
        Containers.FirstOrDefault(x => PlaybackContainerNames.Parse(x.Container) == family);

    public PlaybackCapabilitySupport ContainerSupport(MediaContainerFamily family) =>
        Container(family)?.Support ?? PlaybackCapabilitySupport.Unsupported;

    public ClientVideoCodecCapability? VideoCodec(MediaContainerFamily family, string? codec) =>
        codec is null
            ? null
            : Container(family)?.Video?.FirstOrDefault(x => PlaybackCodecNames.SameVideo(x.Codec, codec));

    public ClientAudioCodecCapability? AudioCodec(MediaContainerFamily family, string? codec) =>
        codec is null
            ? null
            : Container(family)?.Audio?.FirstOrDefault(x => PlaybackCodecNames.SameAudio(x.Codec, codec));

    /// <summary>
    /// Bounds and normalizes an untrusted document: lowercases names, drops unknown
    /// containers, truncates lists and clamps numbers. Nothing from it ever reaches a
    /// command line; it only feeds the decision.
    /// </summary>
    public ClientPlaybackCapabilities Normalize()
    {
        var kind = ClientKinds.Normalize(Client?.Kind);
        var containers = (Containers ?? [])
            .Where(x => x is not null && PlaybackContainerNames.Parse(x.Container) != MediaContainerFamily.Unknown)
            .GroupBy(x => PlaybackContainerNames.Parse(x.Container))
            .Take(MaxContainers)
            .Select(group =>
            {
                var first = group.First();
                return new ClientContainerCapability(
                    PlaybackContainerNames.Name(group.Key),
                    first.Support,
                    [
                        .. (first.Video ?? [])
                            .Where(x => !string.IsNullOrWhiteSpace(x?.Codec))
                            .Take(MaxCodecsPerContainer)
                            .Select(x => x with
                            {
                                Codec = PlaybackCodecNames.NormalizeVideo(x.Codec),
                                BitDepths = x.BitDepths?.Where(depth => depth is >= 8 and <= 16).Distinct().Take(4).ToArray(),
                                MaxHeight = x.MaxHeight is > 0 and <= 8640 ? x.MaxHeight : null,
                                CodecTags = x.CodecTags?
                                    .Where(tag => !string.IsNullOrWhiteSpace(tag) && tag.Length <= 8)
                                    .Select(tag => tag.Trim().ToLowerInvariant())
                                    .Take(4)
                                    .ToArray()
                            })
                    ],
                    [
                        .. (first.Audio ?? [])
                            .Where(x => !string.IsNullOrWhiteSpace(x?.Codec))
                            .Take(MaxCodecsPerContainer)
                            .Select(x => x with
                            {
                                Codec = PlaybackCodecNames.NormalizeAudio(x.Codec),
                                MaxChannels = x.MaxChannels is > 0 and <= 32 ? x.MaxChannels : null
                            })
                    ]);
            })
            .ToArray();

        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            Client = new ClientIdentity(
                kind,
                Truncate(Client?.Name, 40),
                Truncate(Client?.Version, 40),
                Truncate(Client?.AppVersion, 40)),
            Containers = containers,
            Display = Display is null
                ? null
                : new ClientDisplayInfo(
                    Display.Width is > 0 and <= 16384 ? Display.Width : null,
                    Display.Height is > 0 and <= 16384 ? Display.Height : null,
                    Display.PixelRatio is > 0 and <= 8 ? Display.PixelRatio : null)
        };
    }

    private static string? Truncate(string? value, int length)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? null
            : trimmed.Length <= length ? trimmed : trimmed[..length];
    }

    /// <summary>
    /// The Direct Play capability profile of this client for one container, in the
    /// compatibility model #399 shares with the post-download optimizer. Only inferred or
    /// confirmed codecs are part of it.
    /// </summary>
    public PlaybackClientProfile ToCompatibilityProfile()
    {
        var supports = new List<ContainerSupport>();
        foreach (var container in Containers)
        {
            var family = PlaybackContainerNames.Parse(container.Container);
            if (family == MediaContainerFamily.Unknown || !container.Support.IsUsable())
            {
                continue;
            }

            supports.Add(new ContainerSupport(
                family,
                [
                    .. (container.Video ?? [])
                        .Where(x => x.Support.IsUsable())
                        .Select(x => new VideoCodecSupport(
                            x.Codec,
                            PixelFormatsFor(x.Codec, x.BitDepths),
                            x.CodecTags is { Count: > 0 } tags
                                ? new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase)
                                : null))
                ],
                new HashSet<string>(
                    (container.Audio ?? []).Where(x => x.Support.IsUsable()).Select(x => x.Codec),
                    StringComparer.OrdinalIgnoreCase)));
        }

        return new PlaybackClientProfile($"client-{Client.Kind}", Client.Name ?? Client.Kind, supports);
    }

    // Reported bit depths become the 4:2:0 pixel formats of those depths; browsers and native
    // hardware decoders rarely decode 4:2:2/4:4:4, so those stay out. Without a report H.264
    // is assumed 8-bit only (10-bit "Hi10P" anime does not decode in browsers) and the newer
    // codecs 8- and 10-bit, the depths their decoders ship with.
    private static IReadOnlySet<string> PixelFormatsFor(string codec, IReadOnlyList<int>? bitDepths)
    {
        if (bitDepths is not { Count: > 0 })
        {
            bitDepths = codec is "h264" or "mpeg4" or "vp8" or "theora" ? [8] : [8, 10];
        }

        var formats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var depth in bitDepths)
        {
            switch (depth)
            {
                case 8:
                    formats.Add("yuv420p");
                    formats.Add("yuvj420p");
                    formats.Add("nv12");
                    break;
                case 10:
                    formats.Add("yuv420p10le");
                    formats.Add("p010le");
                    break;
                case 12:
                    formats.Add("yuv420p12le");
                    break;
            }
        }

        return formats;
    }

    /// <summary>
    /// A capability document inferred from one of the shared browser profiles, used when a
    /// client sent none (older clients, first paint). Every claim is only Inferred.
    /// </summary>
    public static ClientPlaybackCapabilities FromProfile(
        PlaybackClientProfile profile,
        string clientKind,
        bool hls = false)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new ClientPlaybackCapabilities(
            CurrentSchemaVersion,
            new ClientIdentity(ClientKinds.Normalize(clientKind), profile.Name),
            [
                .. profile.Containers.Select(container => new ClientContainerCapability(
                    PlaybackContainerNames.Name(container.Container),
                    PlaybackCapabilitySupport.Inferred,
                    [
                        .. container.Video.Select(video => new ClientVideoCodecCapability(
                            video.Codec,
                            PlaybackCapabilitySupport.Inferred,
                            BitDepthsFor(video.PixelFormats),
                            CodecTags: video.CodecTags?.ToArray()))
                    ],
                    [
                        .. container.Audio.Select(audio => new ClientAudioCodecCapability(
                            audio,
                            PlaybackCapabilitySupport.Inferred))
                    ]))
            ],
            new ClientHdrCapabilities(),
            new ClientDeliveryCapabilities(
                ProgressiveMp4: PlaybackCapabilitySupport.Inferred,
                Hls: hls ? PlaybackCapabilitySupport.Inferred : PlaybackCapabilitySupport.Unknown),
            new ClientSubtitleCapabilities(Text: PlaybackCapabilitySupport.Inferred),
            new ClientPlatformFeatures(),
            null,
            Inferred: true);
    }

    private static int[]? BitDepthsFor(IReadOnlySet<string>? pixelFormats) =>
        pixelFormats is null
            ? null
            : [
                .. pixelFormats
                    .Select(format => format.Contains("10", StringComparison.Ordinal) ? 10 : 8)
                    .Distinct()
                    .Order()
            ];

    /// <summary>
    /// Picks the shared browser profile for a request without a capability document. The user
    /// agent only chooses which inferred baseline to start from; it never overrides a document
    /// the client actually sent.
    /// </summary>
    public static ClientPlaybackCapabilities InferFromUserAgent(string? userAgent, string clientKind)
    {
        var agent = userAgent ?? "";
        var isChromium = agent.Contains("Chrome/", StringComparison.Ordinal) ||
                         agent.Contains("Chromium/", StringComparison.Ordinal) ||
                         agent.Contains("Edg/", StringComparison.Ordinal);
        var isFirefox = agent.Contains("Firefox/", StringComparison.Ordinal);
        var isWebKit = !isChromium && !isFirefox &&
                       agent.Contains("AppleWebKit/", StringComparison.Ordinal) &&
                       agent.Contains("Safari/", StringComparison.Ordinal);

        var profile = isWebKit
            ? PlaybackClientProfiles.AppleWebKit
            : isChromium
                ? PlaybackClientProfiles.Chromium
                : isFirefox
                    ? PlaybackClientProfiles.Firefox
                    : PlaybackClientProfiles.BrowserBaseline;

        return FromProfile(profile, clientKind, hls: isWebKit);
    }
}

public static class ClientKinds
{
    public const string Web = "web";
    public const string Pwa = "pwa";
    public const string Android = "android";
    public const string AndroidTv = "android_tv";
    public const string Other = "other";

    public static string Normalize(string? kind) =>
        kind?.Trim().ToLowerInvariant() switch
        {
            Web => Web,
            Pwa => Pwa,
            Android => Android,
            AndroidTv or "androidtv" => AndroidTv,
            _ => Other
        };
}

/// <summary>Contract names of the shared container families.</summary>
public static class PlaybackContainerNames
{
    public static string Name(MediaContainerFamily family) =>
        family switch
        {
            MediaContainerFamily.Mp4 => "mp4",
            MediaContainerFamily.WebM => "webm",
            MediaContainerFamily.Matroska => "matroska",
            MediaContainerFamily.Ogg => "ogg",
            MediaContainerFamily.MpegTs => "mpegts",
            MediaContainerFamily.Avi => "avi",
            _ => "unknown"
        };

    public static MediaContainerFamily Parse(string? name) =>
        name?.Trim().ToLowerInvariant() switch
        {
            "mp4" or "mov" or "m4v" or "fmp4" => MediaContainerFamily.Mp4,
            "webm" => MediaContainerFamily.WebM,
            "matroska" or "mkv" => MediaContainerFamily.Matroska,
            "ogg" => MediaContainerFamily.Ogg,
            "mpegts" or "ts" => MediaContainerFamily.MpegTs,
            "avi" => MediaContainerFamily.Avi,
            _ => MediaContainerFamily.Unknown
        };
}

/// <summary>Maps client and ffprobe codec spellings onto ffprobe codec names.</summary>
public static class PlaybackCodecNames
{
    public static string NormalizeVideo(string codec) =>
        codec.Trim().ToLowerInvariant() switch
        {
            "avc" or "avc1" or "h.264" => "h264",
            "h265" or "hvc1" or "hev1" or "h.265" => "hevc",
            "av01" => "av1",
            "vp09" => "vp9",
            var other => other
        };

    public static string NormalizeAudio(string codec) =>
        codec.Trim().ToLowerInvariant() switch
        {
            "mp4a" or "aac_latm" => "aac",
            "ac-3" => "ac3",
            "ec-3" or "e-ac-3" => "eac3",
            "dts-hd" or "dca" => "dts",
            "mlp" => "truehd",
            var other => other
        };

    public static bool SameVideo(string left, string right) =>
        string.Equals(NormalizeVideo(left), NormalizeVideo(right), StringComparison.Ordinal);

    public static bool SameAudio(string left, string right) =>
        string.Equals(NormalizeAudio(left), NormalizeAudio(right), StringComparison.Ordinal);
}
