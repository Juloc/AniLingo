using AniLingo.Web.Data;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings;

/// <summary>
/// Settings → Offline (#221 part 1): storage usage, Wi-Fi-only toggle and
/// per-book management of downloaded content. Everything on this page reads
/// browser-local state (IndexedDB/OPFS, wwwroot/js/offline-library*.js); the
/// server side only renders the localized shell.
/// </summary>
public sealed class OfflineModel(AppDbContext db) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public async Task OnGetAsync()
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
    }
}
