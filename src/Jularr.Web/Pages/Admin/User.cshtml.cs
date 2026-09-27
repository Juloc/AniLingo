using Jularr.Web.Data;
using Jularr.Web.Features.Admin;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Tracking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Admin;

[Authorize(Roles = AccountRoles.Owner)]
public sealed class UserModel(
    AppDbContext db,
    OwnerAuthService authService,
    AdminUserProgressService progressService,
    AniListAccountStore aniListAccountStore) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public AdminUserProgressSummary Account { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(
        string id,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

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
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        try
        {
            await authService.RenameAsync(id, userName, cancellationToken);
            TempData["Status"] = Ui["admin.user.nameUpdated"];
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
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        try
        {
            await authService.SetEnabledAsync(id, enabled, cancellationToken);
            TempData["Status"] = enabled
                ? Ui["admin.user.enabled"]
                : Ui["admin.user.disabledSessionsRevoked"];
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
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        try
        {
            await authService.ResetPasswordAsync(id, newPassword, cancellationToken);
            TempData["Status"] = Ui["admin.user.passwordReset"];
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
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        try
        {
            await authService.InvalidateSessionsAsync(id, cancellationToken);
            TempData["Status"] = Ui["admin.user.sessionsSignedOut"];
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
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

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
                Ui["admin.user.confirmationRequired"]);
            return await ReloadOrNotFoundAsync(id, cancellationToken);
        }

        try
        {
            await authService.DeleteUserAsync(id, cancellationToken);
            await aniListAccountStore.DisconnectAsync(id, cancellationToken);
            TempData["Status"] = Ui.Format("admin.user.deleted", ("userName", account.UserName));
            return RedirectToPage("/Admin/Users");
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
