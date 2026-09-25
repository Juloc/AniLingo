namespace AniLingo.Web.Features.MediaMapping;

public sealed record LinearRelationNode<T>(
    T Candidate,
    IReadOnlyList<string> Prequels,
    IReadOnlyList<string> Sequels);

public sealed record LinearRelationSequenceResult<T>(
    IReadOnlyList<T> Entries,
    bool IsUnambiguous,
    string Reason);

public static class LinearRelationSequence
{
    public static async Task<LinearRelationSequenceResult<T>> ResolveAsync<T>(
        string rootId,
        Func<string, CancellationToken, Task<LinearRelationNode<T>?>> loader,
        CancellationToken cancellationToken,
        int maximumEntries = 16)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootId);
        ArgumentNullException.ThrowIfNull(loader);

        var root = await loader(rootId, cancellationToken);
        if (root is null)
        {
            return new LinearRelationSequenceResult<T>(
                [],
                false,
                "The matched AniList entry could not be loaded.");
        }

        var before = new List<T>();
        var visited = new HashSet<string>(StringComparer.Ordinal)
        {
            rootId
        };

        var currentId = rootId;
        var current = root;

        while (before.Count < maximumEntries - 1)
        {
            if (current.Prequels.Count > 1)
            {
                return new LinearRelationSequenceResult<T>(
                    [],
                    false,
                    "AniList has multiple compatible prequels, so the reading sequence is ambiguous.");
            }

            if (current.Prequels.Count == 0)
            {
                break;
            }

            var previousId = current.Prequels[0];
            if (!visited.Add(previousId))
            {
                return new LinearRelationSequenceResult<T>(
                    [],
                    false,
                    "AniList relation data contains a cycle.");
            }

            var previous = await loader(previousId, cancellationToken);
            if (previous is null)
            {
                return new LinearRelationSequenceResult<T>(
                    [],
                    false,
                    "A related AniList prequel could not be loaded.");
            }

            if (previous.Sequels.Count != 1 ||
                !string.Equals(
                    previous.Sequels[0],
                    currentId,
                    StringComparison.Ordinal))
            {
                return new LinearRelationSequenceResult<T>(
                    [],
                    false,
                    "AniList prequel relations branch or do not point back to the current entry.");
            }

            before.Insert(0, previous.Candidate);
            currentId = previousId;
            current = previous;
        }

        var result = new List<T>(before.Count + 1);
        result.AddRange(before);
        result.Add(root.Candidate);

        currentId = rootId;
        current = root;

        while (result.Count < maximumEntries)
        {
            if (current.Sequels.Count > 1)
            {
                return new LinearRelationSequenceResult<T>(
                    [],
                    false,
                    "AniList has multiple compatible sequels, so the reading sequence is ambiguous.");
            }

            if (current.Sequels.Count == 0)
            {
                break;
            }

            var nextId = current.Sequels[0];
            if (!visited.Add(nextId))
            {
                return new LinearRelationSequenceResult<T>(
                    [],
                    false,
                    "AniList relation data contains a cycle.");
            }

            var next = await loader(nextId, cancellationToken);
            if (next is null)
            {
                return new LinearRelationSequenceResult<T>(
                    [],
                    false,
                    "A related AniList sequel could not be loaded.");
            }

            if (next.Prequels.Count != 1 ||
                !string.Equals(
                    next.Prequels[0],
                    currentId,
                    StringComparison.Ordinal))
            {
                return new LinearRelationSequenceResult<T>(
                    [],
                    false,
                    "AniList sequel relations branch or do not point back to the current entry.");
            }

            result.Add(next.Candidate);
            currentId = nextId;
            current = next;
        }

        return new LinearRelationSequenceResult<T>(
            result,
            true,
            result.Count > 1
                ? "AniList relations form one linear reading sequence."
                : "AniList has no compatible linear prequel/sequel chain for this entry.");
    }
}

public sealed record LocalReadingChapter(
    double Number,
    int? VolumeNumber = null);

public sealed record RemoteReadingPart(
    string Provider,
    string ExternalId,
    string Title,
    int ChapterCount,
    int? VolumeCount = null);

public sealed record PlannedReadingSegment(
    double LocalChapterStart,
    double LocalChapterEnd,
    int RemoteChapterStart,
    RemoteReadingPart RemotePart,
    int? LocalVolumeStart = null,
    int? LocalVolumeEnd = null,
    int? RemoteVolumeStart = null);

public sealed record AutomaticReadingSegmentPlan(
    bool CanApply,
    string Reason,
    IReadOnlyList<PlannedReadingSegment> Segments)
{
    public bool NoMappingRequired =>
        !CanApply &&
        Reason.StartsWith(
            "The primary AniList match already uses",
            StringComparison.Ordinal);

    public static AutomaticReadingSegmentPlan Blocked(string reason) =>
        new(false, reason, []);
}

