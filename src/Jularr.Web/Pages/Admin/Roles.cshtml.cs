using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Admin;

[Authorize(Policy = JularrPolicies.AdminSystem)]
public sealed class RolesModel(
    AppDbContext db,
    OwnerAuthService accounts) : PageModel
{
    private static readonly AccountRole[] OrderedRoles =
    [
        AccountRole.Owner,
        AccountRole.MediaManager,
        AccountRole.User
    ];

    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public IReadOnlyList<RoleSummary> Roles { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        var listed = await accounts.ListAsync(cancellationToken);

        Roles = OrderedRoles
            .Select(role => new RoleSummary(
                role,
                listed.Count(account => account.Role == role),
                JularrPolicies.Roles
                    .Where(pair => pair.Value.Contains(role))
                    .Select(pair => pair.Key)
                    .ToArray()))
            .ToArray();
    }

    public sealed record RoleSummary(
        AccountRole Role,
        int AccountCount,
        IReadOnlyList<string> Policies);
}
