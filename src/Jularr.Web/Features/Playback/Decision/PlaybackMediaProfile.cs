using Jularr.Web.Features.Library;
using Jularr.Web.Features.Media.Compatibility;

namespace Jularr.Web.Features.Playback.Decision;

public sealed record PlaybackVideoStreamProfile(
    string? Codec,
    string? Profile,
    int? Width,
    int? Height,
    string? PixelFormat,
    int? BitDepth,
    string? DynamicRange,
    string? CodecTag = null)
{
    public bool IsHdr =>
        DynamicRange is not null &&
        !string.Equals(DynamicRange, "SDR", StringComparison.OrdinalIgnoreCase);
}

public sealed record PlaybackAudioStreamProfile(
    int StreamIndex,
    string? Codec,
    int? Channels,
    string? Language,
    string? Title,
    bool IsDefault);

public sealed record PlaybackSubtitleStreamProfile(
    int StreamIndex,
    string? Codec,
    string? Language,
    bool IsText,
    bool IsForced,
    bool IsDefault);

/// <summary>
/// The media side of a playback decision, read from the canonical media inventory (the #399
/// analysis). The overall bitrate is derived from size and duration: it is what a client has
/// to download for Direct Play, which is the figure a bandwidth decision needs.
/// </summary>
public sealed record PlaybackMediaProfile(
    MediaContainerFamily Container,
    double? DurationSeconds,
    long? SizeBytes,
    PlaybackVideoStreamProfile? Video,
    IReadOnlyList<PlaybackAudioStreamProfile> Audio,
    IReadOnlyList<PlaybackSubtitleStreamProfile> Subtitles)
{
    public int? OverallBitrateKbps =>
        SizeBytes is > 0 && DurationSeconds is > 1 && double.IsFinite(DurationSeconds.Value)
            ? (int)Math.Ceiling(SizeBytes.Value * 8d / DurationSeconds.Value / 1000d)
            : null;

    public PlaybackAudioStreamProfile? DefaultAudio =>
        Audio.FirstOrDefault(x => x.IsDefault) ?? Audio.FirstOrDefault();

    public PlaybackAudioStreamProfile? AudioStream(int streamIndex) =>
        Audio.FirstOrDefault(x => x.StreamIndex == streamIndex);

    public PlaybackSubtitleStreamProfile? SubtitleStream(int streamIndex) =>
        Subtitles.FirstOrDefault(x => x.StreamIndex == streamIndex);

    /// <summary>What a client receives from this file untouched with the given audio stream.</summary>
    public MediaPlaybackCharacteristics Characteristics(
        PlaybackAudioStreamProfile? audio,
        MediaContainerFamily? container = null) =>
        new(
            container ?? Container,
            Video?.Codec,
            Video?.CodecTag,
            Video?.PixelFormat,
            audio?.Codec);

    public static PlaybackMediaProfile From(
        string path,
        long? sizeBytes,
        MediaTechnicalInfo technical)
    {
        ArgumentNullException.ThrowIfNull(technical);
        var video = technical.Video is { } source
            ? new PlaybackVideoStreamProfile(
                source.Codec,
                source.Profile,
                source.Width,
                source.Height,
                source.PixelFormat,
                source.BitDepth,
                source.DynamicRange)
            : null;

        return new PlaybackMediaProfile(
            MediaContainers.FromProbe(technical.Container, path),
            technical.DurationSeconds,
            sizeBytes,
            video,
            [
                .. technical.AudioStreams.Select(stream => new PlaybackAudioStreamProfile(
                    stream.Index,
                    stream.Codec,
                    stream.Channels,
                    stream.Language,
                    stream.Title,
                    stream.IsDefault))
            ],
            [
                .. technical.SubtitleStreams.Select(stream => new PlaybackSubtitleStreamProfile(
                    stream.Index,
                    stream.Codec,
                    stream.Language,
                    stream.IsText,
                    stream.IsForced,
                    stream.IsDefault))
            ]);
    }
}
