using Jularr.Web.Ui;

namespace Jularr.Web.Features.MediaFacts;

/// <summary>
/// What a media-facts language row describes: the spoken audio track, a subtitle track, or the
/// reading/original text itself (manga pages, novel chapters, book editions). Kept distinct from
/// <see cref="Jularr.Web.Features.Library.MediaStreamKind"/> because it also covers non-video
/// media that never has an audio/subtitle stream at all.
/// </summary>
public enum MediaFactsLanguageUsage
{
    Audio,
    Subtitle,
    Text
}

/// <summary>
/// How completely one language is covered across a work's units (episodes, chapters, known
/// editions). <see cref="MediaFactsLanguageRow.Coverage"/> derives this from the row's own counts
/// so it can never disagree with them.
/// </summary>
public enum MediaFactsCoverage
{
    /// <summary>No unit count is known; completeness cannot be judged.</summary>
    Unknown,
    /// <summary>The language was observed nowhere among the known units.</summary>
    None,
    /// <summary>The language covers some, but not all, known units.</summary>
    Partial,
    /// <summary>The language covers every known unit.</summary>
    Complete
}

/// <summary>
/// One observed language's availability for a work: how many of its units (episodes for anime,
/// chapters for manga/light novels, known catalog editions for books) actually carry that
/// language, out of how many units are known in total. Never fabricated: a row only exists for a
/// language actually found in local media inventory, subtitle tracks, chapter text/translations,
/// or catalog edition metadata (#426).
/// </summary>
public sealed record MediaFactsLanguageRow(
    string Language,
    MediaFactsLanguageUsage Usage,
    int AvailableUnits,
    int TotalUnits)
{
    public MediaFactsCoverage Coverage => TotalUnits <= 0
        ? MediaFactsCoverage.Unknown
        : AvailableUnits <= 0
            ? MediaFactsCoverage.None
            : AvailableUnits >= TotalUnits
                ? MediaFactsCoverage.Complete
                : MediaFactsCoverage.Partial;
}

/// <summary>
/// The canonical per-work facts projection (#426): status, counts, runtime and release year where
/// known, plus language availability. One shape reused by every media type so consumers (detail
/// pages today; Library/Discover/Search/Smart Collections later) never rebuild this themselves.
/// Every field is optional and renders only when present; nothing here is ever invented.
/// </summary>
/// <param name="Kind">The media type this projection describes.</param>
/// <param name="Status">Ongoing/finished/upcoming/hiatus/cancelled, from matched provider metadata.</param>
/// <param name="PrimaryUnitCount">Episodes (anime) or chapters (manga/light novels); null for books.</param>
/// <param name="SecondaryUnitCount">Seasons (anime) or volumes (manga/light novels); null for books.</param>
/// <param name="RuntimeMinutes">Per-episode runtime in minutes, when a provider states one.</param>
/// <param name="ReleaseYear">The work's release/start year, when known.</param>
/// <param name="Languages">Audio/subtitle/text language availability; empty when nothing is known.</param>
public sealed record MediaFacts(
    MediaBannerKind Kind,
    MediaReleaseStatus? Status,
    int? PrimaryUnitCount,
    int? SecondaryUnitCount,
    int? RuntimeMinutes,
    int? ReleaseYear,
    IReadOnlyList<MediaFactsLanguageRow> Languages)
{
    public bool HasContent =>
        Status is not null
        || PrimaryUnitCount is not null
        || SecondaryUnitCount is not null
        || RuntimeMinutes is not null
        || ReleaseYear is not null
        || Languages.Count > 0;

    public static MediaFacts Empty(MediaBannerKind kind) => new(kind, null, null, null, null, null, []);
}
