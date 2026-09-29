using Jularr.Web.Features.MediaCore;

namespace Jularr.Web.Features.Collections;

/// <summary>
/// Resolves the detail-page URL of a media-core work from its legacy source bridge (#427). A collection
/// holds universal <see cref="Work"/>s, but each is shown on the existing per-type detail page of the record
/// it bridges to, so cards deep-link there instead of to a route that does not exist.
/// </summary>
public static class CollectionWorkLinks
{
    public static string? Resolve(WorkSourceKind sourceKind, Guid sourceId) => sourceKind switch
    {
        WorkSourceKind.Anime => $"/Library/Anime/{sourceId}",
        WorkSourceKind.MangaSeries => $"/Manga/Series/{sourceId}",
        WorkSourceKind.NovelWork => $"/Novels/Work/{sourceId}",
        WorkSourceKind.BookEdition => $"/Books/{sourceId}",
        _ => null
    };
}
