using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning;
using Microsoft.AspNetCore.Mvc;

namespace AniLingo.Web.Pages.Learn;

/// <summary>
/// Page-level entry guard for Learning Hub modules. Module pages resolve their
/// visibility through the canonical <see cref="LearningModuleResolver"/>; a
/// disabled module redirects to the hub instead of rendering.
/// </summary>
public static class LearningModuleGate
{
    public static Task<LearningModuleAvailability> ResolveAsync(
        AppDbContext db,
        string profileId,
        CancellationToken cancellationToken) =>
        new LearningModuleResolver(db).ResolveAsync(profileId, cancellationToken);

    public static IActionResult RedirectToHub() =>
        new RedirectToPageResult("/Learn/Index");
}
