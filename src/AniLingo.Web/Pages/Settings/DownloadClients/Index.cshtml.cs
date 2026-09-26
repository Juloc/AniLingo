using AniLingo.Web.Features.Acquisition.DownloadClients;
using AniLingo.Web.Features.Acquisition.Health;
using AniLingo.Web.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings.DownloadClients;

public sealed record DownloadClientRow(DownloadClientEntry Entry, AcquisitionHealthStatus? Health);

/// <summary>
/// The canonical download client list: SABnzbd and qBittorrent connections, each with priority,
/// enable/disable, test and health status. The pipeline and Books submission pick the
/// highest-priority enabled, healthy client that supports a release's protocol.
/// </summary>
[Authorize(Roles = AccountRoles.Owner)]
public sealed class IndexModel(
    DownloadClientStore store,
    IReadOnlyDictionary<DownloadClientType, IDownloadClient> clients,
    AcquisitionHealthStore health) : PageModel
{
    public IReadOnlyList<DownloadClientRow> Rows { get; private set; } = [];
    public string? Notice => TempData["DownloadClientNotice"] as string;
    public string? Error => TempData["DownloadClientError"] as string;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
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
        var entry = await store.GetAsync(id, cancellationToken);
        if (entry is null || !clients.TryGetValue(entry.Type, out var client))
        {
            TempData["DownloadClientError"] = "Download client not found.";
            return RedirectToPage();
        }

        var result = await client.TestAsync(entry, cancellationToken);
        await health.RecordAsync(
            new AcquisitionHealthStatus(
                AcquisitionHealthKind.DownloadClient, entry.Id, entry.Name, true, result.Success, result.Error, DateTimeOffset.UtcNow),
            cancellationToken);

        TempData[result.Success ? "DownloadClientNotice" : "DownloadClientError"] = result.Success
            ? $"Connected to '{entry.Name}'{(result.Version is null ? "" : $" ({result.Version})")}."
            : result.Error ?? "Connection failed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(id, cancellationToken);
        await health.RemoveAsync(AcquisitionHealthKind.DownloadClient, id, cancellationToken);
        TempData["DownloadClientNotice"] = "Download client removed.";
        return RedirectToPage();
    }
}
