using Jularr.Web.Data;
using Jularr.Web.Features.Appearance;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Instance;
using Jularr.Web.Features.Learning;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Shell;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Profile;

/// <summary>
/// Profile: the signed-in user's account, activity, downloads, settings and (for the owner)
/// admin, plus the destinations the phone bottom bar has no room for. <c>/Profile/settings</c>
/// and <c>/Profile/admin</c> are the drill-in lists of those sections. All lists come from
/// <see cref="UiNavigationCatalog"/>.
/// </summary>
public sealed class IndexModel(
    AppDbContext db,
    CurrentAccountContext account,
    IAppShellService appShell,
    IInstanceModuleService instanceModules) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public IReadOnlyList<UiNavigationItem> Links { get; private set; } = [];
    public IReadOnlyList<UiNavigationItem> Elsewhere { get; private set; } = [];

    /// <summary>The drill-in section (Settings or Admin), or null on the Profile list itself.</summary>
    public UiNavigationItem? Section { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? section, CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        var instanceSettings = await instanceModules.GetAsync(cancellationToken);
        var enabledModules = instanceSettings.Modules
            .Where(pair => pair.Value)
            .Select(pair => pair.Key)
            .ToHashSet();

        if (section is not null)
        {
            // Admin needs admin.media; for everyone else the drill-in does not exist.
            Section = UiShellNavigation.BuildSection(section, account.Can, enabledModules);
            return Section is null ? NotFound() : Page();
        }

        var learningVisible = instanceSettings.IsEnabled(InstanceModule.Learning)
            && await new LearningConfigurationStore(db)
                .HasAnyLearningEnabledAsync(account.ProfileId, cancellationToken);
        var media = await appShell.GetMediaAccessAsync(User, cancellationToken);
        (Links, Elsewhere) = UiShellNavigation.BuildProfile(
            learningVisible,
            account.Can,
            media.VisibleMediaTypes,
            enabledModules);

        // The shell account footer (theme, sign out, version) is shown here on phones and
        // reads the same view data the layout sets for the sidebar.
        var appearance = await new ProfileAppearanceStore(db).GetAsync(account.ProfileId, cancellationToken);
        ViewData["UiTextBundle"] = Ui;
        ViewData["AppThemeMode"] = appearance.ThemeMode;
        return Page();
    }
}
