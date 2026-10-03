using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Admin;

[Authorize(Policy = JularrPolicies.AdminSystem)]
public sealed class GroupsModel(
    AppDbContext db,
    AccountGroupStore groups) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public IReadOnlyList<AccountGroupSummary> Groups { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostCreateAsync(
        string name,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        try
        {
            await groups.CreateAsync(name, cancellationToken);
            TempData["Status"] = Ui["admin.groups.created"];
            return RedirectToPage();
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRenameAsync(
        string groupId,
        string name,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        try
        {
            await groups.RenameAsync(groupId, name, cancellationToken);
            TempData["Status"] = Ui["admin.groups.saved"];
            return RedirectToPage();
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(
        string groupId,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        try
        {
            await groups.DeleteAsync(groupId, cancellationToken);
            TempData["Status"] = Ui["admin.groups.deleted"];
            return RedirectToPage();
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        Groups = await groups.ListAsync(cancellationToken);
    }
}
