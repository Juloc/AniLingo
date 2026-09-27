using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Statistics;

public sealed class IndexModel : PageModel
{
    public IActionResult OnGet() =>
        RedirectToPage("/Learn/Progress");
}
