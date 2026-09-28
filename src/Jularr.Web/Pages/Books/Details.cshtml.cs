using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Sabnzbd;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.MediaFacts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Books;

public sealed class DetailsModel(
    BookCatalogService books,
    CurrentAccountContext account,
    SabnzbdDownloadService sabnzbd,
    AppDbContext db,
    ILogger<DetailsModel> logger) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public BookCatalogItem? Book { get; private set; }
    public string? Error { get; private set; }
    public bool IsOwner => account.IsOwner;

    /// <summary>
    /// Language availability across this work's known catalog editions (#426). The page already
    /// shows its own release year, so this only ever renders the language chips (showFacts: false)
    /// -- never a second, differently-worded copy of the same fact.
    /// </summary>
    public MediaFactsStripModel? Facts { get; private set; }

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
            if (Book is null)
            {
                return NotFound();
            }

            Facts = MediaFactsStripModel.Create(
                MediaFactsService.CreateBookCatalogFacts(Book),
                Ui,
                showFacts: false);
            return Page();
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

        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

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
            logger.LogError(exception, "Acquiring catalog book {BookId} failed", id);
            TempData["Status"] = Ui["books.details.acquireFailed"];
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
                    Jularr.Web.Features.Acquisition.Import.CompletedDownloadImportService.ManualDownloadOperationKind,
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
            logger.LogError(exception, "Submitting SABnzbd download for book {BookId} failed", id);
            TempData["Status"] = ui["books.details.sabSubmitFailed"];
        }

        return RedirectToPage(new { id });
    }
}