public static class AutomaticReadingSegmentPlanner
{
    public static AutomaticReadingSegmentPlan Plan(
        IReadOnlyList<LocalReadingChapter> localChapters,
        IReadOnlyList<RemoteReadingPart> remoteSequence,
        string anchorExternalId)
    {
        if (localChapters.Count == 0)
        {
            return AutomaticReadingSegmentPlan.Blocked(
                "No local chapters are available for automatic segment mapping.");
        }

        if (remoteSequence.Count == 0 ||
            string.IsNullOrWhiteSpace(anchorExternalId))
        {
            return AutomaticReadingSegmentPlan.Blocked(
                "No AniList reading sequence is available.");
        }

        var orderedLocal = localChapters
            .OrderBy(x => x.Number)
            .ToArray();

        if (!HasContiguousChapterOffsets(orderedLocal))
        {
            return AutomaticReadingSegmentPlan.Blocked(
                "Local chapter numbering has gaps or unsupported offsets.");
        }

        if (remoteSequence.Any(x => x.ChapterCount <= 0))
        {
            return AutomaticReadingSegmentPlan.Blocked(
                "AniList does not expose reliable chapter counts for the full reading sequence.");
        }

        var matchingWindows = new List<(int Start, int End)>();
        for (var start = 0; start < remoteSequence.Count; start++)
        {
            var chapterCount = 0;
            var containsAnchor = false;

            for (var end = start; end < remoteSequence.Count; end++)
            {
                checked
                {
                    chapterCount += remoteSequence[end].ChapterCount;
                }

                containsAnchor |= string.Equals(
                    remoteSequence[end].ExternalId,
                    anchorExternalId,
                    StringComparison.Ordinal);

                if (chapterCount == orderedLocal.Length && containsAnchor)
                {
                    matchingWindows.Add((start, end));
                }

                if (chapterCount >= orderedLocal.Length)
                {
                    break;
                }
            }
        }

        if (matchingWindows.Count != 1)
        {
            return AutomaticReadingSegmentPlan.Blocked(
                matchingWindows.Count == 0
                    ? "Local chapter count does not match one AniList prequel/sequel range."
                    : "More than one AniList prequel/sequel range fits the local chapter count.");
        }

        var window = matchingWindows[0];
        var segments = new List<PlannedReadingSegment>();
        var localIndex = 0;

        for (var remoteIndex = window.Start;
             remoteIndex <= window.End;
             remoteIndex++)
        {
            var remote = remoteSequence[remoteIndex];
            var localEndIndex = localIndex + remote.ChapterCount - 1;

            if (localEndIndex >= orderedLocal.Length)
            {
                return AutomaticReadingSegmentPlan.Blocked(
                    "The local chapter list ended before the AniList sequence.");
            }

            var slice = orderedLocal[localIndex..(localEndIndex + 1)];
            var localStart = slice[0].Number;
            var localEnd = slice[^1].Number;

            int? localVolumeStart = null;
            int? localVolumeEnd = null;
            int? remoteVolumeStart = null;

            if (remote.VolumeCount is > 0 &&
                TryResolveVolumeRange(
                    slice,
                    remote.VolumeCount.Value,
                    out var volumeStart,
                    out var volumeEnd))
            {
                localVolumeStart = volumeStart;
                localVolumeEnd = volumeEnd;
                remoteVolumeStart = 1;
            }

            segments.Add(new PlannedReadingSegment(
                localStart,
                localEnd,
                1,
                remote,
                localVolumeStart,
                localVolumeEnd,
                remoteVolumeStart));

            localIndex = localEndIndex + 1;
        }

        if (localIndex != orderedLocal.Length)
        {
            return AutomaticReadingSegmentPlan.Blocked(
                "Not every local chapter was consumed by the AniList sequence.");
        }

        var requiresSegments =
            segments.Count > 1 ||
            Math.Abs(segments[0].LocalChapterStart - 1d) > 0.0001 ||
            segments[0].RemoteChapterStart != 1;

        return requiresSegments
            ? new AutomaticReadingSegmentPlan(
                true,
                "Local chapters map uniquely to the AniList prequel/sequel sequence.",
                segments)
            : AutomaticReadingSegmentPlan.Blocked(
                "The primary AniList match already uses the same chapter numbering; no segment mapping is required.");
    }

    private static bool HasContiguousChapterOffsets(
        IReadOnlyList<LocalReadingChapter> chapters)
    {
        if (chapters.Any(x => x.Number <= 0))
        {
            return false;
        }

        for (var index = 1; index < chapters.Count; index++)
        {
            var delta = chapters[index].Number - chapters[index - 1].Number;
            if (Math.Abs(delta - 1d) > 0.0001)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryResolveVolumeRange(
        IReadOnlyList<LocalReadingChapter> chapters,
        int remoteVolumeCount,
        out int localVolumeStart,
        out int localVolumeEnd)
    {
        localVolumeStart = 0;
        localVolumeEnd = 0;

        if (chapters.Any(x => x.VolumeNumber is null))
        {
            return false;
        }

        var volumes = chapters
            .Select(x => x.VolumeNumber!.Value)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        if (volumes.Length != remoteVolumeCount ||
            volumes.Length == 0)
        {
            return false;
        }

        for (var index = 1; index < volumes.Length; index++)
        {
            if (volumes[index] != volumes[index - 1] + 1)
            {
                return false;
            }
        }

        localVolumeStart = volumes[0];
        localVolumeEnd = volumes[^1];
        return true;
    }
}
