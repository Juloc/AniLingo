using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Tracking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings;

public sealed class AniListModel(
    AniListAccountService accountService,
    AniListSyncService syncService,
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

    public AniListSyncOverview Sync { get; private set; } =
        new(AniListSyncMode.Off, null, [], null);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Account = await accountService.GetStatusAsync(cancellationToken);
        if (Account.IsConnected)
        {
            Sync = await syncService.GetOverviewAsync(cancellationToken);
        }

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

    public async Task<IActionResult> OnPostSyncModeAsync(
        AniListSyncMode syncMode,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(syncMode))
        {
            return BadRequest();
        }

        try
        {
            TempData["Status"] = await syncService.SetModeAsync(syncMode, cancellationToken)
                ? $"Automatic AniList sync: {SyncModeLabel(syncMode)}."
                : "Connect AniList before choosing automatic sync.";
        }
        catch (AniListAccountException exception)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage();
    }

    public static string SyncModeLabel(AniListSyncMode mode) => mode switch
    {
        AniListSyncMode.OnCompletion => "On completion",
        AniListSyncMode.Continuous => "Continuous",
        _ => "Off"
    };

    public static string SyncModeDescription(AniListSyncMode mode) => mode switch
    {
        AniListSyncMode.OnCompletion =>
            "Update AniList shortly after you finish an episode, chapter or volume.",
        AniListSyncMode.Continuous =>
            $"Update AniList with forward progress whenever you pause watching or reading for {AniListSyncReconciler.ContinuousDebounce.TotalMinutes:0} minutes.",
        _ => "Only update AniList when you press Sync on a detail page."
    };

    public static (string Label, string CssClass) SyncStatus(AniListSyncItemStatus status) => status switch
    {
        AniListSyncItemStatus.Synced => ("Synced", "status-ok"),
        AniListSyncItemStatus.UpToDate => ("Up to date", "status-ok"),
        AniListSyncItemStatus.Blocked => ("Blocked", "status-warn"),
        _ => ("Error", "status-error")
    };

    public static string LocalWorkUrl(AniListSyncItem item) => item.MediaKind switch
    {
        AniListSyncCheckpoints.Anime => $"/Library/Anime/{item.LocalId}",
        AniListSyncCheckpoints.Manga => $"/Manga/Series/{item.LocalId}",
        _ => $"/Novels/Work/{item.LocalId}"
    };

    public async Task<IActionResult> OnPostDisconnectAsync(
        CancellationToken cancellationToken)
    {
        await accountService.DisconnectAsync(cancellationToken);
        TempData.Remove(ClientIdTempDataKey);
        TempData["Status"] = "AniList disconnected.";
        return RedirectToPage();
    }
}
