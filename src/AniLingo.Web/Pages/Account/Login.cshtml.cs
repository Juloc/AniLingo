using System.ComponentModel.DataAnnotations;
using AniLingo.Web.Features.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace AniLingo.Web.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting("login")]
public sealed class LoginModel(OwnerAuthService ownerAuth) : PageModel
{
    [BindProperty]
    [Required]
    [StringLength(80)]
    public string UserName { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public bool RememberMe { get; set; }

    [BindProperty(SupportsGet = true)]
    public string ReturnUrl { get; set; } = "/";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        ReturnUrl = SafeReturnUrl();

        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(ReturnUrl);
        }

        if (!await ownerAuth.HasOwnerAsync(cancellationToken))
        {
            return RedirectToPage("/Account/Setup", new { returnUrl = ReturnUrl });
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ReturnUrl = SafeReturnUrl();

        if (!await ownerAuth.HasOwnerAsync(cancellationToken))
        {
            return RedirectToPage("/Account/Setup", new { returnUrl = ReturnUrl });
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var owner = await ownerAuth.ValidateCredentialsAsync(UserName, Password, cancellationToken);
        if (owner is null)
        {
            ModelState.AddModelError(string.Empty, "Invalid user name or password.");
            return Page();
        }

        var properties = new AuthenticationProperties
        {
            IsPersistent = RememberMe,
            AllowRefresh = true
        };

        if (RememberMe)
        {
            properties.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30);
        }

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            OwnerAuthService.CreatePrincipal(owner),
            properties);

        return LocalRedirect(ReturnUrl);
    }

    private string SafeReturnUrl() =>
        Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/";
}
