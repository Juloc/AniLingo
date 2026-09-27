using Jularr.Web.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Settings;

[Authorize(Roles = AccountRoles.Owner)]
public sealed class SubtitlesModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Admin/Subtitles");
}
