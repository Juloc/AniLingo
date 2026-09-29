using Jularr.Web.Features.MediaCore;

namespace Jularr.Web.Features.Collections;

/// <summary>
/// Turns a rule condition into a short, human-readable phrase used both in the rule builder and as the
/// "why it matched" explanation on a smart collection's items (#427). Deliberately terse and stable so it
/// can be asserted in tests; it never fabricates — it only restates the condition that actually held.
/// </summary>
public static class CollectionRuleDescriber
{
    public static string Describe(CollectionRuleCondition condition)
    {
        var field = FieldLabel(condition.Field);
        return condition.Field switch
        {
            CollectionRuleField.HasFranchise => condition.Operator == CollectionRuleOperator.IsAbsent
                ? "Not part of a franchise"
                : "Part of a franchise",
            CollectionRuleField.Franchise => condition.Operator == CollectionRuleOperator.NotContains
                ? $"Not in franchise {condition.Value}"
                : $"In franchise {condition.Value}",
            CollectionRuleField.Language or CollectionRuleField.LanguageComplete =>
                $"{field} {OperatorPhrase(condition.Operator)} {condition.Value.ToUpperInvariant()}",
            CollectionRuleField.MediaType =>
                $"{field} {OperatorPhrase(condition.Operator)} {MediaTypeLabel(condition.Value)}",
            _ when condition.Operator is CollectionRuleOperator.IsPresent =>
                $"{field} is set",
            _ when condition.Operator is CollectionRuleOperator.IsAbsent =>
                $"{field} is not set",
            _ => $"{field} {OperatorPhrase(condition.Operator)} {condition.Value}"
        };
    }

    public static string FieldLabel(CollectionRuleField field) => field switch
    {
        CollectionRuleField.MediaType => "Media type",
        CollectionRuleField.Status => "Status",
        CollectionRuleField.ReleaseYear => "Release year",
        CollectionRuleField.PrimaryUnitCount => "Episodes/chapters",
        CollectionRuleField.SecondaryUnitCount => "Seasons/volumes",
        CollectionRuleField.Language => "Language",
        CollectionRuleField.LanguageComplete => "Complete language",
        CollectionRuleField.Franchise => "Franchise",
        CollectionRuleField.HasFranchise => "Franchise",
        _ => field.ToString()
    };

    private static string OperatorPhrase(CollectionRuleOperator op) => op switch
    {
        CollectionRuleOperator.Equals => "is",
        CollectionRuleOperator.NotEquals => "is not",
        CollectionRuleOperator.GreaterThan => ">",
        CollectionRuleOperator.GreaterThanOrEqual => "≥",
        CollectionRuleOperator.LessThan => "<",
        CollectionRuleOperator.LessThanOrEqual => "≤",
        CollectionRuleOperator.Contains => "includes",
        CollectionRuleOperator.NotContains => "excludes",
        CollectionRuleOperator.IsPresent => "is set",
        CollectionRuleOperator.IsAbsent => "is not set",
        _ => op.ToString()
    };

    private static string MediaTypeLabel(string value) =>
        WorkMediaTypes.Parse(value) is { } type ? type.ToString() : value;
}
