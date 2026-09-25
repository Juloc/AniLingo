using AniLingo.Web.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings;

[Authorize(Roles = AccountRoles.Owner)]
public sealed class UserModel : PageModel
{
    public IActionResult OnGet(string id) =>
        RedirectToPage("/Admin/User", new { id });
}
