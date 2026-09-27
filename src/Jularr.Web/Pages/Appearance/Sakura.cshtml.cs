using System.Security.Claims;
using Jularr.Web.Data;
using Jularr.Web.Features.Appearance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Appearance;

[Authorize]
public sealed class SakuraModel(AppDbContext db) : PageModel
{
    public async Task<IActionResult> OnPostAsync(string? sakura)
    {
        var profileId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return Unauthorized();
        }

        if (!AppSakura.TryNormalize(sakura, out var normalized))
        {
            return BadRequest(new { error = "Sakura mode must be off, subtle or full." });
        }

        await new ProfileAppearanceStore(db).SetSakuraAsync(
            profileId,
            normalized,
            HttpContext.RequestAborted);

        return new JsonResult(new { sakura = normalized });
    }
}
