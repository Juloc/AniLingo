using AniLingo.Web.Data;
using AniLingo.Web.Features.Acquisition.Health;
using AniLingo.Web.Features.Acquisition.Indexers;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings.Indexers;

public sealed record IndexerRow(IndexerEntry Entry, AcquisitionHealthStatus? Health);

/// <summary>
/// The canonical indexer list: Prowlarr and direct Newznab connections, each with priority,
/// enable/disable, test and health status.
/// </summary>
[Authorize(Roles = AccountRoles.Owner)]
public sealed class IndexModel(
    AppDbContext db,
    IndexerStore store,
    IReadOnlyDictionary<IndexerType, IIndexer> indexers,
    AcquisitionHealthStore health) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public IReadOnlyList<IndexerRow> Rows { get; private set; } = [];
    public string? Notice => TempData["IndexerNotice"] as string;
    public string? Error => TempData["IndexerError"] as string;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        var entries = (await store.LoadAllAsync(cancellationToken))
            .OrderBy(entry => entry.Priority)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase);

        var rows = new List<IndexerRow>();
        foreach (var entry in entries)
        {
            rows.Add(new IndexerRow(entry, await health.GetAsync(AcquisitionHealthKind.Indexer, entry.Id, cancellationToken)));
        }

        Rows = rows;
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id, CancellationToken cancellationToken)
    {
        var entry = await store.GetAsync(id, cancellationToken);
        if (entry is not null)
        {
            await store.SaveAsync(entry with { Enabled = !entry.Enabled }, cancellationToken);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync(Guid id, CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        var entry = await store.GetAsync(id, cancellationToken);
        if (entry is null || !indexers.TryGetValue(entry.Type, out var indexer))
        {
            TempData["IndexerError"] = ui["settings.indexers.notFound"];
            return RedirectToPage();
        }

        var result = await indexer.TestAsync(entry, cancellationToken);
        await health.RecordAsync(
            new AcquisitionHealthStatus(
                AcquisitionHealthKind.Indexer, entry.Id, entry.Name, true, result.Success, result.Error, DateTimeOffset.UtcNow),
            cancellationToken);

        TempData[result.Success ? "IndexerNotice" : "IndexerError"] = result.Success
            ? (result.Version is null
                ? ui.Format("settings.indexers.connected", ("name", entry.Name))
                : ui.Format("settings.indexers.connectedWithVersion", ("name", entry.Name), ("version", result.Version)))
            : result.Error ?? ui["settings.indexers.connectionFailed"];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        await store.DeleteAsync(id, cancellationToken);
        await health.RemoveAsync(AcquisitionHealthKind.Indexer, id, cancellationToken);
        TempData["IndexerNotice"] = ui["settings.indexers.removed"];
        return RedirectToPage();
    }
}
