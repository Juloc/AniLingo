using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Kana;

public sealed class LegacyModel : PageModel
{
    public IActionResult OnGet(string? script, int stage = 1) =>
        RedirectToPage(
            "/Kana/Index",
            new { script, stage });
}
