namespace Jularr.Web.Features.Search;

/// <summary>
/// Where a search result opens. The one place that maps a source record to its consumer page, so
/// adding a media type's page (movies, series, audiobooks have none yet) is a one-line change here
/// and the search page never links to a route that does not exist.
/// </summary>
public static class MediaSearchTargets
{
    public static string? Href(MediaSearchType type, Guid id) => type switch
    {
        MediaSearchType.Anime => $"/Library/Anime/{id:D}",
        MediaSearchType.Manga => $"/Manga/Series/{id:D}",
        MediaSearchType.Novel => $"/Novels/Work/{id:D}",
        MediaSearchType.Book => $"/Books/Library/{id:D}",
        _ => null
    };

    public static string FranchiseHref(Guid franchiseId) => $"/Franchises/{franchiseId:D}";

    /// <summary>
    /// The page of a result: the franchise page for a franchise, otherwise the page of the best
    /// variant that has one (a movie that is also an anime opens the anime), or null when none does.
    /// </summary>
    public static string? Href(MediaSearchResult result) =>
        result.Kind == MediaSearchResultKind.Franchise
            ? FranchiseHref(result.Id)
            : result.Variants.Select(variant => Href(variant.Type, variant.Id)).FirstOrDefault(href => href is not null);
}
