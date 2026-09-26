using AniLingo.Web.Data;
using AniLingo.Web.Features.Acquisition.Sabnzbd;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Books;

public sealed class IntegrationsModel(
    BookCatalogService books,
    CurrentAccountContext account,
    SabnzbdConnectionResolver sabnzbd,
    IConfiguration configuration,
    AppDbContext db) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
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
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

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
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (!account.IsOwner)
        {
            return Forbid();
        }

        try
        {
            await BookIntegrationSettingsStore.SaveAsync(
                new BookIntegrationSettings(inboxPath),
                cancellationToken);
            TempData["Status"] = ui["books.integrations.saved"];
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
