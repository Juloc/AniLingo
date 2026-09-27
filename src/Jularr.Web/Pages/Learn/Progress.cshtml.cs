using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Learning;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Statistics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Learn;

public sealed class ProgressModel(
    AppDbContext db,
    LearningStatisticsService statisticsService,
    CurrentAccountContext currentAccount) : PageModel
{
    public LearningStatisticsSnapshot Statistics { get; private set; } =
        new(0, 0, 0, 0, 0, 0, 0, 0);

    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var resolved = await LearningModuleGate.ResolveAsync(
            db,
            currentAccount.ProfileId,
            cancellationToken);
        if (!resolved.Progress)
        {
            return LearningModuleGate.RedirectToHub();
        }

        Ui = await new UiTranslationCatalogStore(db).LoadProfileBundleAsync(
            currentAccount.ProfileId,
            cancellationToken);

        Statistics = await statisticsService.LoadAsync(
            currentAccount.ProfileId,
            DateTime.UtcNow,
            cancellationToken);
        return Page();
    }
}
