using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Artwork;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Novels;

/// <summary>
/// Serves a Light Novel series' cover kept beside its EPUBs on the Light Novel library root as a
/// small locally cached WebP thumbnail (#581/#570), so library rows keep rendering while the NAS is
/// offline.
/// </summary>
public sealed class CoverModel(ReadingCoverArtwork covers) : PageModel
{
    public async Task<IActionResult> OnGetAsync(Guid id, int? w, CancellationToken cancellationToken)
    {
        var thumbnail = await covers.GetThumbnailAsync(MediaAcquisitionKind.LightNovel, id, w, cancellationToken);
        if (thumbnail is null || !System.IO.File.Exists(thumbnail.Path))
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private,max-age=604800";
        return PhysicalFile(thumbnail.Path, thumbnail.MediaType);
    }
}
