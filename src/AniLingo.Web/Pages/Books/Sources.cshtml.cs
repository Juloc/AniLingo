using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Books;

public sealed class SourcesModel(
    BookCatalogService books,
    CurrentAccountContext account,
    AppDbContext db) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public IReadOnlyList<BookOpdsSourceSettings> Sources { get; private set; } = [];
    public IReadOnlyList<BookOpdsCatalogItem> Results { get; private set; } = [];
    public string Query { get; private set; } = "";
    public string? SelectedSourceId { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        string? q,
        string? source,
        bool search,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (!account.IsOwner)
        {
            return Forbid();
        }

        Sources = books.GetOpdsSources();
        Query = q?.Trim() ?? "";
        SelectedSourceId = source?.Trim();

        if (search)
        {
            Results = await books.SearchOpdsAsync(
                SelectedSourceId,
                Query,
                cancellationToken);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(
        string? name,
        string? url,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (!account.IsOwner)
        {
            return Forbid();
        }

        try
        {
            await books.SaveOpdsSourceAsync(
                id: null,
                name: name ?? "",
                url: url ?? "",
                username,
                password,
                isEnabled: true,
                preserveExistingPassword: false,
                cancellationToken);

            TempData["Status"] = ui["books.sources.saved"];
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or IOException
                or UnauthorizedAccessException)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(
        string? sourceId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (!account.IsOwner)
        {
            return Forbid();
        }

        var source = books.GetOpdsSources()
            .FirstOrDefault(x =>
                x.Id.Equals(
                    sourceId?.Trim(),
                    StringComparison.OrdinalIgnoreCase));

        if (source is null)
        {
            TempData["Status"] = ui["books.sources.notFound"];
            return RedirectToPage();
        }

        await books.SaveOpdsSourceAsync(
            source.Id,
            source.Name,
            source.Url,
            source.Username,
            source.Password,
            enabled,
            preserveExistingPassword: false,
            cancellationToken);

        TempData["Status"] = enabled
            ? ui.Format("books.sources.sourceEnabled", ("name", source.Name))
            : ui.Format("books.sources.sourceDisabled", ("name", source.Name));

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync(
        string? sourceId,
        CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (!account.IsOwner)
        {
            return Forbid();
        }

        try
        {
            TempData["Status"] =
                await books.TestOpdsSourceAsync(
                    sourceId ?? "",
                    cancellationToken);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or HttpRequestException
                or TaskCanceledException
                or System.Text.Json.JsonException
                or System.Xml.XmlException)
        {
            TempData["Status"] =
                ui.Format("books.sources.testFailed", ("message", exception.Message));
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(
        string? sourceId,
        CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (!account.IsOwner)
        {
            return Forbid();
        }

        try
        {
            await books.RemoveOpdsSourceAsync(
                sourceId ?? "",
                cancellationToken);
            TempData["Status"] = ui["books.sources.removed"];
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or IOException
                or UnauthorizedAccessException)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostImportAsync(
        string? sourceId,
        string? q,
        string? bookKey,
        CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (!account.IsOwner)
        {
            return Forbid();
        }

        try
        {
            var workId = await books.ImportOpdsBookAsync(
                sourceId ?? "",
                q,
                bookKey ?? "",
                cancellationToken);

            return RedirectToPage(
                "/Books/Library",
                new
                {
                    id = workId,
                    lang = "id"
                });
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or HttpRequestException
                or TaskCanceledException
                or System.Text.Json.JsonException
                or System.Xml.XmlException)
        {
            TempData["Status"] =
                ui.Format("books.sources.importFailed", ("message", exception.Message));

            return RedirectToPage(
                new
                {
                    q,
                    source = sourceId,
                    search = true
                });
        }
    }
}
