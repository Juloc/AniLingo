using Jularr.Web.Features.Library;
using Jularr.Web.Features.Media.Compatibility;

namespace Jularr.Web.Features.Media.Optimization;

// Owner setting for the post-import step. Off by default until the preservation checks have
// proven themselves on real libraries.
public enum LosslessPlaybackOptimizationMode
{
    Off = 0,
    SafeOnly = 1
}

public enum MediaOptimizationOutcome
{
    // The file already plays directly wherever a lossless remux could make it play.
    AlreadyOptimal,
    Remux,
    // A remux would widen Direct Play but would lose or alter something, so the source stays.
    KeepOriginal
}

public enum MediaOptimizationReason
{
    AlreadyBrowserFriendly,
    NoCompatibilityGain,
    NoVideoStream,
    MultipleVideoStreams,
    VideoNotRemuxable,
    DolbyVisionNotVerifiable,
    AudioNotRemuxable,
    StyledSubtitlesRequireMatroska,
    SubtitleNotRemuxable,
    AttachmentsRequireMatroska,
    CoverArtNotPreserved,
    DataStreamNotPreserved,
    UnknownStream,
    RemuxWidensDirectPlay
}

public sealed record MediaOptimizationFinding(MediaOptimizationReason Reason, string Message);

public sealed record MediaRemuxPlan(
    MediaContainerFamily TargetContainer,
    string TargetExtension,
    // The primary video stream index to tag hvc1; WebKit only decodes HEVC with that sample entry.
    int? HevcStreamIndex,
    // Track names MP4 keeps only as the handler name.
    IReadOnlyDictionary<int, string> StreamTitles);

public sealed record MediaOptimizationDecision(
    MediaOptimizationOutcome Outcome,
    MediaContainerFamily SourceContainer,
    IReadOnlyList<PlaybackClientProfile> SourceDirectPlay,
    IReadOnlyList<PlaybackClientProfile> TargetDirectPlay,
    IReadOnlyList<MediaOptimizationFinding> Findings,
    MediaRemuxPlan? Plan)
{
    public string Summary
    {
        get
        {
            var reasons = string.Join(" ", Findings.Select(x => x.Message));
            return Outcome switch
            {
                MediaOptimizationOutcome.Remux => $"Remux to MP4 without re-encoding. {reasons}",
                MediaOptimizationOutcome.AlreadyOptimal => $"Kept unchanged. {reasons}",
                _ => $"Kept original. {reasons}"
            };
        }
    }
}

// Decides from the actual streams whether a lossless container remux improves Direct Play and
// preserves everything. It never plans a re-encode: every stream must be copied bit for bit, and
// anything MP4 cannot carry unchanged (styled subtitles, fonts, DTS/TrueHD, Dolby Vision whose
// configuration cannot be verified) keeps the original file.
public static class MediaOptimizationPlanner
{
    public const string TargetExtension = ".mp4";

