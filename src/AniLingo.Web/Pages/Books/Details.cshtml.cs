using AniLingo.Web.Data;
using AniLingo.Web.Features.Acquisition.Sabnzbd;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Books;

public sealed class DetailsModel(
    BookCatalogService books,
    CurrentAccountContext account,
    SabnzbdDownloadService sabnzbd,
    AppDbContext db) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public BookCatalogItem? Book { get; private set; }
    public string? Error { get; private set; }
    public bool IsOwner => account.IsOwner;

    public async Task<IActionResult> OnGetAsync(
        string id,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

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
            Error = Ui["books.details.timeoutError"];
            return Page();
        }
        catch (HttpRequestException)
        {
            Error = Ui["books.details.unavailableError"];
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
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (!account.IsOwner)
        {
            return Forbid();
        }

        var title = (await books.GetAsync(
            id,
            cancellationToken))?.Title
            ?? ui["books.index.defaultBookName"];

        try
        {
            var outcome = await sabnzbd.SubmitUrlAsync(
                new SabnzbdSubmission(
                    BookInboxImport.SabnzbdDownloadKind,
                    "SABnzbd download",
                    title,
                    account.ProfileId,
                    SabnzbdPurpose.Books,
                    JobName: title),
                SabnzbdDownloadService.ParseNzbUrl(nzbUrl),
                cancellationToken);
            TempData["Status"] = outcome.Message;
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
