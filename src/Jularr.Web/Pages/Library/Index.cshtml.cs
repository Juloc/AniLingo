using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Library;
using Jularr.Web.Features.Localization;
using Jularr.Web.Ui;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Library;

public sealed class IndexModel(AppDbContext db, CurrentAccountContext currentAccount) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public IReadOnlyList<MediaBannerCardModel> Cards { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        var cards = await new LibraryMediaCardQuery(db).GetAnimeAsync(
            currentAccount.ProfileId,
            cancellationToken);

        Cards = [.. cards.Select(card => MediaBannerCardModel.Create(card, Ui))];
    }
}
