using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jularr.Web.Features.Collections;

/// <summary>How the children of a <see cref="CollectionRuleGroup"/> combine (Kometa-like ALL/ANY).</summary>
public enum CollectionRuleCombinator
{
    /// <summary>Every child must match (logical AND). An empty ALL group matches everything.</summary>
    All,

    /// <summary>At least one child must match (logical OR). An empty ANY group matches nothing.</summary>
    Any
}

/// <summary>
/// A canonical media fact a smart-collection condition tests (#427). Every field is drawn from the
/// provider-independent facts Jularr already computes — the media core <see cref="Jularr.Web.Features.MediaCore.Work"/>
/// plus <see cref="Jularr.Web.Features.MediaFacts.MediaFactsService"/> — or from franchise relations, so a
/// rule never invents data.
/// </summary>
public enum CollectionRuleField
{
    /// <summary>The work's media type (anime, manga, light novel, book, movie, series).</summary>
    MediaType,

    /// <summary>The work's release status (ongoing, finished, upcoming, hiatus, cancelled).</summary>
    Status,

    /// <summary>The work's release/start year.</summary>
    ReleaseYear,

    /// <summary>Episodes (anime) or chapters (reading media) known for the work.</summary>
    PrimaryUnitCount,

    /// <summary>Seasons (anime) or volumes (reading media) known for the work.</summary>
    SecondaryUnitCount,

    /// <summary>A language present anywhere in the work's audio/subtitle/text facts (any coverage).</summary>
    Language,

    /// <summary>A language whose coverage across the work's units is complete.</summary>
    LanguageComplete,

    /// <summary>The work belongs to the franchise identified by the value (a franchise id).</summary>
    Franchise,

    /// <summary>The work has at least one franchise relation of any kind (i.e. is part of a franchise).</summary>
    HasFranchise
}

/// <summary>How a <see cref="CollectionRuleCondition"/> compares a fact to its value.</summary>
public enum CollectionRuleOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,

    /// <summary>The fact set contains the value (languages, franchises); for scalars, equality.</summary>
    Contains,

    /// <summary>The fact set does not contain the value.</summary>
    NotContains,

    /// <summary>The fact is present/true (value ignored).</summary>
    IsPresent,

    /// <summary>The fact is absent/false (value ignored).</summary>
    IsAbsent
}

/// <summary>
/// One node of a smart-collection rule tree (#427): either a <see cref="CollectionRuleGroup"/> that combines
/// children with ALL/ANY, or a leaf <see cref="CollectionRuleCondition"/> that tests one canonical fact.
/// Polymorphic JSON (<c>type</c> discriminator) so the whole tree round-trips through one column.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CollectionRuleGroup), "group")]
[JsonDerivedType(typeof(CollectionRuleCondition), "condition")]
public abstract record CollectionRuleNode;

/// <summary>A nested ALL/ANY group of child rule nodes.</summary>
public sealed record CollectionRuleGroup(
    CollectionRuleCombinator Combinator,
    IReadOnlyList<CollectionRuleNode> Children) : CollectionRuleNode
{
    public static CollectionRuleGroup All(params CollectionRuleNode[] children) =>
        new(CollectionRuleCombinator.All, children);

    public static CollectionRuleGroup Any(params CollectionRuleNode[] children) =>
        new(CollectionRuleCombinator.Any, children);
}

/// <summary>A leaf condition: one canonical fact tested by one operator against a value.</summary>
public sealed record CollectionRuleCondition(
    CollectionRuleField Field,
    CollectionRuleOperator Operator,
    string Value) : CollectionRuleNode;

/// <summary>
/// Serializes a rule tree to and from the JSON stored in <see cref="Collection.RuleJson"/>. Uses camelCase
/// enum names so the stored rule is human-legible and stable across the app and its tests.
/// </summary>
public static class CollectionRuleSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = false
    };

    public static string Serialize(CollectionRuleNode node) => JsonSerializer.Serialize(node, Options);

    public static CollectionRuleNode? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CollectionRuleNode>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
