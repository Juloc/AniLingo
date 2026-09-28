using Jularr.Web.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Settings;

[Authorize(Policy = JularrPolicies.AdminSystem)]
public sealed class UserModel : PageModel
{
    public IActionResult OnGet(string id) =>
        RedirectToPage("/Admin/User", new { id });
}
