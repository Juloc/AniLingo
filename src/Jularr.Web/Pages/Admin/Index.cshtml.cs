using Jularr.Web.Data;
using Jularr.Web.Features.Admin;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Admin;

[Authorize(Roles = AccountRoles.Owner)]
public sealed class IndexModel(
    AppDbContext db,
    AdminUserProgressService userProgressService,
    AdminOverviewService overviewService) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public IReadOnlyList<OperationSnapshot> Recent { get; private set; } = [];

    public int UserCount { get; private set; }

    public AdminOverviewSnapshot Overview { get; private set; } = AdminOverviewSnapshot.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var store = new OperationStore(db);
        Recent = await store.ListAsync(
            new OperationListFilter(Limit: 8),
            cancellationToken);

        UserCount = (await userProgressService.GetAsync(cancellationToken)).Count;
        Overview = await overviewService.GetAsync(cancellationToken);
    }
}
