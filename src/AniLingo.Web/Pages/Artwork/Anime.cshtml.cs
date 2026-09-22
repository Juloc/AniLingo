using AniLingo.Web.Features.Artwork;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Artwork;

public sealed class AnimeModel : PageModel
{
    public IActionResult OnGet(Guid id, string kind)
    {
        var artworkKind = kind.Equals("poster", StringComparison.OrdinalIgnoreCase)
            ? AnimeArtworkKind.Poster
            : kind.Equals("fanart", StringComparison.OrdinalIgnoreCase)
                ? AnimeArtworkKind.Fanart
                : (AnimeArtworkKind?)null;

        if (artworkKind is null)
        {
            return NotFound();
        }

        var path = AnimeArtworkStore.FindPath(id, artworkKind.Value);
        if (path is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        return PhysicalFile(
            path,
            AnimeArtworkStore.GetContentType(path));
    }
}