    private static readonly HashSet<string> Mp4VideoCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "h264", "hevc", "av1", "vp9"
    };

    private static readonly HashSet<string> Mp4AudioCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "aac", "mp3", "ac3", "eac3", "alac", "flac", "opus"
    };

    private static readonly HashSet<string> StyledSubtitleCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "ass", "ssa"
    };

    public static MediaOptimizationDecision Decide(MediaProbeDetail source, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(source);

        var container = MediaContainers.FromProbe(source.FormatName, sourcePath);
        var video = source.PrimaryVideo;
        if (video is null)
        {
            return Keep(
                MediaOptimizationOutcome.KeepOriginal,
                container,
                [],
                [],
                new(MediaOptimizationReason.NoVideoStream, "No video stream was found."));
        }

        var audioCodec = source.DefaultAudio?.Codec;
        var sourceProfiles = MediaPlaybackCompatibility.DirectPlayProfiles(
            new(container, video.Codec, video.CodecTag, video.PixelFormat, audioCodec));

        if (container == MediaContainerFamily.Mp4)
        {
            return Keep(
                MediaOptimizationOutcome.AlreadyOptimal,
                container,
                sourceProfiles,
                sourceProfiles,
                sourceProfiles.Count > 0
                    ? new(MediaOptimizationReason.AlreadyBrowserFriendly, $"Already MP4; plays directly in {Names(sourceProfiles)}.")
                    : new(MediaOptimizationReason.NoCompatibilityGain, "Already MP4; only transcoding could widen Direct Play."));
        }

        var isHevc = string.Equals(video.Codec, "hevc", StringComparison.OrdinalIgnoreCase);
        var targetProfiles = MediaPlaybackCompatibility.DirectPlayProfiles(
            new(MediaContainerFamily.Mp4, video.Codec, isHevc ? "hvc1" : video.CodecTag, video.PixelFormat, audioCodec));

        var gained = targetProfiles.Where(profile => !sourceProfiles.Contains(profile)).ToArray();
        if (gained.Length == 0)
        {
            return sourceProfiles.Count > 0
                ? Keep(
                    MediaOptimizationOutcome.AlreadyOptimal,
                    container,
                    sourceProfiles,
                    targetProfiles,
                    new(MediaOptimizationReason.AlreadyBrowserFriendly, $"Already plays directly in {Names(sourceProfiles)}; MP4 would not add a browser."))
                : Keep(
                    MediaOptimizationOutcome.KeepOriginal,
                    container,
                    sourceProfiles,
                    targetProfiles,
                    new(MediaOptimizationReason.NoCompatibilityGain, $"An MP4 remux would not play directly in any browser either ({DescribeBlockers(video, audioCodec)})."));
        }

        var blockers = CollectPreservationBlockers(source, video);
        if (blockers.Count > 0)
        {
            return new(
                MediaOptimizationOutcome.KeepOriginal,
                container,
                sourceProfiles,
                targetProfiles,
                blockers,
                null);
        }

        var titles = source.Streams
            .Where(x => x.Type is MediaProbeStreamType.Audio or MediaProbeStreamType.Subtitle && x.Title is not null)
            .ToDictionary(x => x.Index, x => x.Title!);

        return new(
            MediaOptimizationOutcome.Remux,
            container,
            sourceProfiles,
            targetProfiles,
            [new(MediaOptimizationReason.RemuxWidensDirectPlay, $"MP4 plays directly in {Names(targetProfiles)}.")],
            new MediaRemuxPlan(
                MediaContainerFamily.Mp4,
                TargetExtension,
                isHevc ? video.Index : null,
                titles));
    }

    private static List<MediaOptimizationFinding> CollectPreservationBlockers(
        MediaProbeDetail source,
        MediaProbeStream primaryVideo)
    {
        var blockers = new List<MediaOptimizationFinding>();

        foreach (var stream in source.Streams)
        {
            switch (stream.Type)
            {
                case MediaProbeStreamType.Video when stream.IsAttachedPicture:
                    blockers.Add(new(MediaOptimizationReason.CoverArtNotPreserved, $"Stream #{stream.Index} is embedded cover art."));
                    break;

                case MediaProbeStreamType.Video when stream.Index != primaryVideo.Index:
                    blockers.Add(new(MediaOptimizationReason.MultipleVideoStreams, $"Stream #{stream.Index} is a second video stream."));
                    break;

                case MediaProbeStreamType.Video:
                    if (stream.Codec is null || !Mp4VideoCodecs.Contains(stream.Codec))
                    {
                        blockers.Add(new(MediaOptimizationReason.VideoNotRemuxable, $"{stream.Codec ?? "Unknown"} video cannot be copied into MP4."));
                    }

                    if (stream.DynamicRange == "Dolby Vision")
                    {
                        blockers.Add(new(MediaOptimizationReason.DolbyVisionNotVerifiable, "Dolby Vision metadata cannot be verified after a remux."));
                    }

                    break;

                case MediaProbeStreamType.Audio:
                    if (stream.Codec is null || !Mp4AudioCodecs.Contains(stream.Codec))
                    {
                        blockers.Add(new(MediaOptimizationReason.AudioNotRemuxable, $"Audio stream #{stream.Index} ({Describe(stream)}) cannot be copied into MP4 without conversion."));
                    }

                    break;

                case MediaProbeStreamType.Subtitle:
                    if (stream.Codec is not null && StyledSubtitleCodecs.Contains(stream.Codec))
                    {
                        blockers.Add(new(MediaOptimizationReason.StyledSubtitlesRequireMatroska, $"Subtitle stream #{stream.Index} ({Describe(stream)}) is ASS/SSA; its styling requires MKV."));
                    }
                    else if (!string.Equals(stream.Codec, "mov_text", StringComparison.OrdinalIgnoreCase))
                    {
                        blockers.Add(new(MediaOptimizationReason.SubtitleNotRemuxable, $"Subtitle stream #{stream.Index} ({Describe(stream)}) would have to be converted for MP4."));
                    }

                    break;

                case MediaProbeStreamType.Data:
                    blockers.Add(new(MediaOptimizationReason.DataStreamNotPreserved, $"Data stream #{stream.Index} ({stream.Codec ?? "unknown"}) cannot be preserved in MP4."));
                    break;

                case MediaProbeStreamType.Unknown:
                    blockers.Add(new(MediaOptimizationReason.UnknownStream, $"Stream #{stream.Index} has an unknown type."));
                    break;
            }
        }

        var attachments = source.OfType(MediaProbeStreamType.Attachment).ToArray();
        if (attachments.Length > 0)
        {
            var fonts = attachments.Count(IsFont);
            blockers.Add(new(
                MediaOptimizationReason.AttachmentsRequireMatroska,
                fonts == attachments.Length
                    ? $"{fonts} embedded font(s) must stay in MKV."
                    : $"{attachments.Length} attachment(s) ({fonts} font(s)) must stay in MKV."));
        }

        return blockers;
    }

    private static bool IsFont(MediaProbeStream attachment) =>
        attachment.MimeType?.Contains("font", StringComparison.OrdinalIgnoreCase) == true ||
        attachment.Codec is "ttf" or "otf" ||
        Path.GetExtension(attachment.FileName ?? "").ToLowerInvariant() is ".ttf" or ".otf" or ".ttc";

    private static string DescribeBlockers(MediaProbeStream video, string? audioCodec) =>
        $"{video.Codec ?? "unknown"} video ({video.PixelFormat ?? "unknown pixel format"})" +
        (audioCodec is null ? "" : $", {audioCodec} audio");

    private static string Describe(MediaProbeStream stream) =>
        stream.Language is { } language && !string.Equals(language, "und", StringComparison.OrdinalIgnoreCase)
            ? $"{stream.Codec ?? "unknown"}, {language}"
            : stream.Codec ?? "unknown";

    private static string Names(IEnumerable<PlaybackClientProfile> profiles) =>
        string.Join(", ", profiles.Select(x => x.Name));

    private static MediaOptimizationDecision Keep(
        MediaOptimizationOutcome outcome,
        MediaContainerFamily container,
        IReadOnlyList<PlaybackClientProfile> source,
        IReadOnlyList<PlaybackClientProfile> target,
        MediaOptimizationFinding finding) =>
        new(outcome, container, source, target, [finding], null);
}
