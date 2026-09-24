using AniLingo.Web.Features.Books;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Books;

public sealed class CoverModel(
    BookCatalogService books) : PageModel
{
    public IActionResult OnGet(Guid id)
    {
        var path = books.GetLocalCoverPath(id);
        if (path is null || !System.IO.File.Exists(path))
        {
            return NotFound();
        }

        return PhysicalFile(
            path,
            BookCatalogService.GetCoverContentType(path),
            enableRangeProcessing: true);
    }
}
