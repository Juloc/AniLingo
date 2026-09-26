using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning;
using Microsoft.AspNetCore.Mvc;

namespace AniLingo.Web.Pages.Learn;

/// <summary>
/// Page-level entry guard for Learning Hub modules. Every module page resolves
/// its capability through the canonical <see cref="LearningConfigurationStore"/>
/// at profile scope; a disabled module redirects to the hub instead of rendering.
/// </summary>
public static class LearningModuleGate
{
    public static async Task<LearningResolvedSettings> ResolveAsync(
        AppDbContext db,
        string profileId,
        CancellationToken cancellationToken) =>
        await new LearningConfigurationStore(db).ResolveProfileAsync(
            profileId,
            cancellationToken);

    public static IActionResult RedirectToHub() =>
        new RedirectToPageResult("/Learn/Index");
}
