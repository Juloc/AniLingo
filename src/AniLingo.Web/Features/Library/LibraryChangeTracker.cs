namespace AniLingo.Web.Features.Library;

public sealed record LibraryChangeBatch(
    Guid RootId,
    bool FullReconciliation,
    IReadOnlyList<string> Folders);

// Coalesces filesystem events into dirty top-level folders per root. Nothing heavy happens
// here: recording is a dictionary update, and a batch is only released after the root has
// been quiet for the configured period so a download burst becomes one scan.
public sealed class LibraryChangeTracker(TimeSpan quietPeriod)
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, RootChanges> roots = new();

    public TimeSpan QuietPeriod { get; } = quietPeriod;

    public bool HasPending
    {
        get
        {
            lock (gate)
            {
                return roots.Count > 0;
            }
        }
    }

    public void RecordChange(Guid rootId, string rootPath, string fullPath, DateTime nowUtc)
    {
        var folder = TopLevelFolder(rootPath, fullPath);

        lock (gate)
        {
            var changes = Touch(rootId, nowUtc);
            if (folder is null)
            {
                changes.FullReconciliation = true;
            }
            else
            {
                changes.Folders.Add(folder);
            }
        }
    }

    // Buffer overflow or a broken watcher: events were lost, so only a full pass is safe.
    public void RecordOverflow(Guid rootId, DateTime nowUtc)
    {
        lock (gate)
        {
            Touch(rootId, nowUtc).FullReconciliation = true;
        }
    }

    public void Forget(Guid rootId)
    {
        lock (gate)
        {
            roots.Remove(rootId);
        }
    }

    // Releases the roots that have been quiet long enough; each appears once with either a
    // full reconciliation or its distinct dirty folders.
    public IReadOnlyList<LibraryChangeBatch> Drain(DateTime nowUtc)
    {
        lock (gate)
        {
            var due = roots
                .Where(pair => nowUtc - pair.Value.LastEventUtc >= QuietPeriod)
                .ToArray();

            var batches = new List<LibraryChangeBatch>(due.Length);
            foreach (var (rootId, changes) in due)
            {
                roots.Remove(rootId);
                batches.Add(new LibraryChangeBatch(
                    rootId,
                    changes.FullReconciliation,
                    changes.FullReconciliation
                        ? []
                        : changes.Folders.OrderBy(x => x, StringComparer.Ordinal).ToArray()));
            }

            return batches;
        }
    }

    // The anime directory directly below the root. Paths outside the root, the root itself
    // and entries without a folder cannot be scoped and request a full reconciliation.
    public static string? TopLevelFolder(string rootPath, string fullPath)
    {
        string relative;
        try
        {
            relative = Path.GetRelativePath(rootPath, fullPath);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            return null;
        }

        var segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? null : segments[0];
    }

    private RootChanges Touch(Guid rootId, DateTime nowUtc)
    {
        if (!roots.TryGetValue(rootId, out var changes))
        {
            changes = new RootChanges();
            roots.Add(rootId, changes);
        }

        changes.LastEventUtc = nowUtc;
        return changes;
    }

    private sealed class RootChanges
    {
        public DateTime LastEventUtc { get; set; }
        public bool FullReconciliation { get; set; }
        public HashSet<string> Folders { get; } = new(StringComparer.Ordinal);
    }
}

// The periodic safety reconciliation decision, anchored on the last completed full scan and
// on the last periodic attempt so an offline root backs off for a whole interval.
public static class LibraryReconciliationSchedule
{
    public static bool IsDue(
        int intervalMinutes,
        DateTime? lastScannedAtUtc,
        DateTime? lastAttemptUtc,
        DateTime nowUtc)
    {
        if (intervalMinutes <= 0)
        {
            return false;
        }

        var anchor = Later(lastScannedAtUtc, lastAttemptUtc);
        return anchor is null || nowUtc - anchor.Value >= TimeSpan.FromMinutes(intervalMinutes);
    }

    private static DateTime? Later(DateTime? left, DateTime? right) =>
        left is null ? right : right is null ? left : left > right ? left : right;
}
