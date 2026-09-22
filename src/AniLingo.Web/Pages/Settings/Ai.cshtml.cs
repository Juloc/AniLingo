using AniLingo.Web.Features.Ai;
using AniLingo.Web.Infrastructure.Ai;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings;

public sealed class AiModel(CodexCliProvider codex) : PageModel
{
    public AiProviderStatus Provider { get; private set; } =
        new("codex-cli", "OpenAI Codex CLI", false, false, null, null, null);

    public DeviceLoginSnapshot Login { get; private set; } =
        new(DeviceLoginState.Idle, null, null, null, null);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostConnectAsync(CancellationToken cancellationToken)
    {
        var snapshot = await codex.StartDeviceLoginAsync(cancellationToken);

        TempData["Status"] = snapshot.State switch
        {
            DeviceLoginState.WaitingForUser => "Device login started.",
            DeviceLoginState.Succeeded => "Codex connected.",
            DeviceLoginState.Failed => snapshot.Message ?? "Codex login failed.",
            _ => "Codex login is starting."
        };

        return RedirectToPage();
    }

    public IActionResult OnPostCancel()
    {
        codex.CancelDeviceLogin();
        TempData["Status"] = "Codex login cancelled.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostLogoutAsync(CancellationToken cancellationToken)
    {
        var success = await codex.LogoutAsync(cancellationToken);
        TempData["Status"] = success ? "Codex disconnected." : "Codex logout failed.";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Provider = await codex.GetStatusAsync(cancellationToken);
        Login = codex.GetDeviceLoginSnapshot();
    }
}
