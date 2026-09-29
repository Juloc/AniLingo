namespace Jularr.Web.Features.Operations;

/// <summary>
/// The admin-facing area a background operation belongs to. A presentation grouping over the
/// existing <see cref="OperationSnapshot"/> kinds — not a second job system.
/// </summary>
public enum ActivityLane
{
    Downloads = 1,
    Imports = 2,
    Scans = 3,
    Preparation = 4,
    Metadata = 5,
    Maintenance = 6,
    Other = 7
}

/// <summary>Maps an operation to the Activity Center lane it is shown in.</summary>
public static class ActivityLaneClassifier
{
    public static ActivityLane Classify(OperationSnapshot operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var kind = operation.Kind.Trim().ToLowerInvariant();

        if (kind.Contains("scan", StringComparison.Ordinal))
        {
            return ActivityLane.Scans;
        }

        if (operation.IsDownload
            || kind.Contains("sabnzbd-download", StringComparison.Ordinal)
            || kind.EndsWith("usenet-download", StringComparison.Ordinal)
            || kind is "anime-search" or "anime-search-request" or "anime-grab")
        {
            return ActivityLane.Downloads;
        }

        if (kind.StartsWith("anilist-", StringComparison.Ordinal)
            || kind.Contains("metadata", StringComparison.Ordinal)
            || kind.Contains("refresh", StringComparison.Ordinal)
            || kind.Contains("match", StringComparison.Ordinal)
            || kind.Contains("mapping", StringComparison.Ordinal)
            || kind.Contains("sync", StringComparison.Ordinal))
        {
            return ActivityLane.Metadata;
        }

        if (kind.Contains("import", StringComparison.Ordinal))
        {
            return ActivityLane.Imports;
        }

        if (operation.Lane == OperationLane.Interactive
            || kind.Contains("preparation", StringComparison.Ordinal)
            || kind.StartsWith("learning-text", StringComparison.Ordinal)
            || kind.Contains("translation", StringComparison.Ordinal)
            || kind.Contains("chapter-download", StringComparison.Ordinal)
            || kind.Contains("trickplay", StringComparison.Ordinal)
            || kind.Contains("segment", StringComparison.Ordinal)
            || kind.Contains("artwork", StringComparison.Ordinal))
        {
            return ActivityLane.Preparation;
        }

        return operation.Lane == OperationLane.Maintenance
            ? ActivityLane.Maintenance
            : ActivityLane.Other;
    }
}

/// <summary>One lane of the Activity Center with everything running, waiting or needing attention in it.</summary>
public sealed record ActivityGroup(
    ActivityLane Lane,
    IReadOnlyList<OperationSnapshot> Items,
    int Running,
    int Queued,
    int NeedsAttention,
    int? ProgressPercent,
    double? BytesPerSecond,
    DateTime? EtaUtc);

public sealed record ActivityCenterSnapshot(
    IReadOnlyList<ActivityGroup> Groups,
    int Running,
    int Queued,
    int NeedsAttention,
    int CompletedToday)
{
    public bool IsEmpty => Groups.Count == 0;

    public static ActivityCenterSnapshot Empty { get; } = new([], 0, 0, 0, 0);
}

/// <summary>
/// Builds the Activity Center from operation snapshots already read from the
/// <see cref="OperationStore"/>: active work plus recent failures, grouped into lanes.
/// </summary>
public static class ActivityCenterBuilder
{
    /// <summary>How long a failed or interrupted operation stays on the board before it is history only.</summary>
    public static readonly TimeSpan AttentionWindow = TimeSpan.FromHours(24);

    public static bool NeedsAttention(OperationSnapshot operation, DateTime nowUtc) =>
        operation.Status is OperationStatus.Failed or OperationStatus.Interrupted
        && operation.UpdatedAtUtc >= nowUtc - AttentionWindow;

    public static ActivityCenterSnapshot Build(
        IEnumerable<OperationSnapshot> operations,
        int completedToday,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var groups = operations
            .Where(operation => operation.IsActive || NeedsAttention(operation, nowUtc))
            .GroupBy(ActivityLaneClassifier.Classify)
            .OrderBy(group => group.Key)
            .Select(group => BuildGroup(group.Key, group, nowUtc))
            .ToArray();

        return new ActivityCenterSnapshot(
            groups,
            groups.Sum(group => group.Running),
            groups.Sum(group => group.Queued),
            groups.Sum(group => group.NeedsAttention),
            Math.Max(0, completedToday));
    }

    private static ActivityGroup BuildGroup(
        ActivityLane lane,
        IEnumerable<OperationSnapshot> operations,
        DateTime nowUtc)
    {
        // Running work first, then the queue in the order it will run, then problems newest first.
        var items = operations
            .OrderBy(operation => operation.Status switch
            {
                OperationStatus.Running => 0,
                OperationStatus.Queued => 1,
                _ => 2
            })
            .ThenBy(operation => operation.IsActive ? operation.CreatedAtUtc : DateTime.MaxValue)
            .ThenByDescending(operation => operation.UpdatedAtUtc)
            .ToArray();

        var running = items.Where(operation => operation.Status == OperationStatus.Running).ToArray();
        var progress = running
            .Where(operation => operation.ProgressPercent.HasValue)
            .Select(operation => operation.ProgressPercent!.Value)
            .ToArray();
        var speeds = running
            .Where(operation => operation.IsDownload && operation.BytesPerSecond is > 0)
            .Select(operation => operation.BytesPerSecond!.Value)
            .ToArray();
        var etas = running
            .Where(operation => operation.EtaUtc.HasValue)
            .Select(operation => operation.EtaUtc!.Value)
            .ToArray();

        return new ActivityGroup(
            lane,
            items,
            running.Length,
            items.Count(operation => operation.Status == OperationStatus.Queued),
            items.Count(operation => NeedsAttention(operation, nowUtc)),
            progress.Length == 0 ? null : (int)Math.Round(progress.Average()),
            speeds.Length == 0 ? null : speeds.Sum(),
            etas.Length == 0 ? null : etas.Max());
    }
}
