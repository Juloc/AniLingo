using Jularr.Web.Features.Artwork;
using Jularr.Web.Features.Books;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Books;

public sealed class CoverModel(
    BookCatalogService books,
    BesideMediaArtworkCache artworkCache) : PageModel
{
    public async Task<IActionResult> OnGetAsync(Guid id, int? w, CancellationToken cancellationToken)
    {
        // A requested width serves a small, locally cached WebP thumbnail (#570): generated once
        // from the canonical cover and then served from /data, so library rows keep rendering even
        // when the NAS is offline. Without a width the full canonical cover is served.
        if (w is int width and > 0)
        {
            var thumbnail = await artworkCache.GetThumbnailForSourceAsync(
                $"book-{id:N}",
                () => books.GetLocalCoverPathAsync(id, knownStoragePath: null, cancellationToken),
                width,
                cancellationToken);

            if (thumbnail is null || !System.IO.File.Exists(thumbnail.Path))
            {
                return NotFound();
            }

            Response.Headers.CacheControl = "public,max-age=604800";
            return PhysicalFile(thumbnail.Path, thumbnail.MediaType);
        }

        var path = await books.GetLocalCoverPathAsync(id, knownStoragePath: null, cancellationToken);
        if (path is null || !System.IO.File.Exists(path))
        {
            return NotFound();
        }

        return PhysicalFile(
            path,
            BookCatalogService.GetCoverContentType(path));
    }
}
