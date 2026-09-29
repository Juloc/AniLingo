namespace Jularr.Web.Features.OfflineLibrary;

public enum OfflinePrefetchKind
{
    Episode,
    Chapter
}

/// <summary>Why an item is on the device. Only <see cref="Prefetched"/> items may ever be evicted by prefetch.</summary>
public enum OfflinePrefetchOrigin
{
    /// <summary>The user asked for this download (or opened a prefetched item and kept it).</summary>
    Explicit,

    /// <summary>Downloaded by smart prefetch.</summary>
    Prefetched
}

public enum OfflinePrefetchConnection
{
    Unmetered,
    Metered
}

public enum OfflinePrefetchReason
{
    /// <summary>The plan was computed normally (it may still be empty).</summary>
    Planned,

    /// <summary>Prefetch is switched off for this profile; the plan is always empty.</summary>
    Disabled,

    /// <summary>The device is on a metered connection and the policy does not allow that.</summary>
    MeteredConnection
}

/// <summary>One offline item currently on the device, as reported by the client.</summary>
public sealed record OfflinePrefetchInventoryItem(
    OfflinePrefetchKind Kind,
    Guid ItemId,
    long SizeBytes,
    OfflinePrefetchOrigin Origin,
    DateTime LastUsedUtc,
    bool Active = false);

/// <summary>
/// What the client tells the server about the device. The inventory is the
/// only source of truth for what is stored offline; the server keeps no
/// second copy of it.
/// </summary>
public sealed record OfflinePrefetchDeviceState(
    OfflinePrefetchConnection Connection,
    long? DeviceLimitBytes,
    IReadOnlyList<OfflinePrefetchInventoryItem> Inventory);

/// <summary>A next-up item worth having offline, in priority order.</summary>
public sealed record OfflinePrefetchCandidate(
    OfflinePrefetchKind Kind,
    Guid ItemId,
    Guid ContainerId,
    string Title,
    long SizeBytes);

public sealed record OfflinePrefetchPlan(
    OfflinePrefetchReason Reason,
    long CapBytes,
    long BudgetBytes,
    long PrefetchedBytesAfter,
    IReadOnlyList<OfflinePrefetchCandidate> Downloads,
    IReadOnlyList<OfflinePrefetchInventoryItem> Evictions)
{
    public static OfflinePrefetchPlan Empty(OfflinePrefetchReason reason, OfflinePrefetchPolicy policy) =>
        new(reason, policy.CapBytes, 0, 0, [], []);
}

/// <summary>
/// Pure prefetch decision (#415). Given the policy, the device inventory and
/// the ordered next-up candidates it returns what to download and what to
/// evict so that:
/// <list type="bullet">
/// <item>nothing happens while the policy is Off, and no download is planned on a metered
/// connection unless the policy allows it;</item>
/// <item>prefetched bytes never exceed the hard cap (or what the device limit leaves after
/// explicit downloads, whichever is smaller);</item>
/// <item>only <see cref="OfflinePrefetchOrigin.Prefetched"/> items are ever evicted, least recently
/// used first, and never an item that is being downloaded or that is itself wanted next.</item>
/// </list>
/// Explicit downloads are never evicted and never counted against the prefetch cap.
/// </summary>
public static class OfflinePrefetchPlanner
{
    /// <summary>Upper bound of downloads suggested by one plan.</summary>
    public const int MaxDownloadsPerPlan = 20;

    public static OfflinePrefetchPlan Plan(
        OfflinePrefetchPolicy policy,
        IReadOnlyList<OfflinePrefetchCandidate> candidates,
        OfflinePrefetchDeviceState device)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(device);

        if (!policy.Enabled)
        {
            return OfflinePrefetchPlan.Empty(OfflinePrefetchReason.Disabled, policy);
        }

        var inventory = Normalize(device.Inventory);
        var explicitBytes = inventory
            .Where(x => x.Origin == OfflinePrefetchOrigin.Explicit)
            .Sum(x => x.SizeBytes);
        var budget = policy.CapBytes;
        if (device.DeviceLimitBytes is { } deviceLimit)
        {
            budget = Math.Min(budget, Math.Max(0, deviceLimit - explicitBytes));
        }

        var wanted = candidates
            .Where(x => InScope(policy, x.Kind))
            .ToArray();
        var wantedKeys = wanted.Select(x => (x.Kind, x.ItemId)).ToHashSet();
        var known = inventory.Select(x => (x.Kind, x.ItemId)).ToHashSet();

