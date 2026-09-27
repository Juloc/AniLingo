using Jularr.Web.Data;
using Jularr.Web.Features.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Companion;

[AllowAnonymous]
public sealed class IndexModel(AppDbContext db) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
    }
}
