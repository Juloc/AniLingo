using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Subtitles;
using AniLingo.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Settings;

public sealed class IndexModel(
    AppDbContext db,
    BackgroundJobQueue jobs) : PageModel
{
    public IReadOnlyList<LibraryRoot> Roots { get; private set; } = [];
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
        Roots = await db.LibraryRoots
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }
}
