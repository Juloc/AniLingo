namespace Jularr.Web.Features.Tv;

/// <summary>
/// A TV series as a first-class media type (#594). A series is a structured unit: its universal identity,
/// titles, external provider ids and its seasons/episodes live in the media core (a
/// <see cref="Jularr.Web.Features.MediaCore.Work"/> of media type Series, bridged by
/// <c>WorkSourceKind.Series</c>, whose structure reuses <c>WorkSeason</c>/<c>WorkEpisode</c>). This
/// per-type row is the stable anchor the series library and playback features build on.
/// </summary>
public sealed class TvSeries
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stable de-duplication key (folded title + year) so a re-import resolves the same series.</summary>
    public string Key { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>First-air year when known; part of <see cref="Key"/>.</summary>
    public int? Year { get; set; }

    /// <summary>TMDB id when known; the primary external identity is also mirrored into the media core.</summary>
    public string? TmdbId { get; set; }

    /// <summary>TheTVDB id when known.</summary>
    public string? TvdbId { get; set; }

    /// <summary>The library folder the series' files were placed in; null when imported in place.</summary>
    public string? LibraryPath { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
