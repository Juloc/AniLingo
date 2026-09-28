using Jularr.Web.Features.Books;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Books;

public sealed class CoverModel(
    BookCatalogService books) : PageModel
{
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
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
