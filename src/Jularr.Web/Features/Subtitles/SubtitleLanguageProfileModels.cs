using Jularr.Web.Features.Watchlist;

namespace Jularr.Web.Features.Subtitles;

/// <summary>
/// An owner-managed, ordered wishlist of subtitle languages (Bazarr's "language profile"). Items
/// are tried in <see cref="SubtitleLanguageProfileItem.SortOrder"/> order; <see cref="CutoffPosition"/>
/// marks the point past which Jularr stops requiring/searching further languages once something at
/// or above that priority is satisfied.
/// </summary>
public sealed class SubtitleLanguageProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";

    /// <summary>
    /// 0-based index into the profile's ordered items. Once the best (lowest-index) satisfied item
    /// is at or before this position, the profile counts as complete even if lower-priority items
    /// remain unmet. Null means every listed item must be satisfied (no early stop).
    /// </summary>
    public int? CutoffPosition { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>One wanted (language, forced, SDH) combination inside a <see cref="SubtitleLanguageProfile"/>.</summary>
public sealed class SubtitleLanguageProfileItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }

    /// <summary>BCP-47-ish tag matched via <see cref="SubtitleLanguageAliases"/>, e.g. "en", "de", "ja".</summary>
    public string LanguageTag { get; set; } = "";

    public bool Forced { get; set; }
    public bool Sdh { get; set; }

    /// <summary>Priority within the profile; 0 is searched/required first.</summary>
    public int SortOrder { get; set; }
}

/// <summary>
/// Assigns a <see cref="SubtitleLanguageProfile"/> to a scope: a specific media type, a specific
/// library root, both, or neither (the global default). Resolution tries the most specific scope
/// first and falls back to the global default; see <see cref="SubtitleLanguageProfileService.ResolveAsync"/>.
/// There is at most one row per distinct (MediaType, LibraryRootId) pair; that uniqueness is
/// enforced by the service, not by a database constraint, because SQLite treats every NULL in a
/// unique index as distinct and would otherwise allow duplicate global-default rows.
/// </summary>
public sealed class SubtitleProfileAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Null applies to every media type.</summary>
    public WatchlistMediaType? MediaType { get; set; }

    /// <summary>Null applies to every library root.</summary>
    public Guid? LibraryRootId { get; set; }

    public Guid ProfileId { get; set; }
}

public sealed record SubtitleLanguageProfileItemInput(string LanguageTag, bool Forced, bool Sdh);

public sealed record SubtitleLanguageProfileDetail(
    Guid Id,
    string Name,
    int? CutoffPosition,
    IReadOnlyList<SubtitleLanguageProfileItem> Items,
    bool IsGlobalDefault,
    int AssignmentCount);
