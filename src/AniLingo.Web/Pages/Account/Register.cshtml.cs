using System.ComponentModel.DataAnnotations;
using AniLingo.Web.Features.Auth;
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
    [StringLength(80, MinimumLength = 1)]
    public string UserName { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Password is required.")]
    [MinLength(12, ErrorMessage = "Password must be at least 12 characters long.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Please confirm your password.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect("/");
        }

        if (!await ownerAuth.HasOwnerAsync(cancellationToken))
        {
            return RedirectToPage("/Account/Setup");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect("/");
        }

        if (!await ownerAuth.HasOwnerAsync(cancellationToken))
        {
            return RedirectToPage("/Account/Setup");
        }

        if (!ModelState.IsValid)
        {
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
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }

        TempData["Status"] =
            "Registration submitted. The owner must approve your account before you can sign in.";
        return RedirectToPage("/Account/Login");
    }
}
