using AniLingo.Web.Features.Books;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Books;

public sealed class IndexModel(BookCatalogService books) : PageModel
{
    public string Query { get; private set; } = "";
    public IReadOnlyList<BookCatalogItem> Books { get; private set; } = [];
    public string? Error { get; private set; }

    public async Task OnGetAsync(
        string? q,
        CancellationToken cancellationToken)
    {
        Query = q?.Trim() ?? "";

        try
        {
            Books = await books.SearchAsync(Query, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Error = "Book search timed out. Please try again.";
        }
        catch (HttpRequestException)
        {
            Error = "Book search is temporarily unavailable. Please try again.";
        }
        catch (InvalidOperationException exception)
        {
            Error = exception.Message;
        }
    }
}
