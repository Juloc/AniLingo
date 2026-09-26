using System.Security.Claims;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Appearance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Appearance;

/// <summary>
/// Saves the profile accent and serves palette previews. The preview returns the exact stylesheet
/// the layout would render for a seed, so the live preview in Settings uses the one server-side
/// engine instead of a second copy of the colour maths in JavaScript.
/// </summary>
[Authorize]
public sealed class AccentModel(AppDbContext db) : PageModel
{
    public IActionResult OnGetPreview(string? accent)
    {
        if (!AppAccent.TryNormalize(accent, out var normalized))
        {
            return BadRequest(new { error = "Accent must be a #rrggbb colour." });
        }

        return Content(AccentPalette.Build(normalized).ToStyleSheet(), "text/css");
    }

    public async Task<IActionResult> OnPostAsync(string? accent)
    {
        var profileId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return Unauthorized();
        }

        if (!AppAccent.TryNormalize(accent, out _))
        {
            return BadRequest(new { error = "Accent must be a #rrggbb colour." });
        }

        var stored = await new ProfileAppearanceStore(db).SetAccentAsync(
            profileId,
            accent,
            HttpContext.RequestAborted);

        return new JsonResult(new { accent = stored, effective = AppAccent.Effective(stored) });
    }
}
