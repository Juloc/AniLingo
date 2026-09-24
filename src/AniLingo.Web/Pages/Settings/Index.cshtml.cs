using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Storage;
using AniLingo.Web.Features.Subtitles;
using AniLingo.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Settings;

public sealed record LibraryRootSettingsRow(
    Guid Id,
    string Name,
    string Path,
    DateTime? LastScannedAt,
    bool WakeOnLanEnabled,
    string? WakeMacAddress,
    string? WakeBroadcastAddress,
    LibraryRootAvailabilitySnapshot Availability)
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

public sealed class IndexModel(
    AppDbContext db,
    BackgroundJobQueue jobs,
    LibraryRootAvailabilityService availability,
    WakeOnLanService wakeOnLan) : PageModel
{
    public IReadOnlyList<LibraryRootSettingsRow> Roots { get; private set; } = [];
    public bool IsOwner => User.IsInRole(AccountRoles.Owner);

    [BindProperty]
    public string Name { get; set; } = "Anime";

    [BindProperty]
    public string Path { get; set; } = "/media/anime";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (IsOwner)
        {
            await LoadAsync(cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostAddAsync(CancellationToken cancellationToken)
    {
        if (!IsOwner)
        {
            return Forbid();
        }

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
        if (!IsOwner)
        {
            return Forbid();
        }

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
        if (!IsOwner)
        {
            return Forbid();
        }

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
        if (!IsOwner)
        {
            return Forbid();
        }

        var result = await wakeOnLan.WakeAsync(rootId, cancellationToken);
        if (result.Availability is null &&
            !await db.LibraryRoots.AnyAsync(x => x.Id == rootId, cancellationToken))
        {
            return NotFound();
        }

        TempData["Status"] = result.Message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostScanAsync(Guid rootId, CancellationToken cancellationToken)
    {
        if (!IsOwner)
        {
            return Forbid();
        }

        if (!await db.LibraryRoots.AnyAsync(x => x.Id == rootId, cancellationToken))
        {
            return NotFound();
        }

        var storage = await availability.CheckAsync(
            rootId,
            force: true,
            cancellationToken);

        if (storage is not { IsAvailable: true })
        {
            TempData["Status"] =
                "Library scan was not started because media storage is not currently readable.";
            return RedirectToPage();
        }

        await jobs.QueueAsync(
            async (services, jobToken) =>
            {
                var scanner = services.GetRequiredService<LibraryScanner>();
                await scanner.ScanAsync(rootId, jobToken);

                var subtitles =
                    services.GetRequiredService<SubtitleImportService>();
                await subtitles.QueueAllMissingAsync(jobToken);
            },
            cancellationToken);

        TempData["Status"] = "Library scan queued.";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var roots = await db.LibraryRoots
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var rows = new List<LibraryRootSettingsRow>(roots.Count);
        foreach (var root in roots)
        {
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

            rows.Add(new LibraryRootSettingsRow(
                root.Id,
                root.Name,
                root.Path,
                root.LastScannedAt,
                root.WakeOnLanEnabled,
                root.WakeMacAddress,
                root.WakeBroadcastAddress,
                current));
        }

        Roots = rows;
    }
}
