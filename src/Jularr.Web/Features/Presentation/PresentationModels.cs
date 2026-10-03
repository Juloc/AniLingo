namespace Jularr.Web.Features.Presentation;

/// <summary>
/// Media types a presentation grouping can apply to (#524, epic #510). Kept deliberately generic so
/// the same grouping mechanism serves anime episodes and reading (manga/novel) volumes and
/// chapters without changing their underlying item identities.
/// </summary>
public enum PresentationMediaType
{
    Anime,
    Novel,
    Manga,
    Book
}

/// <summary>Storage keys and parsing for <see cref="PresentationMediaType"/> (match the migration CHECK).</summary>
public static class PresentationMediaTypes
{
    public static string ToStorage(PresentationMediaType mediaType) => mediaType switch
    {
        PresentationMediaType.Anime => "anime",
        PresentationMediaType.Novel => "novel",
        PresentationMediaType.Manga => "manga",
        PresentationMediaType.Book => "book",
        _ => throw new ArgumentOutOfRangeException(nameof(mediaType), mediaType, null)
    };

    public static PresentationMediaType Parse(string value) => value switch
    {
        "anime" => PresentationMediaType.Anime,
        "novel" => PresentationMediaType.Novel,
        "manga" => PresentationMediaType.Manga,
        "book" => PresentationMediaType.Book,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown presentation media type.")
    };
}

/// <summary>
/// One inclusive range of internal unit numbers (episode <c>Number</c>, chapter number, volume
/// number) that belongs to a group. Stored as-is; the constructor is tolerant of a reversed pair so
/// the low/high accessors are always meaningful.
/// </summary>
public sealed record PresentationRange(int StartUnit, int EndUnit)
{
    public int Low => Math.Min(StartUnit, EndUnit);

    public int High => Math.Max(StartUnit, EndUnit);

    public bool Contains(int unit) => unit >= Low && unit <= High;
}

/// <summary>
/// A named presentation group for one work: a free-text label plus an ordered list of internal-unit
/// ranges. It never alters episode identity, file paths or provider mappings — it is derived display
/// state over the stable internal identity.
/// </summary>
public sealed record PresentationGroup(
    Guid Id,
    PresentationMediaType MediaType,
    Guid WorkId,
    string Name,
    int SortOrder,
    IReadOnlyList<PresentationRange> Ranges,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>
/// A group as submitted by the editor for an atomic replace-all save. Identity, timestamps and
/// sort order are assigned by the store from list position, so the editor only supplies the label
/// and its ranges.
/// </summary>
public sealed record PresentationGroupDraft(string Name, IReadOnlyList<PresentationRange> Ranges);
