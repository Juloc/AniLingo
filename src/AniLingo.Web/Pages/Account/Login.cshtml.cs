using System.ComponentModel.DataAnnotations;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Localization;
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
    [StringLength(AccountForm.MaxUserNameLength)]
    public string UserName { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public bool RememberMe { get; set; }

    [BindProperty(SupportsGet = true)]
    public string ReturnUrl { get; set; } = "/";

    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public async Task<IActionResult> OnGetAsync(
        [FromServices] AppDbContext db,
        CancellationToken cancellationToken)
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

        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(
        [FromServices] AppDbContext db,
        CancellationToken cancellationToken)
    {
        ReturnUrl = SafeReturnUrl();

        if (!await ownerAuth.HasOwnerAsync(cancellationToken))
        {
            return RedirectToPage("/Account/Setup", new { returnUrl = ReturnUrl });
        }

        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        if (!ModelState.IsValid)
        {
            AccountForm.LocalizeFieldErrors(ModelState, new Dictionary<string, string>
            {
                [nameof(UserName)] = AccountForm.UserNameMessage(Ui),
                [nameof(Password)] = Ui["account.validation.password"]
            });
            return Page();
        }

        var owner = await ownerAuth.ValidateCredentialsAsync(UserName, Password, cancellationToken);
        if (owner is null)
        {
            ModelState.AddModelError(string.Empty, Ui["account.login.invalid"]);
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
