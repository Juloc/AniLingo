using System.ComponentModel.DataAnnotations;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace AniLingo.Web.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting("login")]
public sealed class RegisterModel(OwnerAuthService ownerAuth) : PageModel
{
    [BindProperty]
    [Required]
    [StringLength(AccountForm.MaxUserNameLength, MinimumLength = 1)]
    public string UserName { get; set; } = string.Empty;

    // These English messages only apply to direct DataAnnotations validation;
    // the rendered page replaces them with catalog messages (AccountForm).
    [BindProperty]
    [Required(ErrorMessage = "Password is required.")]
    [MinLength(AccountForm.MinPasswordLength, ErrorMessage = "Password must be at least 12 characters long.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Please confirm your password.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public async Task<IActionResult> OnGetAsync(
        [FromServices] AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect("/");
        }

        if (!await ownerAuth.HasOwnerAsync(cancellationToken))
        {
            return RedirectToPage("/Account/Setup");
        }

        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(
        [FromServices] AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect("/");
        }

        if (!await ownerAuth.HasOwnerAsync(cancellationToken))
        {
            return RedirectToPage("/Account/Setup");
        }

        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        if (!ModelState.IsValid)
        {
            AccountForm.LocalizeFieldErrors(ModelState, new Dictionary<string, string>
            {
                [nameof(UserName)] = AccountForm.UserNameMessage(Ui),
                [nameof(Password)] = AccountForm.NewPasswordMessage(Ui),
                [nameof(ConfirmPassword)] = Ui["account.validation.confirmPassword"]
            });
            return Page();
        }

        try
        {
            await ownerAuth.CreateRegistrationRequestAsync(
                UserName,
                Password,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            AccountForm.AddCreationError(ModelState, Ui, exception);
            return Page();
        }

        TempData["Status"] = Ui["account.register.submitted"];
        return RedirectToPage("/Account/Login");
    }
}
