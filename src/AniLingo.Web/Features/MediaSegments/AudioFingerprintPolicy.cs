namespace AniLingo.Web.Features.MediaSegments;

// Turns raw per-sibling fingerprint matches into one detected segment, applying the acceptance
// rules from #135: reject too-short/too-long spans, and scale confidence with how many other
// episodes corroborate the same range (a match against a single sibling still counts, but a
// segment repeated across three or more episodes is far more likely to be a genuine OP/ED and
// not a coincidence). Pure and dependency-free so it is unit-testable with synthetic hashes.
public static class AudioFingerprintPolicy
{
    // Frames differing in at most this many of 32 bits still count as "the same" frame; chosen
    // to tolerate the light re-encoding noise between episode files while staying well below a
    // coincidental match (random 32-bit hashes differ in ~16 bits on average).
    public const int MaxHammingDistance = 4;

    // Within an accepted run, at least this fraction of frames must be within the Hamming bound.
    public const double MinMatchRatio = 0.85;

    public const double MinSegmentSeconds = 20;
    public const double MaxSegmentSeconds = 180;

    // A single corroborating sibling is enough to report a marker (2 episodes total); a second
    // corroborating sibling (3 episodes total, the acceptance criteria's preferred minimum)
    // raises confidence further. Additional siblings beyond that no longer add confidence.
    private const int ConfidenceSupportCap = 2;
    private const double BaseConfidenceWeight = 0.6;
    private const double PerSupportConfidenceWeight = 0.2;

    public static DetectedMediaSegment? Evaluate(
        MediaSegmentKind kind,
        IReadOnlyList<uint> ownHashes,
        double windowStartSeconds,
        IReadOnlyList<IReadOnlyList<uint>> siblingHashes)
    {
        if (ownHashes.Count == 0)
        {
            return null;
        }

        var minRunFrames = (int)Math.Ceiling(MinSegmentSeconds / AudioFingerprint.FrameHopSeconds);
        var maxRunFrames = (int)Math.Floor(MaxSegmentSeconds / AudioFingerprint.FrameHopSeconds);

        var candidates = new List<(int Start, int End, double MatchRatio)>();
        foreach (var sibling in siblingHashes)
        {
            if (sibling.Count == 0)
            {
                continue;
            }

            var match = AudioFingerprint.FindBestMatch(
                ownHashes,
                sibling,
                MaxHammingDistance,
                MinMatchRatio,
                minRunFrames,
                maxRunFrames);

            if (match is { } found)
            {
                candidates.Add((found.StartFrame, found.EndFrame, found.MatchRatio));
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var group = LargestOverlappingGroup(candidates);
        var startFrame = (int)Math.Round(group.Average(x => x.Start));
        var endFrame = (int)Math.Round(group.Average(x => x.End));
        var lengthSeconds = (endFrame - startFrame) * AudioFingerprint.FrameHopSeconds;
        if (lengthSeconds < MinSegmentSeconds || lengthSeconds > MaxSegmentSeconds)
        {
            return null;
        }

        var matchRatioAverage = group.Average(x => x.MatchRatio);
        var support = Math.Min(ConfidenceSupportCap, group.Count);
        var confidence = MediaSegmentPolicy.ClampConfidence(
            matchRatioAverage * (BaseConfidenceWeight + PerSupportConfidenceWeight * support));

        var startMs = (long)Math.Round((windowStartSeconds + startFrame * AudioFingerprint.FrameHopSeconds) * 1000);
        var endMs = (long)Math.Round((windowStartSeconds + endFrame * AudioFingerprint.FrameHopSeconds) * 1000);
        return new DetectedMediaSegment(kind, startMs, endMs, confidence);
    }

    // Connected components over interval overlap; the largest (most corroborated) component wins.
    private static List<(int Start, int End, double MatchRatio)> LargestOverlappingGroup(
        List<(int Start, int End, double MatchRatio)> candidates)
    {
        var groups = new List<List<(int Start, int End, double MatchRatio)>>();
        foreach (var candidate in candidates)
        {
            var joined = groups.FirstOrDefault(group =>
                group.Any(existing => Overlaps(existing, candidate)));

            if (joined is null)
            {
                groups.Add([candidate]);
            }
            else
            {
                joined.Add(candidate);

                // A candidate can bridge two previously separate groups; merge them.
                var others = groups.Where(g => g != joined && g.Any(x => Overlaps(x, candidate))).ToList();
                foreach (var other in others)
                {
                    joined.AddRange(other);
                    groups.Remove(other);
                }
            }
        }

        return groups
            .OrderByDescending(g => g.Count)
            .ThenByDescending(g => g.Average(x => x.MatchRatio))
            .First();
    }

    private static bool Overlaps((int Start, int End, double MatchRatio) a, (int Start, int End, double MatchRatio) b) =>
        a.Start < b.End && b.Start < a.End;
}
