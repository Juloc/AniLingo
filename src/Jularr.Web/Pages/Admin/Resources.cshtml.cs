using Jularr.Web.Data;
using Jularr.Web.Features.Admin;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Admin;

/// <summary>Admin → Resources: only the Jularr/PostgreSQL stack and the storage mounts visible to it.</summary>
[Authorize(Policy = JularrPolicies.AdminMedia)]
public sealed class ResourcesModel(
    AppDbContext db,
    AdminDashboardService dashboard) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public AdminDashboardSnapshot Snapshot { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        Snapshot = await dashboard.GetAsync(includeSessions: false, cancellationToken);
    }
}
