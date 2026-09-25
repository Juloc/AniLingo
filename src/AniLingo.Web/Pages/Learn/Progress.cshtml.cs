using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Localization;
using AniLingo.Web.Features.Statistics;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Learn;

public sealed class ProgressModel(
    AppDbContext db,
    LearningStatisticsService statisticsService,
    CurrentAccountContext currentAccount) : PageModel
{
    public LearningStatisticsSnapshot Statistics { get; private set; } =
        new(0, 0, 0, 0, 0, 0, 0, 0);

    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await new UiTranslationCatalogStore(db).LoadProfileBundleAsync(
            currentAccount.ProfileId,
            cancellationToken);

        Statistics = await statisticsService.LoadAsync(
            currentAccount.ProfileId,
            DateTime.UtcNow,
            cancellationToken);
    }
}
