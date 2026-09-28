using Jularr.Web.Features.Localization;

namespace Jularr.Web.Features.Franchises;

public static class FranchiseLabels
{
    // AniList names the relation of the related work to the listing work: a SEQUEL edge from A
    // to B means B is the sequel to A. CONTAINS reads both ways on AniList, so it gets no label.
    private static readonly HashSet<string> KnownRelations = new(StringComparer.Ordinal)
    {
        "adaptation",
        "source",
        "prequel",
        "sequel",
        "parent",
        "side-story",
        "spin-off",
        "alternative",
        "summary",
        "compilation",
        "remake"
    };

    /// <summary>
    /// The compact "Related / Franchise" grouping a stored or provider relation type falls into,
    /// coarser than <see cref="RelationKey"/>'s per-card sentence. Unknown or unlabelled types
    /// (including AniList's CONTAINS, which reads both ways and names no specific relation) fall
    /// back to "same franchise" rather than being dropped.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> RelationGroups =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["adaptation"] = "franchise.group.adaptation",
            ["source"] = "franchise.group.adaptation",
            ["sequel"] = "franchise.group.sequelPrequel",
            ["prequel"] = "franchise.group.sequelPrequel",
            ["side-story"] = "franchise.group.sideStory",
            ["parent"] = "franchise.group.sideStory",
            ["spin-off"] = "franchise.group.spinOff",
            ["alternative"] = "franchise.group.alternative",
            ["summary"] = "franchise.group.alternative",
            ["compilation"] = "franchise.group.alternative",
            ["remake"] = "franchise.group.alternative"
        };

    /// <summary>The fallback bucket: same franchise, but no specific typed edge to that work.</summary>
    public const string SameFranchiseGroupKey = "franchise.group.sameFranchise";

    /// <summary>Render order of the "Related / Franchise" section's groups.</summary>
    public static readonly IReadOnlyList<string> RelationGroupOrder =
    [
        "franchise.group.adaptation",
        "franchise.group.sequelPrequel",
        "franchise.group.sideStory",
        "franchise.group.spinOff",
        "franchise.group.alternative",
        SameFranchiseGroupKey
    ];

    /// <summary>The franchise title, or a placeholder until the first refresh has read it.</summary>
    public static string Title(UiTextBundle ui, string? title) =>
        string.IsNullOrWhiteSpace(title) ? ui["franchise.pendingTitle"] : title;

    /// <summary>
    /// The UI key ("Sequel to {title}") of a provider relation type (SIDE_STORY or side-story), or
    /// null when it has no label.
    /// </summary>
    public static string? RelationKey(string? relationType)
    {
        if (string.IsNullOrWhiteSpace(relationType))
        {
            return null;
        }

        var normalized = MediaRelationStore.NormalizeRelationType(relationType);
        return KnownRelations.Contains(normalized) ? $"franchise.relation.{normalized}" : null;
    }

    /// <summary>The group a relation type belongs to; never null, defaults to "same franchise".</summary>
    public static string RelationGroupKey(string? relationType)
    {
        if (string.IsNullOrWhiteSpace(relationType))
        {
            return SameFranchiseGroupKey;
        }

        var normalized = MediaRelationStore.NormalizeRelationType(relationType);
        return RelationGroups.GetValueOrDefault(normalized, SameFranchiseGroupKey);
    }
}
