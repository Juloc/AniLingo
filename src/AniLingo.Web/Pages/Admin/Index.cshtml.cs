using AniLingo.Web.Data;
using AniLingo.Web.Features.Admin;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Admin;

[Authorize(Roles = AccountRoles.Owner)]
public sealed class IndexModel(
    AppDbContext db,
    AdminUserProgressService userProgressService) : PageModel
{
    public OperationSummary Summary { get; private set; } =
        new(0, 0, 0, 0, 0, 0);

    public IReadOnlyList<OperationSnapshot> Recent { get; private set; } = [];

    public int UserCount { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var store = new OperationStore(db);
        Summary = await store.GetSummaryAsync(cancellationToken);
        Recent = await store.ListAsync(
            new OperationListFilter(Limit: 8),
            cancellationToken);

        UserCount = (await userProgressService.GetAsync(cancellationToken)).Count;
    }
}
