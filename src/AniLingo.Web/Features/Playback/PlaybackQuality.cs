using System.Globalization;

namespace AniLingo.Web.Features.Playback;

/// <summary>
/// Optional remote-bandwidth cap. It is a device/network fact owned by the
/// playing device, never a profile preference. <see cref="Auto"/> keeps the
/// original stream.
/// </summary>
public enum PlaybackQualityCap
{
    Auto,
    P1080,
    P720,
    LowBandwidth
}

/// <summary>
/// Canonical quality-cap rules shared by the web player, the Razor media
/// handler and the native client API. A cap only limits a stream the server
/// produces anyway; it never turns direct play or a video-copy remux into a
/// transcode unless the canonical media analysis proves the source exceeds it.
/// </summary>
public static class PlaybackQuality
{
    public static IReadOnlyList<string> Names { get; } = ["auto", "1080p", "720p", "low"];

    public static string Name(PlaybackQualityCap cap) =>
        cap switch
        {
            PlaybackQualityCap.P1080 => "1080p",
            PlaybackQualityCap.P720 => "720p",
            PlaybackQualityCap.LowBandwidth => "low",
            _ => "auto"
        };

    /// <summary>Parses a cap name; null or empty means <see cref="PlaybackQualityCap.Auto"/>.</summary>
    public static bool TryParse(string? value, out PlaybackQualityCap cap)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "auto":
                cap = PlaybackQualityCap.Auto;
                return true;
            case "1080p":
                cap = PlaybackQualityCap.P1080;
                return true;
            case "720p":
                cap = PlaybackQualityCap.P720;
                return true;
            case "low":
                cap = PlaybackQualityCap.LowBandwidth;
                return true;
            default:
                cap = PlaybackQualityCap.Auto;
                return false;
        }
    }

    public static int? MaxHeight(PlaybackQualityCap cap) =>
        cap switch
        {
            PlaybackQualityCap.P1080 => 1080,
            PlaybackQualityCap.P720 => 720,
            PlaybackQualityCap.LowBandwidth => 480,
            _ => null
        };

    /// <summary>
    /// True only when the canonical media analysis proves the source is taller
    /// than the cap. An unknown source height never justifies a transcode.
    /// </summary>
    public static bool RequiresTranscode(PlaybackQualityCap cap, int? sourceHeight) =>
        MaxHeight(cap) is { } maxHeight &&
        sourceHeight is { } height &&
        height > maxHeight;

    /// <summary>
    /// ffmpeg output arguments for an H.264 encode that is already required.
    /// The scale expression never upscales; bitrate limits bound the stream.
    /// </summary>
    public static IReadOnlyList<string> EncodeArguments(PlaybackQualityCap cap)
    {
        var maxHeight = MaxHeight(cap);
        if (maxHeight is null)
        {
            return [];
        }

        var (maxRate, bufferSize) = cap switch
        {
            PlaybackQualityCap.P1080 => ("8000k", "16000k"),
            PlaybackQualityCap.P720 => ("4000k", "8000k"),
            _ => ("1500k", "3000k")
        };

        return
        [
            "-vf", $"scale=-2:min(ih\\,{maxHeight.Value.ToString(CultureInfo.InvariantCulture)})",
            "-maxrate", maxRate,
            "-bufsize", bufferSize
        ];
    }

    public static string AudioBitrate(PlaybackQualityCap cap) =>
        cap == PlaybackQualityCap.LowBandwidth ? "128k" : "192k";
}

/// <summary>Stable canonical track IDs (<c>stream:{ffprobe index}</c>) shared by every client.</summary>
public static class PlaybackTrackIds
{
    private const string Prefix = "stream:";

    public static string Format(int streamIndex) =>
        $"{Prefix}{streamIndex.ToString(CultureInfo.InvariantCulture)}";

    public static bool TryParse(string? trackId, out int streamIndex)
    {
        streamIndex = -1;
        return trackId is not null &&
               trackId.StartsWith(Prefix, StringComparison.Ordinal) &&
               int.TryParse(
                   trackId.AsSpan(Prefix.Length),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out streamIndex);
    }
}

