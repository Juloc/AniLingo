using AniLingo.Web.Data;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Reading;

public sealed class IndexModel(AppDbContext db) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public async Task OnGetAsync()
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
    }
}
