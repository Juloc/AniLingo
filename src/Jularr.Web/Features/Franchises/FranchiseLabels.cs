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
        "compilation"
    };

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
}
