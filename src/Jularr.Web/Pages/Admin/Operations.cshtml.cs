using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Admin;

[Authorize(Policy = JularrPolicies.AdminMedia)]
public sealed class OperationsModel(AppDbContext db) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    [BindProperty(SupportsGet = true)]
    public string View { get; set; } = "active";

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Category { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public IReadOnlyList<OperationSnapshot> Operations { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var parsedStatus = Enum.TryParse<OperationStatus>(
            Status,
            ignoreCase: true,
            out var status)
            ? status
            : null as OperationStatus?;

        Operations = await new OperationStore(db).ListAsync(
            new OperationListFilter(
                View,
                parsedStatus,
                Category,
                Search,
                Limit: 200),
            cancellationToken);
    }
}
