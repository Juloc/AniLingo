using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Instance;
using Jularr.Web.Features.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Admin;

/// <summary>Owner-only server-wide module switches. Disabling a module never deletes its data.</summary>
[Authorize(Policy = JularrPolicies.AdminSystem)]
public sealed class InstanceModel(
    AppDbContext db,
    IInstanceModuleService modules) : PageModel
{
    public static IReadOnlyList<InstanceModule> MediaModules { get; } =
    [
        InstanceModule.Anime,
        InstanceModule.Movie,
        InstanceModule.Tv,
        InstanceModule.Manga,
        InstanceModule.Novel,
        InstanceModule.Book,
        InstanceModule.Audiobook
    ];

    public static IReadOnlyList<InstanceModule> FeatureModules { get; } =
    [
        InstanceModule.Learning,
        InstanceModule.Acquisition,
        InstanceModule.Tracking
    ];

    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public InstanceModuleSettings Settings { get; private set; } =
        InstanceModuleSettings.Default;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var values = Enum.GetValues<InstanceModule>()
            .ToDictionary(
                module => module,
                module => Request.Form.ContainsKey(FieldName(module)));

        await modules.SaveAsync(
            new InstanceModuleSettings(values),
            cancellationToken);

        TempData["Status"] = Ui["admin.instance.saved"];
        return RedirectToPage();
    }

    public static string FieldName(InstanceModule module) =>
        $"module_{module}";

    public string Name(InstanceModule module) =>
        Ui[$"admin.instance.module.{StorageName(module)}"];

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        Settings = await modules.GetAsync(cancellationToken);
    }

    private static string StorageName(InstanceModule module) =>
        module switch
        {
            InstanceModule.Tv => "tv",
            _ => module.ToString().ToLowerInvariant()
        };
}
