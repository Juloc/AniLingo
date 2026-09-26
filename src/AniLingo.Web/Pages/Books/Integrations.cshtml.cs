using AniLingo.Web.Features.Acquisition.Sabnzbd;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Books;

public sealed class IntegrationsModel(
    BookCatalogService books,
    CurrentAccountContext account,
    SabnzbdConnectionResolver sabnzbd,
    IConfiguration configuration) : PageModel
{
    public BookIntegrationSettings Settings { get; private set; } =
        BookIntegrationSettings.Empty;

    public bool SabConfigured { get; private set; }
    public string? SabBooksCategory { get; private set; }
    public bool InboxConfigured => books.IsInboxConfigured;

    public bool InboxEnvironmentOverride =>
        !string.IsNullOrWhiteSpace(
            configuration["Books:InboxPath"]);

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        Settings = BookIntegrationSettingsStore.Load();
        var resolved = await sabnzbd.ResolveAsync(cancellationToken);
        SabConfigured = resolved.IsConfigured;
        SabBooksCategory = resolved.Effective.BooksCategory;
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(
        string? inboxPath,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        try
        {
            await BookIntegrationSettingsStore.SaveAsync(
                new BookIntegrationSettings(inboxPath),
                cancellationToken);
            TempData["Status"] = "Books acquisition settings saved.";
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
}
