using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Recommendations;
using Jularr.Web.Ui;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Recommendations;

/// <summary>
/// The cross-media "For you" landing (#428): explainable content and continuation shelves for the
/// current profile, rendered on the shared shelf surface. All work is local; no provider is called here.
/// </summary>
public sealed class ForYouModel(
    MediaRecommendationService recommendations,
    AppDbContext db,
    CurrentAccountContext account) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public MediaShelfBoardModel Board { get; private set; } = new([]);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var result = await recommendations.GetForProfileAsync(
            account.User,
            account.ProfileId,
            cancellationToken);

        Board = MediaRecommendationShelfView.ToBoard(result, Ui);
    }
}
