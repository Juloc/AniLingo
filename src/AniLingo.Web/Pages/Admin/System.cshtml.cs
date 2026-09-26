using AniLingo.Web.Data;
using AniLingo.Web.Features.Acquisition.DownloadClients;
using AniLingo.Web.Features.Acquisition.Health;
using AniLingo.Web.Features.Acquisition.Indexers;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Features.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Admin;

public sealed record AdminLibraryRootRow(
    Guid Id,
    string Name,
    string Path,
    DateTime? LastScannedAt,
    bool WakeOnLanEnabled,
    string? WakeMacAddress,
    string? WakeBroadcastAddress,
    LibraryRootAvailabilitySnapshot Availability,
    int ReconciliationIntervalMinutes,
    AdminLibraryScanRow? ActiveScan,
    AdminLibraryScanRow? LastScan)
{
    public string AvailabilityLabel =>
        Availability.State switch
        {
            StorageAvailabilityState.Available => "Online",
            StorageAvailabilityState.Starting => "Starting",
            StorageAvailabilityState.Offline => "Offline",
            StorageAvailabilityState.Unreachable => "Unreachable",
            StorageAvailabilityState.FileMissing => "File missing",
            _ => "Unknown"
        };

    public string AvailabilityCss =>
        Availability.State switch
        {
            StorageAvailabilityState.Available => "status-ok",
            StorageAvailabilityState.Starting => "status-warning",
            StorageAvailabilityState.Unknown => "status-warning",
            _ => "status-error"
        };
}

