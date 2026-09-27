using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.MediaMapping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Settings;

public sealed class MappingReviewModel(
    MediaMappingReviewStore reviewStore,
    CurrentAccountContext account,
    AppDbContext db) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public IReadOnlyList<MediaMappingReviewTask> Tasks { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (!account.IsOwner)
        {
            return Forbid();
        }

        Tasks = await reviewStore.ListAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostDismissAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (!account.IsOwner)
        {
            return Forbid();
        }

        var removed = await reviewStore.DismissAsync(
            id,
            cancellationToken);
        TempData["Status"] = removed
            ? Ui["settings.mappingReview.dismissed"]
            : Ui["settings.mappingReview.alreadyGone"];

        return RedirectToPage();
    }

    public static string? TargetUrl(MediaMappingReviewTask task)
    {
        if (!Guid.TryParse(task.LocalId, out var id))
        {
            return null;
        }

        return task.MediaType.ToLowerInvariant() switch
        {
            "anime" => $"/Library/Anime/{id}",
            "manga" => $"/Manga/Series/{id}",
            "novel" => $"/Novels/Work/{id}",
            _ => null
        };
    }
}
