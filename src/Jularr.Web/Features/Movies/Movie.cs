namespace Jularr.Web.Features.Movies;

/// <summary>
/// A movie as a first-class media type (#593). A movie is one playable unit: its universal identity,
/// titles and external provider ids live in the media core (a <see cref="Jularr.Web.Features.MediaCore.Work"/>
/// of media type Movie, bridged by <c>WorkSourceKind.Movie</c>). This per-type row is the stable anchor
/// the movie library and playback features build on; it is never modified by a metadata refresh of the
/// core it bridges to.
/// </summary>
public sealed class Movie
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stable de-duplication key (folded title + year) so a re-import resolves the same movie.</summary>
    public string Key { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>Release year when known; part of <see cref="Key"/> so remakes stay distinct.</summary>
    public int? Year { get; set; }

    /// <summary>TMDB id when known; the primary external identity is also mirrored into the media core.</summary>
    public string? TmdbId { get; set; }

    /// <summary>IMDb id when known.</summary>
    public string? ImdbId { get; set; }

    /// <summary>The library folder the movie's files were placed in; null when imported in place.</summary>
    public string? LibraryPath { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
