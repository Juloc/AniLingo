using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Books;

public sealed class IntegrationsModel(
    BookCatalogService books,
    CurrentAccountContext account,
    IConfiguration configuration) : PageModel
{
    public BookIntegrationSettings Settings { get; private set; } =
        BookIntegrationSettings.Empty;

    public bool HasSavedApiKey =>
        !string.IsNullOrWhiteSpace(Settings.SabnzbdApiKey);

    public bool SabConfigured => books.IsSabnzbdConfigured;
    public bool InboxConfigured => books.IsInboxConfigured;

    public bool SabEnvironmentOverride =>
        !string.IsNullOrWhiteSpace(
            configuration["Books:SABnzbd:BaseUrl"])
        || !string.IsNullOrWhiteSpace(
            configuration["Books:SABnzbd:ApiKey"]);

    public bool InboxEnvironmentOverride =>
        !string.IsNullOrWhiteSpace(
            configuration["Books:InboxPath"]);

    public IActionResult OnGet()
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        Settings = BookIntegrationSettingsStore.Load();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(
        string? sabBaseUrl,
        string? sabApiKey,
        string? sabCategory,
        string? inboxPath,
        bool clearApiKey,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        var existing = BookIntegrationSettingsStore.Load();
        var effectiveApiKey = clearApiKey
            ? null
            : string.IsNullOrWhiteSpace(sabApiKey)
                ? existing.SabnzbdApiKey
                : sabApiKey;

        try
        {
            await BookIntegrationSettingsStore.SaveAsync(
                new BookIntegrationSettings(
                    sabBaseUrl,
                    effectiveApiKey,
                    sabCategory,
                    inboxPath),
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

    public async Task<IActionResult> OnPostTestSabAsync(
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        try
        {
            var message = await books.TestSabnzbdAsync(
                cancellationToken);
            TempData["Status"] = message;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or HttpRequestException
                or TaskCanceledException)
        {
            TempData["Status"] =
                "SABnzbd connection failed: " + exception.Message;
        }

        return RedirectToPage();
    }
}
