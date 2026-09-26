using AniLingo.Web.Features.Novels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Novels;

/// <summary>
/// Serves cached EPUB volume covers and illustrations. Asset names are
/// content-addressed raster files, so responses are immutable.
/// </summary>
public sealed class AssetModel(NovelVolumeAssetStore assets) : PageModel
{
    public IActionResult OnGet(Guid volumeId, string asset)
    {
        var path = assets.Resolve(volumeId, asset);
        if (path is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        Response.Headers.XContentTypeOptions = "nosniff";
        return PhysicalFile(path, NovelVolumeAssetStore.ContentType(asset));
    }
}
