using System.ComponentModel.DataAnnotations;
using AniLingo.Web.Features.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Account;

[AllowAnonymous]
public sealed class SetupModel(OwnerAuthService ownerAuth) : PageModel
{
    [BindProperty]
    [Required]
    [StringLength(80, MinimumLength = 1)]
    public string UserName { get; set; } = "owner";

    [BindProperty]
    [Required]
    [MinLength(12)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password))]
    public string ConfirmPassword { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string ReturnUrl { get; set; } = "/";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        ReturnUrl = SafeReturnUrl();

        if (await ownerAuth.HasOwnerAsync(cancellationToken))
        {
            return RedirectToPage("/Account/Login", new { returnUrl = ReturnUrl });
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ReturnUrl = SafeReturnUrl();

        if (await ownerAuth.HasOwnerAsync(cancellationToken))
        {
            return RedirectToPage("/Account/Login", new { returnUrl = ReturnUrl });
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        OwnerAccount owner;
        try
        {
            owner = await ownerAuth.CreateOwnerAsync(UserName, Password, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return RedirectToPage("/Account/Login", new { returnUrl = ReturnUrl });
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            OwnerAuthService.CreatePrincipal(owner));

        TempData["Status"] = "Owner account created.";
        return LocalRedirect(ReturnUrl);
    }

    private string SafeReturnUrl() =>
        Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/";
}
