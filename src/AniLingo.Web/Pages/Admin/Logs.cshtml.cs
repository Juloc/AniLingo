using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Localization;
using AniLingo.Web.Features.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Admin;

[Authorize(Roles = AccountRoles.Owner)]
public sealed class LogsModel(AppDbContext db) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    [BindProperty(SupportsGet = true)]
    public string? Level { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Module { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? OperationId { get; set; }

    public IReadOnlyList<OperationLogEntry> Logs { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var parsedLevel = Enum.TryParse<OperationLogLevel>(
            Level,
            ignoreCase: true,
            out var level)
            ? level
            : null as OperationLogLevel?;

        Logs = await new OperationStore(db).ListLogsAsync(
            new OperationLogFilter(
                parsedLevel,
                OperationId,
                Module,
                Search,
                Limit: 500),
            cancellationToken);
    }
}
