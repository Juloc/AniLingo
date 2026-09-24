using AniLingo.Web.Features.Admin;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Tracking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings;

[Authorize(Roles = AccountRoles.Owner)]
public sealed class UserModel(
    OwnerAuthService authService,
    AdminUserProgressService progressService,
    AniListAccountStore aniListAccountStore) : PageModel
{
    public AdminUserProgressSummary Account { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(
        string id,
        CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostRenameAsync(
        string id,
        string userName,
        CancellationToken cancellationToken)
    {
        try
        {
            await authService.RenameAsync(id, userName, cancellationToken);
            TempData["Status"] = "User name updated.";
            return RedirectToPage(new { id });
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return await ReloadOrNotFoundAsync(id, cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostSetEnabledAsync(
        string id,
        bool enabled,
        CancellationToken cancellationToken)
    {
        try
        {
            await authService.SetEnabledAsync(id, enabled, cancellationToken);
            TempData["Status"] = enabled
                ? "User enabled."
                : "User disabled and existing sessions revoked.";
            return RedirectToPage(new { id });
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return await ReloadOrNotFoundAsync(id, cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(
        string id,
        string newPassword,
        CancellationToken cancellationToken)
    {
        try
        {
            await authService.ResetPasswordAsync(id, newPassword, cancellationToken);
            TempData["Status"] = "Password reset and existing sessions revoked.";
            return RedirectToPage(new { id });
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return await ReloadOrNotFoundAsync(id, cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostInvalidateSessionsAsync(
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            await authService.InvalidateSessionsAsync(id, cancellationToken);
            TempData["Status"] = "All existing sessions were signed out.";
            return RedirectToPage(new { id });
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return await ReloadOrNotFoundAsync(id, cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(
        string id,
        string confirmation,
        CancellationToken cancellationToken)
    {
        var account = await authService.GetAsync(id, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        if (!string.Equals(
                account.UserName,
                confirmation?.Trim(),
                StringComparison.Ordinal))
        {
            ModelState.AddModelError(
                string.Empty,
                "Enter the user name exactly to confirm deletion.");
            return await ReloadOrNotFoundAsync(id, cancellationToken);
        }

        try
        {
            await authService.DeleteUserAsync(id, cancellationToken);
            await aniListAccountStore.DisconnectAsync(id, cancellationToken);
            TempData["Status"] = $"User {account.UserName} deleted.";
            return RedirectToPage("/Settings/Users");
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return await ReloadOrNotFoundAsync(id, cancellationToken);
        }
    }

    private async Task<IActionResult> ReloadOrNotFoundAsync(
        string id,
        CancellationToken cancellationToken) =>
        await LoadAsync(id, cancellationToken)
            ? Page()
            : NotFound();

    private async Task<bool> LoadAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var users = await progressService.GetAsync(cancellationToken);
        var account = users.SingleOrDefault(x => x.Account.Id == id);
        if (account is null)
        {
            return false;
        }

        Account = account;
        return true;
    }
}
