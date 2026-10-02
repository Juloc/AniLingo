using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Admin;

/// <summary>
/// Admin manual search for one Book request. Results are always generated from the request's
/// persisted identity; POST only carries the opaque release identity and never a download URL.
/// </summary>
[Authorize(Policy = JularrPolicies.AdminMedia)]
public sealed class BookManualSearchModel(
    AppDbContext db,
    BookManualSearchService manualSearch,
    ILogger<BookManualSearchModel> logger) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public BookManualSearchResult? Result { get; private set; }
    public string? Notice => TempData["BookManualSearchNotice"] as string;
    public string? Error => TempData["BookManualSearchError"] as string;

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(
            HttpContext,
            db);

        try
        {
            Result = await manualSearch.SearchAsync(
                id,
                cancellationToken);
            return Page();
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(
                exception,
                "Book manual search could not open request {RequestId}.",
                id);
            return NotFound();
        }
    }

    public async Task<IActionResult> OnPostGrabAsync(
        Guid id,
        string? releaseIdentity,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(
            HttpContext,
            db);

        if (string.IsNullOrWhiteSpace(releaseIdentity))
        {
            TempData["BookManualSearchError"] =
                Ui["books.index.searchUnavailable"];
            return RedirectToPage(new { id });
        }

        try
        {
            var request = await manualSearch.GrabAsync(
                id,
                releaseIdentity,
                cancellationToken);
            TempData["Status"] = request.StatusMessage ?? request.Title;
            return RedirectToPage("/Admin/Wanted");
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(
                exception,
                "Book manual grab failed for request {RequestId}.",
                id);
            TempData["BookManualSearchError"] = exception.Message;
            return RedirectToPage(new { id });
        }
    }

    public static string Size(long? bytes) => bytes switch
    {
        null => "—",
        >= 1024L * 1024 * 1024 =>
            $"{bytes.Value / (1024d * 1024 * 1024):0.0} GB",
        >= 1024L * 1024 =>
            $"{bytes.Value / (1024d * 1024):0.0} MB",
        _ => $"{bytes.Value / 1024d:0} KB"
    };
}
