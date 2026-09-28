using System.Security.Claims;
using Jularr.Web.Data;
using Jularr.Web.Features.Appearance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Appearance;

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

    public async Task<IActionResult> OnPostSelectAsync(string? themeId)
    {
        var profileId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return Unauthorized();
        }

        var instance = await new InstanceAppearanceSettingsStore(db)
            .LoadAsync(HttpContext.RequestAborted);
        if (!instance.AllowProfileThemeOverride)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(themeId))
        {
            await new ProfileAppearanceStore(db).SetThemeIdAsync(
                profileId,
                null,
                HttpContext.RequestAborted);
            return SelectedThemeResponse(null);
        }

        if (!ThemeCatalog.TryGet(themeId, out var theme))
        {
            return BadRequest(new { error = "Unknown application theme." });
        }

        await new ProfileAppearanceStore(db).SetThemeIdAsync(
            profileId,
            theme.Id,
            HttpContext.RequestAborted);
        return SelectedThemeResponse(theme.Id);
    }

    private IActionResult SelectedThemeResponse(string? themeId) =>
        Request.Headers.Accept.Any(value => value.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            ? new JsonResult(new { themeId })
            : RedirectToPage("/Settings/Appearance");
}
