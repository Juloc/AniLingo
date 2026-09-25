using System.Security.Claims;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Appearance;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Appearance;

[Authorize]
public sealed class ThemeModel(AppDbContext db) : PageModel
{
    public async Task<IActionResult> OnPostAsync(string? theme)
    {
        var profileId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return Unauthorized();
        }

        if (!AppTheme.TryNormalize(theme, out var normalized))
        {
            return BadRequest(new { error = "Unknown theme mode." });
        }

        var store = new UiTranslationCatalogStore(db);
        await store.SetProfileThemeAsync(
            profileId,
            normalized,
            HttpContext.RequestAborted);

        return new JsonResult(new { theme = normalized });
    }
}
