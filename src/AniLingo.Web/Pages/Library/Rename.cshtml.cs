using AniLingo.Web.Data;
using AniLingo.Web.Features.Acquisition.Naming;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Library;

// Per-anime naming selection plus the rename preview and its confirmed execution.
[Authorize(Roles = AccountRoles.Owner)]
public sealed class RenameModel(
    AppDbContext db,
    AnimeRenameService renameService,
    AnimeNamingProfileStore namingStore) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public bool Folder { get; set; }

    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public Guid AnimeId { get; private set; }
    public AnimeRenamePlan? Plan { get; private set; }
    public AnimeNamingState Naming { get; private set; } = AnimeNamingPresets.CreateDefaultState();
    public AnimeNamingAssignment? Assignment { get; private set; }
    public string? Notice => TempData["RenameNotice"] as string;
    public string? Error => TempData["RenameError"] as string;
    public Guid? OperationId => TempData["RenameOperation"] is string value && Guid.TryParse(value, out var id) ? id : null;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        AnimeId = id;
        Naming = await namingStore.LoadAsync(cancellationToken);
        Naming.AnimeAssignments.TryGetValue(id.ToString("D"), out var assignment);
        Assignment = assignment;
        Plan = await renameService.PlanAsync(id, Folder, cancellationToken);
        return Plan is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostSelectionAsync(
        Guid id,
        string? profileId,
        AnimeSeriesType seriesType,
        CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        try
        {
            await namingStore.AssignAnimeAsync(id, profileId, seriesType, cancellationToken);
            TempData["RenameNotice"] = ui["library.rename.selectionSaved"];
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            TempData["RenameError"] = exception.Message;
        }

        return RedirectToPage(new { id, folder = Folder });
    }

    public async Task<IActionResult> OnPostExecuteAsync(
        Guid id,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var result = await renameService.ExecuteAsync(id, Folder, fingerprint ?? "", cancellationToken);
        TempData[result.Success ? "RenameNotice" : "RenameError"] = result.Message;
        if (result.OperationId is Guid operationId)
        {
            TempData["RenameOperation"] = operationId.ToString("D");
        }

        return RedirectToPage(new { id, folder = Folder });
    }

    public static string Display(string path, string seriesFolder)
    {
        var root = Path.GetDirectoryName(seriesFolder);
        return root is null ? path : Path.GetRelativePath(root, path);
    }
}
