namespace Jularr.Web.Features.Collections;

/// <summary>One work's computed membership in a smart collection: its snapshot and the reasons it matched.</summary>
public sealed record CollectionMembership(WorkFactSnapshot Work, IReadOnlyList<string> Reasons);

/// <summary>
/// Computes which works belong to a smart collection by running its rule tree over the candidate works'
/// fact snapshots (#427). Pure: it takes the rule and the snapshots and returns the ordered membership with
/// per-item reasons, leaving persistence and caching to <see cref="CollectionService"/>. Because it is
/// side-effect free, nested ALL/ANY evaluation, cross-media membership and explainability are all testable
/// without a database.
/// </summary>
public static class SmartCollectionMaterializer
{
    /// <summary>
    /// The matched members, ordered newest-first then by title, so a materialized shelf reads like the rest
    /// of Jularr's discovery surfaces. An empty or null rule yields no members (a smart collection with no
    /// rule shows nothing rather than the whole library).
    /// </summary>
    public static IReadOnlyList<CollectionMembership> Match(
        CollectionRuleNode? rule,
        IEnumerable<WorkFactSnapshot> snapshots)
    {
        if (rule is null)
        {
            return [];
        }

        return
        [
            .. snapshots
                .Select(snapshot => (snapshot, match: CollectionRuleEvaluator.Evaluate(rule, snapshot)))
                .Where(x => x.match.Matched)
                .OrderByDescending(x => x.snapshot.Year ?? int.MinValue)
                .ThenBy(x => x.snapshot.Title, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => new CollectionMembership(x.snapshot, x.match.Reasons))
        ];
    }
}
