using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Statistics;

public sealed class IndexModel : PageModel
{
    public IActionResult OnGet() =>
        RedirectToPage("/Learn/Progress");
}
