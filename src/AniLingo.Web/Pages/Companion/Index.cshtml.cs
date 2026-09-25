using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Companion;

[AllowAnonymous]
public sealed class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}
