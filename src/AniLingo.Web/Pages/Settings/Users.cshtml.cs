using AniLingo.Web.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings;

[Authorize(Roles = AccountRoles.Owner)]
public sealed class UsersModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Admin/Users");
}
