using Jularr.Web.Data;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Shell;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Reading;

public sealed class IndexModel(AppDbContext db, IAppShellService appShell) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    /// <summary>The hub lists only the reading media types the profile may browse (#598).</summary>
    public bool ShowNovels { get; private set; }
    public bool ShowManga { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var media = await appShell.GetMediaAccessAsync(User, cancellationToken);
        ShowNovels = media.CanOpen("/Novels");
        ShowManga = media.CanOpen("/Manga");
    }
}