        // Least recently used first; items the plan wants next go last so they are only
        // sacrificed when the cap itself shrinks below what is stored.
        var evictable = inventory
            .Where(x => x.Origin == OfflinePrefetchOrigin.Prefetched && !x.Active)
            .OrderBy(x => wantedKeys.Contains((x.Kind, x.ItemId)) ? 1 : 0)
            .ThenBy(x => x.LastUsedUtc)
            .ThenBy(x => x.ItemId)
            .ToArray();

        var prefetchedBytes = inventory
            .Where(x => x.Origin == OfflinePrefetchOrigin.Prefetched)
            .Sum(x => x.SizeBytes);
        var evictions = new List<OfflinePrefetchInventoryItem>();
        var evicted = new HashSet<(OfflinePrefetchKind, Guid)>();

        // The cap was lowered (or explicit downloads grew): shrink back under budget.
        if (prefetchedBytes > budget)
        {
            foreach (var victim in evictable)
            {
                if (prefetchedBytes <= budget)
                {
                    break;
                }

                evictions.Add(victim);
                evicted.Add((victim.Kind, victim.ItemId));
                prefetchedBytes -= victim.SizeBytes;
            }
        }

        var downloads = new List<OfflinePrefetchCandidate>();
        var reason = OfflinePrefetchReason.Planned;

        if (device.Connection == OfflinePrefetchConnection.Metered && !policy.AllowMetered)
        {
            reason = OfflinePrefetchReason.MeteredConnection;
        }
        else
        {
            var planned = new HashSet<(OfflinePrefetchKind, Guid)>();

            foreach (var candidate in wanted)
            {
                if (downloads.Count >= MaxDownloadsPerPlan)
                {
                    break;
                }

                var key = (candidate.Kind, candidate.ItemId);
                if (candidate.SizeBytes <= 0 ||
                    candidate.SizeBytes > budget ||
                    known.Contains(key) ||
                    !planned.Add(key))
                {
                    continue;
                }

                var overflow = prefetchedBytes + candidate.SizeBytes - budget;
                if (overflow > 0)
                {
                    // Make room from stale prefetched items only; skip the candidate when
                    // that is not enough (explicit and wanted items are never touched).
                    var victims = new List<OfflinePrefetchInventoryItem>();
                    long freed = 0;
                    foreach (var victim in evictable)
                    {
                        if (freed >= overflow)
                        {
                            break;
                        }

                        if (evicted.Contains((victim.Kind, victim.ItemId)) ||
                            wantedKeys.Contains((victim.Kind, victim.ItemId)))
                        {
                            continue;
                        }

                        victims.Add(victim);
                        freed += victim.SizeBytes;
                    }

                    if (freed < overflow)
                    {
                        planned.Remove(key);
                        continue;
                    }

                    foreach (var victim in victims)
                    {
                        evictions.Add(victim);
                        evicted.Add((victim.Kind, victim.ItemId));
                    }

                    prefetchedBytes -= freed;
                }

                downloads.Add(candidate);
                prefetchedBytes += candidate.SizeBytes;
            }
        }

        return new OfflinePrefetchPlan(
            reason,
            policy.CapBytes,
            budget,
            prefetchedBytes,
            downloads,
            evictions);
    }

    private static bool InScope(OfflinePrefetchPolicy policy, OfflinePrefetchKind kind) =>
        kind == OfflinePrefetchKind.Episode ? policy.IncludeEpisodes : policy.IncludeChapters;

    /// <summary>
    /// Collapses duplicate reports of one item (an explicit copy always wins, so a
    /// mislabelled duplicate can never expose it to eviction) and clamps sizes.
    /// </summary>
    private static IReadOnlyList<OfflinePrefetchInventoryItem> Normalize(
        IReadOnlyList<OfflinePrefetchInventoryItem> inventory) =>
        [
            .. inventory
                .GroupBy(x => (x.Kind, x.ItemId))
                .Select(group =>
                {
                    var preferred = group
                        .OrderBy(x => x.Origin)
                        .ThenByDescending(x => x.LastUsedUtc)
                        .First();
                    return preferred with
                    {
                        SizeBytes = Math.Max(0, group.Max(x => x.SizeBytes)),
                        Active = group.Any(x => x.Active)
                    };
                })
        ];
}
