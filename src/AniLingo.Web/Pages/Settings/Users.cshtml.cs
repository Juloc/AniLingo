using System.ComponentModel.DataAnnotations;
using AniLingo.Web.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings;

[Authorize(Roles = AccountRoles.Owner)]
public sealed class UsersModel(OwnerAuthService authService) : PageModel
{
    public IReadOnlyList<LocalAccountSummary> Accounts { get; private set; } = [];

    [BindProperty]
    [Required]
    [StringLength(80)]
    public string UserName { get; set; } = "";

    [BindProperty]
    [Required]
    [DataType(DataType.Password)]
    [MinLength(12)]
    public string Password { get; set; } = "";

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            await authService.CreateUserAsync(UserName, Password, cancellationToken);
            TempData["Status"] = $"User {UserName.Trim()} created.";
            return RedirectToPage();
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostSetEnabledAsync(
        string accountId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        try
        {
            await authService.SetEnabledAsync(accountId, enabled, cancellationToken);
            TempData["Status"] = enabled ? "User approved and enabled." : "User disabled.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(
        string accountId,
        string newPassword,
        CancellationToken cancellationToken)
    {
        try
        {
            await authService.ResetPasswordAsync(
                accountId,
                newPassword,
                cancellationToken);
            TempData["Status"] = "Password reset.";
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken) =>
        Accounts = await authService.ListAsync(cancellationToken);
}
