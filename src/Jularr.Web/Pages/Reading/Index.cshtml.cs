using Jularr.Web.Data;
using Jularr.Web.Features.Localization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Reading;

public sealed class IndexModel(AppDbContext db) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public async Task OnGetAsync()
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
    }
}