/// <summary>
/// Language tags as stored in media containers (ISO 639-1 or 639-2) and in
/// profile preferences. Comparison is alias-aware so a stored preference of
/// <c>ja</c> matches a <c>jpn</c> audio stream.
/// </summary>
public static class PlaybackLanguages
{
    public const string SubtitlesOff = "off";

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["jpn"] = "ja",
        ["eng"] = "en",
        ["ger"] = "de",
        ["deu"] = "de",
        ["fre"] = "fr",
        ["fra"] = "fr",
        ["spa"] = "es",
        ["ita"] = "it",
        ["por"] = "pt",
        ["chi"] = "zh",
        ["zho"] = "zh",
        ["kor"] = "ko",
        ["rus"] = "ru",
        ["ara"] = "ar",
        ["dut"] = "nl",
        ["nld"] = "nl",
        ["pol"] = "pl",
        ["swe"] = "sv",
        ["tur"] = "tr"
    };

    /// <summary>
    /// Normalizes a language tag to its canonical lowercase form. Returns null
    /// for empty or undetermined values. <see cref="SubtitlesOff"/> is preserved.
    /// </summary>
    public static string? Normalize(string? value)
    {
        var trimmed = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(trimmed) || trimmed == "und")
        {
            return null;
        }

        if (trimmed == SubtitlesOff)
        {
            return SubtitlesOff;
        }

        var primary = trimmed.Split('-', 2)[0];
        if (primary.Length is < 2 or > 3 || primary.Any(character => character is < 'a' or > 'z'))
        {
            return null;
        }

        return Aliases.TryGetValue(primary, out var alias) ? alias : primary;
    }

    public static bool Matches(string? left, string? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        return a is not null && b is not null && string.Equals(a, b, StringComparison.Ordinal);
    }
}

public enum PlaybackSubtitleMode
{
    Off,
    Learning,
    Embedded
}

public sealed record PlaybackSubtitleSelection(
    PlaybackSubtitleMode Mode,
    int? StreamIndex = null)
{
    public static PlaybackSubtitleSelection Off { get; } = new(PlaybackSubtitleMode.Off);
    public static PlaybackSubtitleSelection Learning { get; } = new(PlaybackSubtitleMode.Learning);

    public string? TrackId =>
        StreamIndex is { } index ? PlaybackTrackIds.Format(index) : null;

    public string ModeName =>
        Mode switch
        {
            PlaybackSubtitleMode.Learning => "learning",
            PlaybackSubtitleMode.Embedded => "embedded",
            _ => "off"
        };
}

/// <summary>
/// Canonical default track resolution: the file default is the baseline; a
/// profile language preference overrides it when a matching track exists.
/// Every client consumes the resolved selection instead of re-deciding.
/// </summary>
public static class PlaybackTrackSelection
{
    public static PlaybackMediaTrack? DefaultAudio(IReadOnlyList<PlaybackMediaTrack>? tracks)
    {
        var audio = (tracks ?? []).Where(x => x.Kind == PlaybackTrackKind.Audio).ToArray();
        return audio.FirstOrDefault(x => x.IsDefault) ?? audio.FirstOrDefault();
    }

    public static PlaybackMediaTrack? ResolveAudio(
        IReadOnlyList<PlaybackMediaTrack>? tracks,
        string? preferredLanguage)
    {
        var preferred = (tracks ?? [])
            .Where(x => x.Kind == PlaybackTrackKind.Audio &&
                        PlaybackLanguages.Matches(x.Language, preferredLanguage))
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.StreamIndex)
            .FirstOrDefault();

        return preferred ?? DefaultAudio(tracks);
    }

    public static PlaybackSubtitleSelection ResolveSubtitle(
        IReadOnlyList<PlaybackMediaTrack>? tracks,
        bool hasLearningCues,
        string? preferredLanguage)
    {
        var preferred = PlaybackLanguages.Normalize(preferredLanguage);
        if (preferred == PlaybackLanguages.SubtitlesOff)
        {
            return PlaybackSubtitleSelection.Off;
        }

        var textTracks = (tracks ?? [])
            .Where(x => x.Kind == PlaybackTrackKind.Subtitle && x.IsText)
            .ToArray();

        if (preferred is not null)
        {
            if (hasLearningCues && PlaybackLanguages.Matches(preferred, "ja"))
            {
                return PlaybackSubtitleSelection.Learning;
            }

            var match = textTracks
                .Where(x => PlaybackLanguages.Matches(x.Language, preferred))
                .OrderBy(x => x.IsForced)
                .ThenByDescending(x => x.IsDefault)
                .ThenBy(x => x.StreamIndex)
                .FirstOrDefault();

            if (match is not null)
            {
                return new PlaybackSubtitleSelection(PlaybackSubtitleMode.Embedded, match.StreamIndex);
            }
        }

        if (hasLearningCues)
        {
            return PlaybackSubtitleSelection.Learning;
        }

        var fileDefault = textTracks.FirstOrDefault(x => x.IsDefault && !x.IsForced);
        return fileDefault is null
            ? PlaybackSubtitleSelection.Off
            : new PlaybackSubtitleSelection(PlaybackSubtitleMode.Embedded, fileDefault.StreamIndex);
    }
}