[Authorize(Roles = AccountRoles.Owner)]
public sealed class SystemModel(
    AppDbContext db,
    LibraryScanCoordinator scans,
    CurrentAccountContext account,
    LibraryRootAvailabilityService availability,
    WakeOnLanService wakeOnLan,
    IndexerStore indexerStore,
    DownloadClientStore downloadClientStore,
    AcquisitionHealthStore acquisitionHealth) : PageModel
{
    public IReadOnlyList<AdminLibraryRootRow> Roots { get; private set; } = [];
    public int TotalIndexers { get; private set; }
    public int HealthyIndexers { get; private set; }
    public int TotalDownloadClients { get; private set; }
    public int HealthyDownloadClients { get; private set; }

    [BindProperty]
    public string Name { get; set; } = "Anime";

    [BindProperty]
    public string Path { get; set; } = "/media/anime";

    public Task OnGetAsync(CancellationToken cancellationToken) =>
        LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostAddAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Path))
        {
            ModelState.AddModelError(string.Empty, "Name and path are required.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var fullPath = System.IO.Path.GetFullPath(Path.Trim());
        if (await db.LibraryRoots.AnyAsync(x => x.Path == fullPath, cancellationToken))
        {
            ModelState.AddModelError(string.Empty, "This library root already exists.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        db.LibraryRoots.Add(new LibraryRoot
        {
            Name = Name.Trim(),
            Path = fullPath
        });
        await db.SaveChangesAsync(cancellationToken);

        TempData["Status"] = "Library root added.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostConfigureWakeAsync(
        Guid rootId,
        bool wakeOnLanEnabled,
        string? wakeMacAddress,
        string? wakeBroadcastAddress,
        CancellationToken cancellationToken)
    {
        var root = await db.LibraryRoots
            .SingleOrDefaultAsync(x => x.Id == rootId, cancellationToken);
        if (root is null)
        {
            return NotFound();
        }

        string? normalizedMac = null;
        if (!string.IsNullOrWhiteSpace(wakeMacAddress) &&
            !WakeOnLanService.TryNormalizeMacAddress(
                wakeMacAddress,
                out normalizedMac))
        {
            ModelState.AddModelError(
                string.Empty,
                "Enter a valid 6-byte MAC address.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        if (wakeOnLanEnabled && normalizedMac is null)
        {
            ModelState.AddModelError(
                string.Empty,
                "Enter a MAC address before enabling Wake-on-LAN.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        if (!WakeOnLanService.TryResolveBroadcastAddress(
                wakeBroadcastAddress,
                out _))
        {
            ModelState.AddModelError(
                string.Empty,
                "Wake-on-LAN broadcast must be a valid IPv4 address.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        root.WakeOnLanEnabled = wakeOnLanEnabled;
        root.WakeMacAddress = normalizedMac;
        root.WakeBroadcastAddress = string.IsNullOrWhiteSpace(wakeBroadcastAddress)
            ? null
            : wakeBroadcastAddress.Trim();

        await db.SaveChangesAsync(cancellationToken);
        TempData["Status"] = wakeOnLanEnabled
            ? "Wake-on-LAN settings saved."
            : "Wake-on-LAN disabled for this root.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestRootAsync(
        Guid rootId,
        CancellationToken cancellationToken)
    {
        var status = await availability.CheckAsync(
            rootId,
            force: true,
            cancellationToken);

        if (status is null)
        {
            return NotFound();
        }

        TempData["Status"] = status.State switch
        {
            StorageAvailabilityState.Available =>
                "Media storage is online and readable.",
            StorageAvailabilityState.Starting =>
                "Media storage is starting; AniLingo will keep checking during playback.",
            StorageAvailabilityState.Offline =>
                "Media storage is currently offline.",
            StorageAvailabilityState.Unreachable =>
                $"Media storage cannot be read ({status.DiagnosticCode ?? "unreachable"}).",
            _ => "Media storage availability is unknown."
        };

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostWakeRootAsync(
        Guid rootId,
        CancellationToken cancellationToken)
    {
        var result = await wakeOnLan.WakeAsync(rootId, cancellationToken);
        if (result.Availability is null &&
            !await db.LibraryRoots.AnyAsync(x => x.Id == rootId, cancellationToken))
        {
            return NotFound();
        }

        TempData["Status"] = result.Message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostScanAsync(
        Guid rootId,
        CancellationToken cancellationToken)
    {
        var result = await scans.QueueAsync(
            new LibraryScanRequest(rootId, LibraryScanTrigger.Manual, ProfileId: account.ProfileId),
            cancellationToken);

        if (result.Outcome == LibraryScanQueueOutcome.RootNotFound)
        {
            return NotFound();
        }

        TempData["Status"] = result.Message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostConfigureReconciliationAsync(
        Guid rootId,
        int reconciliationIntervalMinutes,
        CancellationToken cancellationToken)
    {
        var root = await db.LibraryRoots
            .SingleOrDefaultAsync(x => x.Id == rootId, cancellationToken);
        if (root is null)
        {
            return NotFound();
        }

        if (reconciliationIntervalMinutes != 0 &&
            (reconciliationIntervalMinutes < LibraryRoot.MinimumReconciliationIntervalMinutes ||
             reconciliationIntervalMinutes > LibraryRoot.MaximumReconciliationIntervalMinutes))
        {
            ModelState.AddModelError(
                string.Empty,
                $"The reconciliation interval must be 0 (off) or between {LibraryRoot.MinimumReconciliationIntervalMinutes} and {LibraryRoot.MaximumReconciliationIntervalMinutes} minutes.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        root.ReconciliationIntervalMinutes = reconciliationIntervalMinutes;
        await db.SaveChangesAsync(cancellationToken);

        TempData["Status"] = reconciliationIntervalMinutes == 0
            ? "Periodic reconciliation disabled for this root."
            : $"Periodic reconciliation set to every {reconciliationIntervalMinutes} minutes.";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var roots = await db.LibraryRoots
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var rootNames = roots.ToDictionary(x => x.Id, x => x.Name);
        var scanRows = (await new OperationStore(db).ListAsync(
                new OperationListFilter(
                    Kind: LibraryScanCoordinator.OperationKind,
                    Limit: LibraryScanCoordinator.HistoryLimit),
                cancellationToken))
            .Select(operation => AdminLibraryScanRow.From(operation, rootNames))
            .Where(row => row.Details is not null)
            .ToArray();

        var rows = new List<AdminLibraryRootRow>(roots.Count);
        foreach (var root in roots)
        {
            var activeScan = scanRows.FirstOrDefault(row =>
                row.Details!.RootId == root.Id && row.Operation.IsActive);
            var lastScan = scanRows
                .Where(row => row.Details!.RootId == root.Id && !row.Operation.IsActive)
                .OrderByDescending(row => row.Operation.FinishedAtUtc ?? row.Operation.UpdatedAtUtc)
                .FirstOrDefault();

            var current = availability.GetCached(root)
                ?? await availability.CheckAsync(
                    root.Id,
                    force: false,
                    cancellationToken)
                ?? new LibraryRootAvailabilitySnapshot(
                    root.Id,
                    StorageAvailabilityState.Unknown,
                    DateTimeOffset.UtcNow,
                    null,
                    false);

            rows.Add(new AdminLibraryRootRow(
                root.Id,
                root.Name,
                root.Path,
                root.LastScannedAt,
                root.WakeOnLanEnabled,
                root.WakeMacAddress,
                root.WakeBroadcastAddress,
                current,
                root.ReconciliationIntervalMinutes,
                activeScan,
                lastScan));
        }

        Roots = rows;

        var indexers = await indexerStore.LoadAllAsync(cancellationToken);
        TotalIndexers = indexers.Count;
        HealthyIndexers = 0;
        foreach (var indexer in indexers)
        {
            if (await acquisitionHealth.IsHealthyAsync(AcquisitionHealthKind.Indexer, indexer.Id, cancellationToken))
            {
                HealthyIndexers++;
            }
        }

        var downloadClients = await downloadClientStore.LoadAllAsync(cancellationToken);
        TotalDownloadClients = downloadClients.Count;
        HealthyDownloadClients = 0;
        foreach (var client in downloadClients)
        {
            if (await acquisitionHealth.IsHealthyAsync(AcquisitionHealthKind.DownloadClient, client.Id, cancellationToken))
            {
                HealthyDownloadClients++;
            }
        }
    }
}
