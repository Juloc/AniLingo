using System.Security.Claims;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Appearance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Appearance;

[Authorize]
public sealed class ThemeModel(AppDbContext db) : PageModel
{
    public async Task<IActionResult> OnPostAsync(string? theme)
    {
        var profileId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return Unauthorized();
        }

        if (!AppTheme.TryNormalize(theme, out var normalized))
        {
            return BadRequest(new { error = "Unknown theme mode." });
        }

        await new ProfileAppearanceStore(db).SetThemeAsync(
            profileId,
            normalized,
            HttpContext.RequestAborted);

        return new JsonResult(new { theme = normalized });
    }
}
