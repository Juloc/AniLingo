using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Collections;
using Jularr.Web.Features.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Collections;

public sealed class IndexModel(
    AppDbContext db,
    CurrentAccountContext account,
    CollectionService collections) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public IReadOnlyList<CollectionSummaryView> Collections { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        Collections = await collections.ListAsync(account.ProfileId, cancellationToken);
    }

    public async Task<IActionResult> OnPostCreateAsync(
        string? name,
        string? description,
        string? kind,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Status"] = "A collection needs a name.";
            return RedirectToPage();
        }

        var collectionKind = string.Equals(kind, "smart", StringComparison.OrdinalIgnoreCase)
            ? CollectionKind.Smart
            : CollectionKind.Manual;

        var created = await collections.CreateAsync(
            account.ProfileId,
            collectionKind,
            name,
            description,
            rule: null,
            cancellationToken);

        // A smart collection is only useful once it has a rule, so send the owner straight to the builder.
        return collectionKind == CollectionKind.Smart
            ? RedirectToPage("/Collections/Rules", new { id = created.Id })
            : RedirectToPage("/Collections/Detail", new { id = created.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await collections.DeleteAsync(account.ProfileId, id, cancellationToken);
        TempData["Status"] = "Collection deleted.";
        return RedirectToPage();
    }
}
