using System.Globalization;
using Jularr.Web.Features.Library;
using Jularr.Web.Features.Media.Compatibility;

namespace Jularr.Web.Features.Media.Optimization;

// Compares the ffprobe view of a remuxed file with its source. Any difference in streams, codec
// parameters, HDR signalling, languages, track names, dispositions, chapters or duration rejects
// the output; the source is then kept untouched.
public static class MediaRemuxVerifier
{
    private const double MinimumDurationToleranceSeconds = 1.0;
    private const double DurationToleranceRatio = 0.005;
    private const double ChapterToleranceSeconds = 0.5;
    private const double MinimumSizeRatio = 0.9;
    private const double FrameRateTolerance = 0.05;

    // Dispositions that change how a track is presented or chosen. "default" is compared per
    // stream type below because MP4 expresses it as the enabled track.
    private static readonly string[] SemanticDispositions =
    [
        "forced", "hearing_impaired", "visual_impaired", "comment", "dub", "original",
        "karaoke", "lyrics", "captions", "descriptions", "metadata", "dependent", "still_image"
    ];

    // ISO 639-2 bibliographic and terminology codes name the same language; containers differ in
    // which one they store.
    private static readonly Dictionary<string, string> LanguageAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["deu"] = "ger", ["fra"] = "fre", ["zho"] = "chi", ["ces"] = "cze", ["nld"] = "dut",
        ["ell"] = "gre", ["fas"] = "per", ["ron"] = "rum", ["slk"] = "slo", ["cym"] = "wel",
        ["hye"] = "arm", ["eus"] = "baq", ["mya"] = "bur", ["kat"] = "geo", ["isl"] = "ice",
        ["mkd"] = "mac", ["mri"] = "mao", ["msa"] = "may", ["sqi"] = "alb", ["bod"] = "tib"
    };

    public static IReadOnlyList<string> Verify(
        MediaProbeDetail source,
        MediaProbeDetail output,
        MediaContainerFamily expectedContainer,
        string outputPath,
        long sourceSizeBytes,
        long outputSizeBytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);

        var problems = new List<string>();

        if (MediaContainers.FromProbe(output.FormatName, outputPath) != expectedContainer)
        {
            problems.Add($"Output container is {output.FormatName ?? "unknown"}, expected {expectedContainer}.");
        }

        if (outputSizeBytes <= 0 || outputSizeBytes < sourceSizeBytes * MinimumSizeRatio)
        {
            problems.Add($"Output is {outputSizeBytes} bytes for a {sourceSizeBytes}-byte source.");
        }

        VerifyStreams(source, output, problems);
        VerifyDefaults(source, output, problems);
        VerifyChapters(source, output, problems);

        if (source.DurationSeconds is { } sourceDuration)
        {
            var tolerance = Math.Max(MinimumDurationToleranceSeconds, sourceDuration * DurationToleranceRatio);
            if (output.DurationSeconds is not { } outputDuration ||
                Math.Abs(outputDuration - sourceDuration) > tolerance)
            {
                problems.Add($"Duration changed from {sourceDuration:0.###}s to {output.DurationSeconds?.ToString("0.###") ?? "unknown"}s.");
            }
        }

        if (source.Title is { } title && !string.Equals(title, output.Title, StringComparison.Ordinal))
        {
            problems.Add("The file title was not preserved.");
        }

        return problems;
    }

    private static void VerifyStreams(MediaProbeDetail source, MediaProbeDetail output, List<string> problems)
    {
        if (output.Streams.Count < source.Streams.Count)
        {
            problems.Add($"Output has {output.Streams.Count} streams; the source has {source.Streams.Count}.");
            return;
        }

        // The MP4 muxer stores chapters as an extra QuickTime text track after the mapped streams.
        foreach (var extra in output.Streams.Skip(source.Streams.Count))
        {
            var isChapterTrack = extra.Type == MediaProbeStreamType.Data &&
                                 string.Equals(extra.CodecTag, "text", StringComparison.OrdinalIgnoreCase) &&
                                 source.Chapters.Count > 0;
            if (!isChapterTrack)
            {
                problems.Add($"Output has an unexpected {extra.Type} stream #{extra.Index}.");
            }
        }

        for (var position = 0; position < source.Streams.Count; position++)
        {
            var expected = source.Streams[position];
            var actual = output.Streams[position];
            var label = $"Stream #{expected.Index}";

            if (expected.Type != actual.Type ||
                !string.Equals(expected.Codec, actual.Codec, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{label} changed from {expected.Type} {expected.Codec} to {actual.Type} {actual.Codec}.");
                continue;
            }

            switch (expected.Type)
            {
                case MediaProbeStreamType.Video:
                    Same(problems, label, "resolution", $"{expected.Width}x{expected.Height}", $"{actual.Width}x{actual.Height}");
                    SameWhenKnown(problems, label, "profile", expected.Profile, actual.Profile);
                    SameWhenKnown(problems, label, "level", expected.Level, actual.Level);
                    SameWhenKnown(problems, label, "pixel format", expected.PixelFormat, actual.PixelFormat);
                    SameWhenKnown(problems, label, "bit depth", expected.BitDepth, actual.BitDepth);
                    if (ParseRate(expected.FrameRate) is { } expectedRate &&
                        !(ParseRate(actual.FrameRate) is { } actualRate && Math.Abs(expectedRate - actualRate) <= FrameRateTolerance))
                    {
                        problems.Add($"{label} frame rate changed from {expected.FrameRate} to {actual.FrameRate ?? "unknown"}.");
                    }

                    SameWhenKnown(problems, label, "color primaries", expected.ColorPrimaries, actual.ColorPrimaries);
                    SameWhenKnown(problems, label, "color transfer", expected.ColorTransfer, actual.ColorTransfer);
                    SameWhenKnown(problems, label, "color space", expected.ColorSpace, actual.ColorSpace);
                    SameWhenKnown(problems, label, "dynamic range", expected.DynamicRange, actual.DynamicRange);
                    SameWhenKnown(problems, label, "frame count", expected.FrameCount, actual.FrameCount);
                    foreach (var sideData in expected.SideDataTypes.Where(type => !actual.SideDataTypes.Contains(type, StringComparer.OrdinalIgnoreCase)))
                    {
                        problems.Add($"{label} lost its {sideData}.");
                    }

                    break;

                case MediaProbeStreamType.Audio:
                    SameWhenKnown(problems, label, "profile", expected.Profile, actual.Profile);
                    SameWhenKnown(problems, label, "channels", expected.Channels, actual.Channels);
                    SameWhenKnown(problems, label, "channel layout", expected.ChannelLayout, actual.ChannelLayout);
                    SameWhenKnown(problems, label, "sample rate", expected.SampleRate, actual.SampleRate);
                    break;
            }

            if (expected.Language is { } language &&
                !string.Equals(language, "und", StringComparison.OrdinalIgnoreCase) &&
                !SameLanguage(language, actual.Language))
            {
                problems.Add($"{label} language changed from {language} to {actual.Language ?? "none"}.");
            }

            if (expected.Title is { } title &&
                !string.Equals(title, actual.DisplayTitle, StringComparison.Ordinal))
            {
                problems.Add($"{label} lost its track name \"{title}\".");
            }

            foreach (var disposition in SemanticDispositions.Where(flag =>
                         expected.Dispositions.Contains(flag) && !actual.Dispositions.Contains(flag)))
            {
                problems.Add($"{label} lost its {disposition} flag.");
            }
        }
    }

    // Video and audio: the stream a player starts with must stay the same. Subtitles: the set of
    // default-flagged tracks must match exactly, because a newly enabled subtitle would show.
    private static void VerifyDefaults(MediaProbeDetail source, MediaProbeDetail output, List<string> problems)
    {
        foreach (var type in new[] { MediaProbeStreamType.Video, MediaProbeStreamType.Audio })
        {
            var expected = EffectiveDefault(source, type);
            var actual = EffectiveDefault(output, type);
            if (expected != actual)
            {
                problems.Add($"The default {type.ToString().ToLowerInvariant()} stream changed from #{expected} to #{actual}.");
            }
        }

        var expectedSubtitles = FlaggedDefaults(source, source.Streams.Count);
        var actualSubtitles = FlaggedDefaults(output, source.Streams.Count);
        if (!expectedSubtitles.SequenceEqual(actualSubtitles))
        {
            problems.Add("The default subtitle selection changed.");
        }
    }

    private static int? EffectiveDefault(MediaProbeDetail detail, MediaProbeStreamType type)
    {
        var streams = detail.OfType(type).Where(x => !x.IsAttachedPicture).ToArray();
        return (streams.FirstOrDefault(x => x.IsDefault) ?? streams.FirstOrDefault())?.Index;
    }

    private static int[] FlaggedDefaults(MediaProbeDetail detail, int mappedStreams) =>
        [
            .. detail.Streams
                .Take(mappedStreams)
                .Where(x => x.Type == MediaProbeStreamType.Subtitle && x.IsDefault)
                .Select(x => x.Index)
        ];

    private static void VerifyChapters(MediaProbeDetail source, MediaProbeDetail output, List<string> problems)
    {
        if (source.Chapters.Count != output.Chapters.Count)
        {
            problems.Add($"Output has {output.Chapters.Count} chapters; the source has {source.Chapters.Count}.");
            return;
        }

        for (var index = 0; index < source.Chapters.Count; index++)
        {
            var expected = source.Chapters[index];
            var actual = output.Chapters[index];
            if (Math.Abs(expected.StartSeconds - actual.StartSeconds) > ChapterToleranceSeconds)
            {
                problems.Add($"Chapter {index + 1} moved from {expected.StartSeconds:0.###}s to {actual.StartSeconds:0.###}s.");
            }

            if (expected.Title is { } title && !string.Equals(title, actual.Title, StringComparison.Ordinal))
            {
                problems.Add($"Chapter {index + 1} lost its title \"{title}\".");
            }
        }
    }

    // ffprobe prints rates as "num/den"; containers derive the average from different timestamps,
    // so equal copies can differ in the last digits (24000/1001 vs 2997/125).
    private static double? ParseRate(string? rate)
    {
        if (rate is null)
        {
            return null;
        }

        var parts = rate.Split('/');
        return parts.Length == 2 &&
               double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) &&
               double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) &&
               denominator > 0 &&
               numerator > 0
            ? numerator / denominator
            : null;
    }

    private static bool SameLanguage(string expected, string? actual) =>
        actual is not null &&
        string.Equals(Canonical(expected), Canonical(actual), StringComparison.OrdinalIgnoreCase);

    private static string Canonical(string language) =>
        LanguageAliases.TryGetValue(language, out var alias) ? alias : language;

    private static void Same(List<string> problems, string label, string property, string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            problems.Add($"{label} {property} changed from {expected} to {actual}.");
        }
    }

    private static void SameWhenKnown(List<string> problems, string label, string property, string? expected, string? actual)
    {
        if (expected is not null && !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"{label} {property} changed from {expected} to {actual ?? "unknown"}.");
        }
    }

    private static void SameWhenKnown<T>(List<string> problems, string label, string property, T? expected, T? actual)
        where T : struct, IEquatable<T>
    {
        if (expected is { } value && !(actual is { } other && value.Equals(other)))
        {
            problems.Add($"{label} {property} changed from {value} to {(actual is null ? "unknown" : actual.ToString())}.");
        }
    }
}
