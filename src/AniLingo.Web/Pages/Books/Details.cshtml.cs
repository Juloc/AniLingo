using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Books;

public sealed class DetailsModel(
    BookCatalogService books,
    CurrentAccountContext account) : PageModel
{
    public BookCatalogItem? Book { get; private set; }
    public string? Error { get; private set; }
    public bool IsOwner => account.IsOwner;

    public async Task<IActionResult> OnGetAsync(
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            Book = await books.GetAsync(
                id,
                cancellationToken);
            return Book is null
                ? NotFound()
                : Page();
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Error = "The catalog request timed out. Please try again.";
            return Page();
        }
        catch (HttpRequestException)
        {
            Error = "The catalog is temporarily unavailable.";
            return Page();
        }
    }

    public async Task<IActionResult> OnPostAcquireAsync(
        string id,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        try
        {
            var workId = await books.AcquireCatalogBookAsync(
                id,
                cancellationToken);
            return RedirectToPage(
                "/Books/Library",
                new { id = workId, lang = "id" });
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or HttpRequestException
                or TaskCanceledException)
        {
            TempData["Status"] = exception.Message;
            return RedirectToPage(new { id });
        }
    }

    public async Task<IActionResult> OnPostSabUrlAsync(
        string id,
        string? nzbUrl,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        var title = (await books.GetAsync(
            id,
            cancellationToken))?.Title;

        try
        {
            var result = await books.QueueSabnzbdUrlAsync(
                nzbUrl ?? "",
                title,
                cancellationToken);
            TempData["Status"] = result.Message;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or HttpRequestException
                or TaskCanceledException)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage(new { id });
    }
}
