using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AniLingo.Web.Features.MediaSegments;

public enum MediaSegmentKind
{
    Intro = 1,
    Recap = 2,
    Outro = 3,
    Preview = 4,
    Credits = 5
}

// Ordered by authority: a lower value supersedes a higher one for the same kind.
public enum MediaSegmentSource
{
    Manual = 1,
    Imported = 2,
    Provider = 3,
    Detector = 4
}

// One canonical marker per episode, kind and source. Clients never derive
// their own boundaries; they consume the resolved set from the server.
public sealed class EpisodeMediaSegment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EpisodeId { get; set; }
    public MediaSegmentKind Kind { get; set; }
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public MediaSegmentSource Source { get; set; }
    public string Method { get; set; } = "";
    public string Version { get; set; } = "";
    public double Confidence { get; set; }

    // Media identity the automatic result was computed against; lets a detector
    // skip re-analysis while source identity and detector version are unchanged.
    public string? MediaIdentity { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class MediaSegmentOptions
{
    public const string SectionName = "MediaSegments";

    // A skip action is offered only when the resolved marker reaches this confidence.
    public double SkipConfidenceThreshold { get; set; } = 0.8;
}

public sealed record ResolvedMediaSegment(
    MediaSegmentKind Kind,
    long StartMs,
    long EndMs,
    MediaSegmentSource Source,
    string Method,
    string Version,
    double Confidence,
    bool CanSkip);

public sealed record EpisodeSegmentDescriptor(
    double SkipConfidenceThreshold,
    IReadOnlyList<ResolvedMediaSegment> Segments);

public sealed record EpisodePlayerNavigation(
    EpisodeSegmentDescriptor Segments,
    TrickplayDescriptor Trickplay);

public static class MediaSegmentPolicy
{
    public const long MinimumLengthMs = 1000;
    public const string ManualMethod = "manual";
    public const string SidecarMethod = "sidecar";
    public const string SidecarVersion = "1";

    public static readonly IReadOnlyList<MediaSegmentKind> Kinds =
    [
        MediaSegmentKind.Intro,
        MediaSegmentKind.Recap,
        MediaSegmentKind.Outro,
        MediaSegmentKind.Preview,
        MediaSegmentKind.Credits
    ];

    public static int Precedence(MediaSegmentSource source) => (int)source;

    // Per kind the most authoritative source wins regardless of confidence:
    // an explicit correction must always replace an automatic result.
    public static IReadOnlyList<ResolvedMediaSegment> Resolve(
        IEnumerable<EpisodeMediaSegment> rows,
        double skipConfidenceThreshold) =>
        rows
            .Where(x => IsValidRange(x.StartMs, x.EndMs))
            .GroupBy(x => x.Kind)
            .Select(group => group
                .OrderBy(x => Precedence(x.Source))
                .ThenByDescending(x => x.UpdatedAt)
                .First())
            .OrderBy(x => x.StartMs)
            .ThenBy(x => x.Kind)
            .Select(x => new ResolvedMediaSegment(
                x.Kind,
                x.StartMs,
                x.EndMs,
                x.Source,
                x.Method,
                x.Version,
                x.Confidence,
                x.Confidence >= skipConfidenceThreshold))
            .ToArray();

    public static bool IsValidRange(long startMs, long endMs) =>
        startMs >= 0 && endMs - startMs >= MinimumLengthMs;

    public static double ClampConfidence(double confidence) =>
        double.IsFinite(confidence) ? Math.Clamp(confidence, 0, 1) : 0;

    public static string KindName(MediaSegmentKind kind) => kind switch
    {
        MediaSegmentKind.Intro => "intro",
        MediaSegmentKind.Recap => "recap",
        MediaSegmentKind.Outro => "outro",
        MediaSegmentKind.Preview => "preview",
        MediaSegmentKind.Credits => "credits",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static string KindLabel(MediaSegmentKind kind) => kind switch
    {
        MediaSegmentKind.Intro => "Intro / OP",
        MediaSegmentKind.Recap => "Recap",
        MediaSegmentKind.Outro => "Outro / ED",
        MediaSegmentKind.Preview => "Preview",
        MediaSegmentKind.Credits => "Credits",
        _ => kind.ToString()
    };

    public static bool TryParseKind(string? value, out MediaSegmentKind kind)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "intro" or "op" or "opening":
                kind = MediaSegmentKind.Intro;
                return true;
            case "recap":
                kind = MediaSegmentKind.Recap;
                return true;
            case "outro" or "ed" or "ending":
                kind = MediaSegmentKind.Outro;
                return true;
            case "preview" or "next":
                kind = MediaSegmentKind.Preview;
                return true;
            case "credits" or "other":
                kind = MediaSegmentKind.Credits;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    public static string SourceName(MediaSegmentSource source) => source switch
    {
        MediaSegmentSource.Manual => "manual",
        MediaSegmentSource.Imported => "imported",
        MediaSegmentSource.Provider => "provider",
        MediaSegmentSource.Detector => "detector",
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };
}

public static class MediaTimecode
{
    // 1:30, 1:30.5, 0:01:30.250 or plain seconds (90, 90.5).
    public static bool TryParse(string? text, out long milliseconds)
    {
        milliseconds = 0;
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var parts = value.Split(':');
        if (parts.Length > 3)
        {
            return false;
        }

        double total = 0;
        for (var index = 0; index < parts.Length; index++)
        {
            var isLast = index == parts.Length - 1;
            if (!double.TryParse(
                    parts[index],
                    NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
                    CultureInfo.InvariantCulture,
                    out var part) ||
                part < 0 ||
                (!isLast && part != Math.Floor(part)))
            {
                return false;
            }

            total = total * 60 + part;
        }

        if (!double.IsFinite(total) || total > TimeSpan.MaxValue.TotalSeconds / 2)
        {
            return false;
        }

        milliseconds = (long)Math.Round(total * 1000);
        return true;
    }

    public static string Format(long milliseconds)
    {
        var clamped = Math.Max(0, milliseconds);
        var totalSeconds = clamped / 1000;
        var fraction = clamped % 1000;
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;

        var core = hours > 0
            ? $"{hours}:{minutes:00}:{seconds:00}"
            : $"{minutes}:{seconds:00}";

        return fraction == 0
            ? core
            : $"{core}.{fraction:000}";
    }
}

public static class MediaIdentity
{
    // Canonical identity of one inventoried media file: the media row plus the
    // size and last-write stamp recorded by the library scan. Any content change
    // observed by reconciliation yields a new identity and invalidates generated analysis.
    public static string Compute(
        Guid mediaFileId,
        long sizeBytes,
        DateTime lastWriteTimeUtc)
    {
        var material = string.Create(
            CultureInfo.InvariantCulture,
            $"{mediaFileId:D}\n{sizeBytes}\n{lastWriteTimeUtc.Ticks}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexStringLower(hash)[..32];
    }
}
