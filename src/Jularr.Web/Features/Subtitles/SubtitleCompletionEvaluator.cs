namespace Jularr.Web.Features.Subtitles;

public enum SubtitleProfileCompletionStatus
{
    /// <summary>Every wanted item in the profile is satisfied.</summary>
    Complete,

    /// <summary>
    /// A cutoff is configured and an item at or above it is satisfied, so Jularr stops here even
    /// though a lower-priority item past the cutoff remains unmet.
    /// </summary>
    CutoffMet,

    /// <summary>Neither of the above: at least one required item (up to the cutoff, if any) is missing.</summary>
    Missing
}

public sealed record SubtitleCompletionResult(
    SubtitleProfileCompletionStatus Status,
    IReadOnlyList<int> MissingIndexes);

/// <summary>
/// Pure evaluation of "is this language profile satisfied" from a precomputed per-item satisfied
/// flag list, kept separate from <see cref="SubtitleCompletenessService"/> (which does the DB/track
/// lookups to produce those flags) so the cutoff/priority rules are trivially unit-testable.
/// </summary>
public static class SubtitleCompletionEvaluator
{
    /// <param name="satisfied">
    /// One flag per profile item, in the same priority order as the profile's items
    /// (index 0 = highest priority / searched first).
    /// </param>
    /// <param name="cutoffPosition">
    /// 0-based index of the profile's cutoff item, or null when every item is required.
    /// </param>
    public static SubtitleCompletionResult Evaluate(
        IReadOnlyList<bool> satisfied,
        int? cutoffPosition)
    {
        ArgumentNullException.ThrowIfNull(satisfied);

        if (satisfied.Count == 0)
        {
            // No wanted languages configured: nothing to be missing.
            return new SubtitleCompletionResult(SubtitleProfileCompletionStatus.Complete, []);
        }

        if (cutoffPosition is int cutoff && cutoff >= 0 && cutoff < satisfied.Count)
        {
            var bestSatisfiedIndex = BestSatisfiedIndex(satisfied);
            if (bestSatisfiedIndex is int best && best <= cutoff)
            {
                var status = satisfied.All(x => x)
                    ? SubtitleProfileCompletionStatus.Complete
                    : SubtitleProfileCompletionStatus.CutoffMet;
                return new SubtitleCompletionResult(status, []);
            }

            var missingUpToCutoff = Enumerable.Range(0, cutoff + 1)
                .Where(i => !satisfied[i])
                .ToArray();
            return new SubtitleCompletionResult(SubtitleProfileCompletionStatus.Missing, missingUpToCutoff);
        }

        var missingAll = Enumerable.Range(0, satisfied.Count)
            .Where(i => !satisfied[i])
            .ToArray();

        return missingAll.Length == 0
            ? new SubtitleCompletionResult(SubtitleProfileCompletionStatus.Complete, [])
            : new SubtitleCompletionResult(SubtitleProfileCompletionStatus.Missing, missingAll);
    }

    private static int? BestSatisfiedIndex(IReadOnlyList<bool> satisfied)
    {
        for (var i = 0; i < satisfied.Count; i++)
        {
            if (satisfied[i])
            {
                return i;
            }
        }

        return null;
    }
}
