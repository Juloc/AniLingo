using System.ComponentModel.DataAnnotations;
using AniLingo.Web.Features.Admin;
using AniLingo.Web.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings;

[Authorize(Roles = AccountRoles.Owner)]
public sealed class UsersModel(
    OwnerAuthService authService,
    AdminUserProgressService progressService) : PageModel
{
    public IReadOnlyList<AdminUserProgressSummary> Users { get; private set; } = [];

    [BindProperty]
    [Required]
    [StringLength(80)]
    public string UserName { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Password is required.")]
    [DataType(DataType.Password)]
    [MinLength(12, ErrorMessage = "Password must be at least 12 characters long.")]
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

    public Task<IActionResult> OnPostApproveAsync(
        string accountId,
        CancellationToken cancellationToken) =>
        SetEnabledAsync(
            accountId,
            enabled: true,
            successMessage: "User approved and enabled.",
            cancellationToken);

    public Task<IActionResult> OnPostDisableAsync(
        string accountId,
        CancellationToken cancellationToken) =>
        SetEnabledAsync(
            accountId,
            enabled: false,
            successMessage: "User disabled.",
            cancellationToken);

    private async Task<IActionResult> SetEnabledAsync(
        string accountId,
        bool enabled,
        string successMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await authService.SetEnabledAsync(accountId, enabled, cancellationToken);
            TempData["Status"] = successMessage;
        }
        catch (InvalidOperationException exception)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken) =>
        Users = await progressService.GetAsync(cancellationToken);
}
