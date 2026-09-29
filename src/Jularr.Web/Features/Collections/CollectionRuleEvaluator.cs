using System.Globalization;
using Jularr.Web.Features.MediaCore;
using Jularr.Web.Ui;

namespace Jularr.Web.Features.Collections;

/// <summary>
/// The outcome of evaluating a rule tree against one work: whether it matched and, when it did, the
/// human-readable reasons each contributing condition matched — so a smart collection can always explain
/// why an item is on the shelf (#427).
/// </summary>
public sealed record CollectionMatch(bool Matched, IReadOnlyList<string> Reasons)
{
    public static readonly CollectionMatch NoMatch = new(false, []);
}

/// <summary>
/// The pure, deterministic core of smart collections (#427): it evaluates a nested ALL/ANY rule tree over
/// a <see cref="WorkFactSnapshot"/> with no database or provider dependency, and returns explainable
/// reasons for every match. All membership decisions flow through here, so nested combinators, cross-media
/// facts and explainability are covered by unit tests alone.
/// </summary>
public static class CollectionRuleEvaluator
{
    public static CollectionMatch Evaluate(CollectionRuleNode? node, WorkFactSnapshot snapshot) => node switch
    {
        null => CollectionMatch.NoMatch,
        CollectionRuleGroup group => EvaluateGroup(group, snapshot),
        CollectionRuleCondition condition => EvaluateCondition(condition, snapshot),
        _ => CollectionMatch.NoMatch
    };

    private static CollectionMatch EvaluateGroup(CollectionRuleGroup group, WorkFactSnapshot snapshot)
    {
        var results = group.Children.Select(child => Evaluate(child, snapshot)).ToArray();

        if (group.Combinator == CollectionRuleCombinator.All)
        {
            // An empty ALL group is vacuously true, but it matches nothing meaningful, so it carries no
            // reason of its own; a non-empty ALL matches only when every child does.
            var matched = results.All(r => r.Matched);
            return matched
                ? new CollectionMatch(true, results.SelectMany(r => r.Reasons).Distinct().ToArray())
                : CollectionMatch.NoMatch;
        }

        // ANY: matches when at least one child matches; the reasons are those of the matching children.
        var anyMatched = results.Where(r => r.Matched).ToArray();
        return anyMatched.Length > 0
            ? new CollectionMatch(true, anyMatched.SelectMany(r => r.Reasons).Distinct().ToArray())
            : CollectionMatch.NoMatch;
    }

    private static CollectionMatch EvaluateCondition(CollectionRuleCondition condition, WorkFactSnapshot snapshot)
    {
        var matched = Matches(condition, snapshot);
        return matched
            ? new CollectionMatch(true, [CollectionRuleDescriber.Describe(condition)])
            : CollectionMatch.NoMatch;
    }

    private static bool Matches(CollectionRuleCondition condition, WorkFactSnapshot s) => condition.Field switch
    {
        CollectionRuleField.MediaType => MatchMediaType(condition, s),
        CollectionRuleField.Status => MatchStatus(condition, s),
        CollectionRuleField.ReleaseYear => MatchNumber(condition, s.Year),
        CollectionRuleField.PrimaryUnitCount => MatchNumber(condition, s.PrimaryUnitCount),
        CollectionRuleField.SecondaryUnitCount => MatchNumber(condition, s.SecondaryUnitCount),
        CollectionRuleField.Language => MatchSet(condition, s.LanguageCodes, NormalizeLanguage(condition.Value)),
        CollectionRuleField.LanguageComplete => MatchSet(condition, s.CompleteLanguageCodes, NormalizeLanguage(condition.Value)),
        CollectionRuleField.Franchise => MatchFranchise(condition, s),
        CollectionRuleField.HasFranchise => MatchPresence(condition, s.IsInFranchise),
        _ => false
    };

    private static bool MatchMediaType(CollectionRuleCondition condition, WorkFactSnapshot s)
    {
        var target = WorkMediaTypes.Parse(condition.Value);
        if (target is null)
        {
            return false;
        }

        return condition.Operator switch
        {
            CollectionRuleOperator.NotEquals => s.MediaType != target,
            _ => s.MediaType == target
        };
    }

    private static bool MatchStatus(CollectionRuleCondition condition, WorkFactSnapshot s)
    {
        if (!Enum.TryParse<MediaReleaseStatus>(condition.Value, ignoreCase: true, out var target))
        {
            return false;
        }

        return condition.Operator switch
        {
            CollectionRuleOperator.NotEquals => s.Status != target,
            _ => s.Status == target
        };
    }

    private static bool MatchNumber(CollectionRuleCondition condition, int? actual)
    {
        if (condition.Operator == CollectionRuleOperator.IsPresent)
        {
            return actual is not null;
        }

        if (condition.Operator == CollectionRuleOperator.IsAbsent)
        {
            return actual is null;
        }

        if (actual is null || !int.TryParse(condition.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        return condition.Operator switch
        {
            CollectionRuleOperator.Equals => actual == value,
            CollectionRuleOperator.NotEquals => actual != value,
            CollectionRuleOperator.GreaterThan => actual > value,
            CollectionRuleOperator.GreaterThanOrEqual => actual >= value,
            CollectionRuleOperator.LessThan => actual < value,
            CollectionRuleOperator.LessThanOrEqual => actual <= value,
            _ => false
        };
    }

    private static bool MatchSet(CollectionRuleCondition condition, IEnumerable<string> set, string needle)
    {
        var contains = set.Any(x => string.Equals(x, needle, StringComparison.OrdinalIgnoreCase));
        return condition.Operator switch
        {
            CollectionRuleOperator.NotContains => !contains,
            _ => contains
        };
    }

    private static bool MatchFranchise(CollectionRuleCondition condition, WorkFactSnapshot s)
    {
        if (!Guid.TryParse(condition.Value, out var franchiseId))
        {
            return false;
        }

        var contains = s.FranchiseIds.Contains(franchiseId);
        return condition.Operator switch
        {
            CollectionRuleOperator.NotContains => !contains,
            _ => contains
        };
    }

    private static bool MatchPresence(CollectionRuleCondition condition, bool present) => condition.Operator switch
    {
        CollectionRuleOperator.IsAbsent => !present,
        CollectionRuleOperator.NotEquals => !present,
        _ => present
    };

    private static string NormalizeLanguage(string value) => value.Trim().ToLowerInvariant();
}
