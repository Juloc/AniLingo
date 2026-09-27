using Jularr.Web.Features.Books;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Net.Http.Headers;

namespace Jularr.Web.Pages.Books;

/// <summary>
/// The stored file of a Books work (the PDF of a PDF book) for the Books reader, which renders
/// it itself. Signed-in profiles only (the fallback policy); range requests let the reader load
/// pages on demand.
/// </summary>
public sealed class FileModel(BookCatalogService books) : PageModel
{
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await books.GetStoredFileAsync(id, cancellationToken) is not { } file)
        {
            return NotFound();
        }

        var disposition = new ContentDispositionHeaderValue("inline");
        disposition.SetHttpFileName(file.FileName);
        Response.Headers.ContentDisposition = disposition.ToString();
        Response.Headers.CacheControl = "private, max-age=86400";
        return new PhysicalFileResult(file.Path, file.MediaType)
        {
            EnableRangeProcessing = true,
            LastModified = System.IO.File.GetLastWriteTimeUtc(file.Path)
        };
    }
}
