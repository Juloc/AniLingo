using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Statistics;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Statistics;

public sealed class IndexModel(
    LearningStatisticsService statisticsService,
    CurrentAccountContext currentAccount) : PageModel
{
    public LearningStatisticsSnapshot Statistics { get; private set; } =
        new(0, 0, 0, 0, 0, 0, 0, 0);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Statistics = await statisticsService.LoadAsync(
            currentAccount.ProfileId,
            DateTime.UtcNow,
            cancellationToken);
    }
}
