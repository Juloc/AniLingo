namespace Jularr.Web.Features.Calendar;

/// <summary>
/// Media types of the release calendar. TV and movies are part of the model so the calendar,
/// filters and links already handle them, but no source produces them until Jularr has a TV/movie
/// backend: the calendar never shows placeholder data.
/// </summary>
public enum ReleaseMediaType
{
    Anime,
    Tv,
    Movie,
    Manga,
    LightNovel,
    Book
}

/// <summary>
/// What is released. Movie kinds stay distinct because a cinema date is not a downloadable
/// release.
/// </summary>
public enum ReleaseKind
{
    Episode,
    SeasonPremiere,
    SeriesStart,
    Chapter,
    Volume,
    Publication,
    Cinema,
    Digital,
    Streaming,
    Physical
}

/// <summary>
/// Jularr's local view of one release, taken from the canonical library, monitoring and
/// acquisition state; the calendar keeps no state of its own.
/// </summary>
public enum ReleaseLocalState
{
    /// <summary>In the library without per-release state (for example a series start).</summary>
    None,
    NotMonitored,
    Monitored,
    Wanted,
    Searching,
    Grabbed,
    Failed,
    Available,
    Missing
}

/// <param name="InLibrary">The media is part of the local library.</param>
/// <param name="Monitored">Monitoring for this release; null where monitoring does not apply.</param>
public sealed record ReleaseLocalStatus(bool InLibrary, bool? Monitored, ReleaseLocalState State)
{
    public static ReleaseLocalStatus InLibraryOnly { get; } = new(true, null, ReleaseLocalState.None);

    public bool IsAvailable => State == ReleaseLocalState.Available;
}

/// <summary>The released unit: an episode (with the local season when known), chapter or volume.</summary>
public sealed record ReleaseUnit(double Number, int? Season = null);

/// <summary>
/// One normalized release. It references the canonical media entity (<see cref="MediaId"/>, and
/// <see cref="UnitId"/> for an existing local episode/chapter/edition) instead of copying it;
/// <see cref="Title"/> and <see cref="CoverImageUrl"/> are read from that entity.
/// </summary>
public sealed record ReleaseEvent(
    string Id,
    ReleaseMediaType MediaType,
    Guid MediaId,
    Guid? UnitId,
    ReleaseKind Kind,
    string Title,
    ReleaseUnit? Unit,
    ReleaseDate Date,
    string Provider,
    string? ProviderExternalId,
    ReleaseLocalStatus Local,
    string? CoverImageUrl = null,
    string? Region = null,
    string? DetailsUrl = null)
{
    public static string BuildId(ReleaseMediaType mediaType, Guid mediaId, ReleaseKind kind, ReleaseUnit? unit, string? discriminator = null) =>
        string.Join(
            ':',
            mediaType.ToString().ToLowerInvariant(),
            mediaId.ToString("N"),
            kind.ToString().ToLowerInvariant(),
            unit is null ? "0" : $"{unit.Season ?? 0}-{unit.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            discriminator ?? "");
}

/// <summary>
/// The inclusive day range a source is asked for, in the viewer's time zone. Undated events are
/// only included when requested, for the calendar's "date not exact yet" list.
/// </summary>
public sealed record ReleaseEventQuery(
    DateOnly Start,
    DateOnly End,
    TimeZoneInfo Zone,
    DateTimeOffset Now,
    bool IncludeUndated = false,
    ReleaseMediaType? MediaType = null,
    Guid? MediaId = null,
    string? ProfileId = null)
{
    public bool Wants(ReleaseMediaType type, Guid? mediaId = null) =>
        (MediaType is null || MediaType == type) &&
        (MediaId is null || mediaId is null || MediaId == mediaId);

    public bool Includes(ReleaseDate date) =>
        date.Precision == ReleaseDatePrecision.Unknown ? IncludeUndated : date.Overlaps(Start, End, Zone);
}

/// <summary>
/// Produces release events for some media types from data Jularr already has (library tables and
/// the provider cache). Sources never call providers while a page renders.
/// </summary>
public interface IReleaseEventSource
{
    string Name { get; }

    IReadOnlyCollection<ReleaseMediaType> MediaTypes { get; }

    Task<IReadOnlyList<ReleaseEvent>> GetEventsAsync(ReleaseEventQuery query, CancellationToken cancellationToken);
}

public enum ReleaseStateFilter
{
    All,
    Monitored,
    Missing,
    Available
}

/// <summary>Media-type and library-state filter of the calendar; an empty type set means all types.</summary>
public sealed record ReleaseCalendarFilter(
    IReadOnlySet<ReleaseMediaType> MediaTypes,
    ReleaseStateFilter State,
    string? ProfileId = null)
{
    public static ReleaseCalendarFilter All { get; } = new(new HashSet<ReleaseMediaType>(), ReleaseStateFilter.All);

    public bool Matches(ReleaseEvent release, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (MediaTypes.Count > 0 && !MediaTypes.Contains(release.MediaType))
        {
            return false;
        }

        return State switch
        {
            ReleaseStateFilter.Monitored => release.Local.Monitored == true,
            ReleaseStateFilter.Available => release.Local.IsAvailable,
            ReleaseStateFilter.Missing => IsMissing(release, now, zone),
            _ => true
        };
    }

    /// <summary>Released, in the library and not available locally: what the Missing filter shows.</summary>
    public static bool IsMissing(ReleaseEvent release, DateTimeOffset now, TimeZoneInfo zone) =>
        release.Local.InLibrary &&
        release.Local.State is ReleaseLocalState.Missing or ReleaseLocalState.Wanted or ReleaseLocalState.Searching or ReleaseLocalState.Failed &&
        release.Date.IsReleased(now, zone);
}
