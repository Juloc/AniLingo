using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Books;

public sealed class SourcesModel(
    BookCatalogService books,
    CurrentAccountContext account) : PageModel
{
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

            TempData["Status"] =
                "OPDS source saved.";
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
            TempData["Status"] =
                "OPDS source was not found.";
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
            ? $"{source.Name} enabled."
            : $"{source.Name} disabled.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync(
        string? sourceId,
        CancellationToken cancellationToken)
    {
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
                "OPDS connection failed: "
                + exception.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(
        string? sourceId,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        try
        {
            await books.RemoveOpdsSourceAsync(
                sourceId ?? "",
                cancellationToken);
            TempData["Status"] =
                "OPDS source removed.";
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
                "OPDS import failed: "
                + exception.Message;

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
