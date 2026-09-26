using AniLingo.Web.Data;
using AniLingo.Web.Features.Appearance;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings;

/// <summary>
/// Personal appearance: theme mode and accent colour. Changes are saved through
/// /Appearance/Theme and /Appearance/Accent and previewed live with the server-rendered palette.
/// </summary>
public sealed class AppearanceModel(
    AppDbContext db,
    CurrentAccountContext currentAccount) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public ProfileAppearance Appearance { get; private set; } = ProfileAppearance.Default;
    public IReadOnlyList<AccentPreset> Presets => AppAccent.Presets;
    public string EffectiveAccent => AppAccent.Effective(Appearance.AccentColor);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        Appearance = await new ProfileAppearanceStore(db).GetAsync(
            currentAccount.ProfileId,
            cancellationToken);
    }
}
