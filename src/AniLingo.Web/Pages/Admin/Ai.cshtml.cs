using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Authorization;
using AniLingo.Web.Features.Ai;
using AniLingo.Web.Infrastructure.Ai;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Admin;

[Authorize(Roles = AccountRoles.Owner)]
public sealed class AiModel(AppDbContext db, CodexCliProvider codex) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public AiProviderStatus Provider { get; private set; } =
        new("codex-cli", "OpenAI Codex CLI", false, false, null, null, null);

    public DeviceLoginSnapshot Login { get; private set; } =
        new(DeviceLoginState.Idle, null, null, null, null);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostConnectAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var snapshot = await codex.StartDeviceLoginAsync(cancellationToken);

        TempData["Status"] = snapshot.State switch
        {
            DeviceLoginState.WaitingForUser => Ui["admin.ai.loginStarted"],
            DeviceLoginState.Succeeded => Ui["admin.ai.connected"],
            DeviceLoginState.Failed => snapshot.Message ?? Ui["admin.ai.loginFailed"],
            _ => Ui["admin.ai.loginStarting"]
        };

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCancelAsync()
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        codex.CancelDeviceLogin();
        TempData["Status"] = Ui["admin.ai.loginCancelled"];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostLogoutAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var success = await codex.LogoutAsync(cancellationToken);
        TempData["Status"] = success ? Ui["admin.ai.disconnected"] : Ui["admin.ai.logoutFailed"];
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Provider = await codex.GetStatusAsync(cancellationToken);
        Login = codex.GetDeviceLoginSnapshot();
    }
}
