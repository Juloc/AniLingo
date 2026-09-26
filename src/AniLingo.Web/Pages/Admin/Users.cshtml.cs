using System.ComponentModel.DataAnnotations;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Admin;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Admin;

[Authorize(Roles = AccountRoles.Owner)]
public sealed class UsersModel(
    AppDbContext db,
    OwnerAuthService authService,
    AdminUserProgressService progressService) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

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

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            await authService.CreateUserAsync(UserName, Password, cancellationToken);
            TempData["Status"] = Ui.Format("admin.users.created", ("userName", UserName.Trim()));
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
            successMessageKey: "admin.users.approved",
            cancellationToken);

    public Task<IActionResult> OnPostDisableAsync(
        string accountId,
        CancellationToken cancellationToken) =>
        SetEnabledAsync(
            accountId,
            enabled: false,
            successMessageKey: "admin.users.disabled",
            cancellationToken);

    private async Task<IActionResult> SetEnabledAsync(
        string accountId,
        bool enabled,
        string successMessageKey,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        try
        {
            await authService.SetEnabledAsync(accountId, enabled, cancellationToken);
            TempData["Status"] = Ui[successMessageKey];
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
