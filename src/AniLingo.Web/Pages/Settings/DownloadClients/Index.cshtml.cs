using AniLingo.Web.Data;
using AniLingo.Web.Features.Acquisition.DownloadClients;
using AniLingo.Web.Features.Acquisition.Health;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings.DownloadClients;

public sealed record DownloadClientRow(DownloadClientEntry Entry, AcquisitionHealthStatus? Health);

/// <summary>
/// The canonical download client list: SABnzbd connections, each with priority, enable/disable,
/// test and health status. The pipeline and Books submission pick the highest-priority enabled,
/// healthy client, failing over to the next one on submission failure.
/// </summary>
[Authorize(Roles = AccountRoles.Owner)]
public sealed class IndexModel(
    AppDbContext db,
    DownloadClientStore store,
    IDownloadClient client,
    AcquisitionHealthStore health) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public IReadOnlyList<DownloadClientRow> Rows { get; private set; } = [];
    public string? Notice => TempData["DownloadClientNotice"] as string;
    public string? Error => TempData["DownloadClientError"] as string;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        var entries = (await store.LoadAllAsync(cancellationToken))
            .OrderBy(entry => entry.Priority)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase);

        var rows = new List<DownloadClientRow>();
        foreach (var entry in entries)
        {
            rows.Add(new DownloadClientRow(entry, await health.GetAsync(AcquisitionHealthKind.DownloadClient, entry.Id, cancellationToken)));
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
        if (entry is null)
        {
            TempData["DownloadClientError"] = ui["settings.downloadClients.notFound"];
            return RedirectToPage();
        }

        var result = await client.TestAsync(entry, cancellationToken);
        await health.RecordAsync(
            new AcquisitionHealthStatus(
                AcquisitionHealthKind.DownloadClient, entry.Id, entry.Name, true, result.Success, result.Error, DateTimeOffset.UtcNow),
            cancellationToken);

        TempData[result.Success ? "DownloadClientNotice" : "DownloadClientError"] = result.Success
            ? (result.Version is null
                ? ui.Format("settings.downloadClients.connected", ("name", entry.Name))
                : ui.Format("settings.downloadClients.connectedWithVersion", ("name", entry.Name), ("version", result.Version)))
            : result.Error ?? ui["settings.downloadClients.connectionFailed"];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        await store.DeleteAsync(id, cancellationToken);
        await health.RemoveAsync(AcquisitionHealthKind.DownloadClient, id, cancellationToken);
        TempData["DownloadClientNotice"] = ui["settings.downloadClients.removed"];
        return RedirectToPage();
    }
}
