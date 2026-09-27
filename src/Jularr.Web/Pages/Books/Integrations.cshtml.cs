using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.DownloadClients;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.Localization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Books;

public sealed class IntegrationsModel(
    BookCatalogService books,
    CurrentAccountContext account,
    DownloadClientStore downloadClients,
    IConfiguration configuration,
    AppDbContext db,
    IDataProtectionProvider dataProtectionProvider) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public BookIntegrationSettings Settings { get; private set; } =
        BookIntegrationSettings.Empty;

    public bool IsOwner => account.IsOwner;
    public bool SabConfigured { get; private set; }
    public string? SabBooksCategory { get; private set; }
    public bool InboxConfigured => books.IsInboxConfigured;
    public StoredHardcoverAccount? HardcoverAccount { get; private set; }

    [BindProperty]
    public string HardcoverAccessToken { get; set; } = "";

    public bool InboxEnvironmentOverride =>
        !string.IsNullOrWhiteSpace(
            configuration["Books:InboxPath"]);

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        HardcoverAccount = await new BookHardcoverAccountStore(
                dataProtectionProvider)
            .LoadAsync(account.ProfileId, cancellationToken);

        if (account.IsOwner)
        {
            Settings = BookIntegrationSettingsStore.Load();
            var entry = (await downloadClients.LoadAllAsync(cancellationToken))
                .Where(item => item.Type == DownloadClientType.Sabnzbd)
                .OrderBy(item => item.Priority)
                .FirstOrDefault();
            SabConfigured = entry?.Enabled == true;
            SabBooksCategory = entry?.Settings.BooksCategory;
        }

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

    /// <summary>Connects the current profile's own Hardcover account (never another profile's).</summary>
    public async Task<IActionResult> OnPostConnectHardcoverAsync(
        CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        try
        {
            var viewer = await books.ValidateHardcoverTokenAsync(
                HardcoverAccessToken,
                cancellationToken);
            await new BookHardcoverAccountStore(dataProtectionProvider)
                .SaveAsync(
                    account.ProfileId,
                    new StoredHardcoverAccount(
                        viewer.UserId,
                        viewer.Username,
                        BookCatalogService.NormalizeHardcoverToken(HardcoverAccessToken),
                        DateTimeOffset.UtcNow),
                    cancellationToken);
            TempData["Status"] = ui.Format(
                "books.integrations.hardcoverConnected",
                ("username", viewer.Username));
        }
        catch (HardcoverConnectionException)
        {
            TempData["Status"] = ui["books.integrations.hardcoverRejected"];
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested
            && exception is HttpRequestException
                or TaskCanceledException
                or System.Text.Json.JsonException)
        {
            TempData["Status"] = ui["books.integrations.hardcoverUnreachable"];
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            TempData["Status"] = ui["books.integrations.hardcoverSaveFailed"];
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDisconnectHardcoverAsync(
        CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        await new BookHardcoverAccountStore(dataProtectionProvider)
            .DisconnectAsync(account.ProfileId, cancellationToken);
        TempData["Status"] = ui["books.integrations.hardcoverDisconnected"];
        return RedirectToPage();
    }

}
