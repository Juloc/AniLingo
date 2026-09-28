namespace Jularr.Web.Features.Presentation;

/// <summary>
/// One rendered section of a work's item list: a heading plus the items that fall under it, in the
/// item order they were given. <see cref="IsFallback"/> marks the trailing bucket of items that no
/// group claimed, so callers can label it distinctly (never with an eyebrow or accent stripe).
/// </summary>
public sealed record PresentationSection<T>(string Name, bool IsFallback, IReadOnlyList<T> Items);

/// <summary>
/// Derives the display grouping for a work from its presentation groups and its ordered items. This
/// is the single source of truth shared by the consumer detail page and the owner editor's preview,
/// so the preview always matches what will render. It is media-type-agnostic: the caller supplies
/// the unit number for each item (episode <c>Number</c>, chapter/volume number).
/// </summary>
public static class PresentationGrouping
{
    /// <summary>
    /// Assigns each item to the first group (in group order) whose ranges contain the item's unit
    /// number; items no group claims fall into a trailing fallback section. Returns an empty list
    /// when there are no groups OR when no group matched any item — in both cases the caller renders
    /// the plain, ungrouped list exactly as before.
    /// </summary>
    public static IReadOnlyList<PresentationSection<T>> Arrange<T>(
        IReadOnlyList<PresentationGroup> groups,
        IReadOnlyList<T> items,
        Func<T, int> unitOf,
        string fallbackName)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(unitOf);

        if (groups.Count == 0)
        {
            return [];
        }

        var claimed = new bool[items.Count];
        var sections = new List<PresentationSection<T>>();

        foreach (var group in groups.OrderBy(x => x.SortOrder))
        {
            var picked = new List<T>();
            for (var i = 0; i < items.Count; i++)
            {
                if (claimed[i])
                {
                    continue;
                }

                if (group.Ranges.Any(range => range.Contains(unitOf(items[i]))))
                {
                    picked.Add(items[i]);
                    claimed[i] = true;
                }
            }

            if (picked.Count > 0)
            {
                sections.Add(new PresentationSection<T>(group.Name, false, picked));
            }
        }

        if (sections.Count == 0)
        {
            // Groups exist but describe ranges no current item falls into: render plainly.
            return [];
        }

        var leftover = new List<T>();
        for (var i = 0; i < items.Count; i++)
        {
            if (!claimed[i])
            {
                leftover.Add(items[i]);
            }
        }

        if (leftover.Count > 0)
        {
            sections.Add(new PresentationSection<T>(fallbackName, true, leftover));
        }

        return sections;
    }
}
