using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Localization;
using AniLingo.Web.Features.Tracking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings;

public sealed class AniListModel(
    AniListAccountService accountService,
    AniListSyncService syncService,
    CurrentAccountContext currentAccount,
    AppDbContext db) : PageModel
{
    private string ClientIdTempDataKey =>
        $"AniListClientId:{currentAccount.ProfileId}";

    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

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
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

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

    public async Task<IActionResult> OnPostPrepareAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (ClientId <= 0)
        {
            ModelState.AddModelError(
                nameof(ClientId),
                Ui["settings.anilist.validation.clientIdRequired"]);
            return Page();
        }

        TempData[ClientIdTempDataKey] = ClientId.ToString();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostConnectAsync(
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (ClientId <= 0)
        {
            ModelState.AddModelError(
                nameof(ClientId),
                Ui["settings.anilist.validation.clientIdInvalid"]);
        }

        if (string.IsNullOrWhiteSpace(AccessToken))
        {
            ModelState.AddModelError(
                nameof(AccessToken),
                Ui["settings.anilist.validation.accessTokenRequired"]);
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
            TempData["Status"] = Ui["settings.anilist.status.connected"];
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
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (!Enum.IsDefined(syncMode))
        {
            return BadRequest();
        }

        try
        {
            TempData["Status"] = await syncService.SetModeAsync(syncMode, cancellationToken)
                ? Ui.Format("settings.anilist.status.syncModeSet", ("mode", SyncModeLabel(syncMode)))
                : Ui["settings.anilist.status.connectFirst"];
        }
        catch (AniListAccountException exception)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage();
    }

    public string SyncModeLabel(AniListSyncMode mode) => mode switch
    {
        AniListSyncMode.OnCompletion => Ui["settings.anilist.syncMode.onCompletion"],
        AniListSyncMode.Continuous => Ui["settings.anilist.syncMode.continuous"],
        _ => Ui["settings.anilist.syncMode.off"]
    };

    public string SyncModeDescription(AniListSyncMode mode) => mode switch
    {
        AniListSyncMode.OnCompletion =>
            Ui["settings.anilist.syncMode.onCompletion.description"],
        AniListSyncMode.Continuous =>
            Ui.Format(
                "settings.anilist.syncMode.continuous.description",
                ("minutes", $"{AniListSyncReconciler.ContinuousDebounce.TotalMinutes:0}")),
        _ => Ui["settings.anilist.syncMode.off.description"]
    };

    public (string Label, string CssClass) SyncStatus(AniListSyncItemStatus status) => status switch
    {
        AniListSyncItemStatus.Synced => (Ui["settings.anilist.syncStatus.synced"], "status-ok"),
        AniListSyncItemStatus.UpToDate => (Ui["settings.anilist.syncStatus.upToDate"], "status-ok"),
        AniListSyncItemStatus.Blocked => (Ui["settings.anilist.syncStatus.blocked"], "status-warn"),
        _ => (Ui["settings.anilist.syncStatus.error"], "status-error")
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
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        await accountService.DisconnectAsync(cancellationToken);
        TempData.Remove(ClientIdTempDataKey);
        TempData["Status"] = Ui["settings.anilist.status.disconnected"];
        return RedirectToPage();
    }
}
