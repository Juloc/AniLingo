using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Localization;
using AniLingo.Web.Features.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Admin;

// One scan run as shown in the admin UI: the operation record plus its parsed details.
public sealed record AdminLibraryScanRow(
    OperationSnapshot Operation,
    LibraryScanDetails? Details,
    string RootName)
{
    public string Scope => Details is null ? "—" : LibraryScanCoordinator.DescribeScope(Details.Folders);

    public string Trigger =>
        Details is null ? "—" : LibraryScanCoordinator.TriggerLabel(Details.Trigger);

    public string Phase =>
        Details is null ? "—" : LibraryScanCoordinator.PhaseLabel(Details.Phase);

    public LibraryScanCounters? Counters => Details?.Counters;

    public int Warnings => Details?.Warnings ?? 0;

    public string? FileProgress =>
        Details is { FilesTotal: > 0 } details && Operation.IsActive
            ? $"{Math.Min(details.FilesProcessed, details.FilesTotal)} of {details.FilesTotal} files"
            : null;

    public bool CanRunAgain =>
        !Operation.IsActive && Details is not null;

    public static AdminLibraryScanRow From(
        OperationSnapshot operation,
        IReadOnlyDictionary<Guid, string> rootNames)
    {
        var details = LibraryScanDetails.TryParse(operation.Details);
        var rootName = details is not null && rootNames.TryGetValue(details.RootId, out var name)
            ? name
            : operation.Subject ?? "Removed root";
        return new AdminLibraryScanRow(operation, details, rootName);
    }
}

[Authorize(Roles = AccountRoles.Owner)]
public sealed class ScansModel(
    AppDbContext db,
    LibraryScanCoordinator scans,
    CurrentAccountContext account) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    [BindProperty(SupportsGet = true)]
    public Guid? RootId { get; set; }

    public IReadOnlyList<(Guid Id, string Name)> Roots { get; private set; } = [];

    public IReadOnlyList<AdminLibraryScanRow> Scans { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var roots = await db.LibraryRoots
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);
        Roots = roots.Select(x => (x.Id, x.Name)).ToArray();
        var rootNames = roots.ToDictionary(x => x.Id, x => x.Name);

        var operations = await new OperationStore(db).ListAsync(
            new OperationListFilter(
                Kind: LibraryScanCoordinator.OperationKind,
                Limit: LibraryScanCoordinator.HistoryLimit),
            cancellationToken);

        Scans = operations
            .Select(operation => AdminLibraryScanRow.From(operation, rootNames))
            .Where(row => RootId is null || row.Details?.RootId == RootId)
            .ToArray();
    }

    public async Task<IActionResult> OnPostRunAgainAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var result = await scans.RetryAsync(operationId, account.ProfileId, cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        TempData["Status"] = result.Message;
        return RedirectToPage(new { rootId = RootId });
    }
}
