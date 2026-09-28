using Jularr.Web.Data;
using Jularr.Web.Features.Appearance;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Admin;

/// <summary>Controls the instance default and which personal appearance overrides are permitted.</summary>
[Authorize(Policy = JularrPolicies.AdminSystem)]
public sealed class AppearanceModel(AppDbContext db) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public IReadOnlyList<AppThemeDefinition> Themes => ThemeCatalog.All;

    [BindProperty]
    public string DefaultThemeId { get; set; } = ThemeCatalog.Original;

    [BindProperty]
    public bool AllowProfileThemeOverride { get; set; }

    [BindProperty]
    public bool AllowProfileAccentOverride { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        if (!ThemeCatalog.TryGet(DefaultThemeId, out var theme))
        {
            ModelState.AddModelError(nameof(DefaultThemeId), Ui["admin.appearance.invalidTheme"]);
            return Page();
        }

        await new InstanceAppearanceSettingsStore(db).SaveAsync(
            new InstanceAppearanceSettings(
                theme.Id,
                AllowProfileThemeOverride,
                AllowProfileAccentOverride),
            cancellationToken);

        TempData["Status"] = Ui["admin.appearance.saved"];
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        var settings = await new InstanceAppearanceSettingsStore(db).LoadAsync(cancellationToken);
        DefaultThemeId = settings.DefaultThemeId;
        AllowProfileThemeOverride = settings.AllowProfileThemeOverride;
        AllowProfileAccentOverride = settings.AllowProfileAccentOverride;
    }
}
