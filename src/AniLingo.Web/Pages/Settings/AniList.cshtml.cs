using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Tracking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings;

public sealed class AniListModel(
    AniListAccountService accountService,
    CurrentAccountContext currentAccount) : PageModel
{
    private string ClientIdTempDataKey =>
        $"AniListClientId:{currentAccount.ProfileId}";
    [BindProperty]
    public int ClientId { get; set; }

    [BindProperty]
    public string AccessToken { get; set; } = "";

    public AniListAccountStatus Account { get; private set; } =
        AniListAccountStatus.Disconnected;

    public string? AuthorizationUrl { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Account = await accountService.GetStatusAsync(cancellationToken);

        if (TempData.TryGetValue(ClientIdTempDataKey, out var value) &&
            int.TryParse(value?.ToString(), out var clientId) &&
            clientId > 0)
        {
            ClientId = clientId;
            AuthorizationUrl = AniListAccountService.BuildAuthorizationUrl(clientId);
            TempData.Keep(ClientIdTempDataKey);
        }
    }

    public IActionResult OnPostPrepare()
    {
        if (ClientId <= 0)
        {
            ModelState.AddModelError(
                nameof(ClientId),
                "Enter the client ID from your AniList developer application.");
            return Page();
        }

        TempData[ClientIdTempDataKey] = ClientId.ToString();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostConnectAsync(
        CancellationToken cancellationToken)
    {
        if (ClientId <= 0)
        {
            ModelState.AddModelError(nameof(ClientId), "Enter a valid AniList client ID.");
        }

        if (string.IsNullOrWhiteSpace(AccessToken))
        {
            ModelState.AddModelError(nameof(AccessToken), "Paste the AniList access token.");
        }

        if (!ModelState.IsValid)
        {
            Account = await accountService.GetStatusAsync(cancellationToken);
            if (ClientId > 0)
            {
                AuthorizationUrl = AniListAccountService.BuildAuthorizationUrl(ClientId);
            }

            return Page();
        }

        try
        {
            await accountService.ConnectAsync(
                ClientId,
                AccessToken,
                cancellationToken);

            TempData.Remove(ClientIdTempDataKey);
            TempData["Status"] = "AniList connected.";
            return RedirectToPage();
        }
        catch (AniListAccountException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            Account = await accountService.GetStatusAsync(cancellationToken);
            AuthorizationUrl = AniListAccountService.BuildAuthorizationUrl(ClientId);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDisconnectAsync(
        CancellationToken cancellationToken)
    {
        await accountService.DisconnectAsync(cancellationToken);
        TempData.Remove(ClientIdTempDataKey);
        TempData["Status"] = "AniList disconnected.";
        return RedirectToPage();
    }
}
