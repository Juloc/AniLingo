using Jularr.Web.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Settings.DownloadClients;

/// <summary>SABnzbd connections are managed on the Usenet hub; this address stays for old links.</summary>
[Authorize(Roles = AccountRoles.Owner)]
public sealed class IndexModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Admin/Usenet");
}
