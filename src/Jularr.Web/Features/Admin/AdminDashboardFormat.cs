using System.Globalization;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Operations;
using Jularr.Web.Features.Playback.Decision;

namespace Jularr.Web.Features.Admin;

/// <summary>Turns the dashboard's raw figures into the short, localized values the page shows.</summary>
public static class AdminDashboardFormat
{
    /// <summary>Bytes per second as MB/s, or KB/s below a megabyte per second (decimal units, as the network is measured).</summary>
    public static string Rate(UiTextBundle ui, double bytesPerSecond)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var value = Math.Max(bytesPerSecond, 0);
        return value >= 1_000_000
            ? ui.Format("admin.dashboard.rate.mb", ("value", (value / 1_000_000).ToString("0.#", CultureInfo.InvariantCulture)))
            : ui.Format("admin.dashboard.rate.kb", ("value", Math.Round(value / 1000).ToString("0", CultureInfo.InvariantCulture)));
    }

    /// <summary>A span as "3 d 4 h", "5 h 12 min" or "8 min"; under a minute reads "1 min".</summary>
    public static string Duration(UiTextBundle ui, TimeSpan span)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (span >= TimeSpan.FromDays(1))
        {
            return ui.Format("admin.dashboard.duration.days", ("days", (int)span.TotalDays), ("hours", span.Hours));
        }

        if (span >= TimeSpan.FromHours(1))
        {
            return ui.Format("admin.dashboard.duration.hours", ("hours", (int)span.TotalHours), ("minutes", span.Minutes));
        }

        return ui.Format("admin.dashboard.duration.minutes", ("minutes", Math.Max(1, (int)span.TotalMinutes)));
    }

    /// <summary>The time left of an operation, or null when it has no estimate or the estimate has passed.</summary>
    public static string? Eta(UiTextBundle ui, OperationSnapshot operation, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation.Status == OperationStatus.Running && operation.EtaUtc is { } eta && eta > nowUtc
            ? Duration(ui, eta - nowUtc)
            : null;
    }

    /// <summary>The progress of an operation in whole percent, from its own figure or from its bytes.</summary>
    public static int? Progress(OperationSnapshot operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.ProgressPercent is { } percent)
        {
            return Math.Clamp(percent, 0, 100);
        }

        return operation.BytesTotal is > 0 && operation.BytesCompleted is { } done
            ? (int)Math.Clamp(done * 100d / operation.BytesTotal.Value, 0, 100)
            : null;
    }

    /// <summary>The download client behind an operation's provider id, with the product's own spelling.</summary>
    public static string? ClientName(string? providerId) =>
        string.IsNullOrWhiteSpace(providerId)
            ? null
            : string.Equals(providerId, "sabnzbd", StringComparison.OrdinalIgnoreCase) ? "SABnzbd" : providerId;

    /// <summary>What a session does to the video, for example "1080p HEVC" (played as is) or "2160p HEVC → 1080p H264".</summary>
    public static string? Video(PlaybackPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Video is not { } video)
        {
            return null;
        }

        var source = Join(video.SourceHeight is > 0 ? $"{video.SourceHeight}p" : null, Codec(video.SourceCodec));
        if (video.Copy)
        {
            return source;
        }

        var height = video.MaxOutputHeight is > 0 && video.SourceHeight is > 0
            ? Math.Min(video.MaxOutputHeight.Value, video.SourceHeight.Value)
            : video.MaxOutputHeight ?? video.SourceHeight;
        var output = Join(height is > 0 ? $"{height}p" : null, Codec(video.OutputCodec));
        return source is null ? output : output is null ? source : $"{source} → {output}";
    }

    /// <summary>
    /// The points of a sparkline polyline in a <paramref name="width"/> by <paramref name="height"/> box, or
    /// null when there are fewer than two values to draw. Values are drawn from 0 up to <paramref name="max"/>.
    /// </summary>
    public static string? SparklinePoints(IReadOnlyList<double> values, double max, int width = 120, int height = 32)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count < 2 || max <= 0)
        {
            return null;
        }

        var points = new List<string>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            var x = index * (double)width / (values.Count - 1);
            var ratio = Math.Clamp(values[index] / max, 0, 1);
            var y = height - 2 - ratio * (height - 4);
            points.Add(string.Create(CultureInfo.InvariantCulture, $"{x:0.#},{y:0.#}"));
        }

        return string.Join(' ', points);
    }

    private static string? Codec(string? codec) =>
        string.IsNullOrWhiteSpace(codec) ? null : codec.Trim().ToUpperInvariant();

    private static string? Join(string? first, string? second) =>
        (first, second) switch
        {
            (null, null) => null,
            (null, _) => second,
            (_, null) => first,
            _ => $"{first} {second}"
        };
}
