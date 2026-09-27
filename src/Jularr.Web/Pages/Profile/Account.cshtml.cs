using Jularr.Web.Data;
using Jularr.Web.Features.Localization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Profile;

/// <summary>
/// The signed-in user's own account. There is no self-service password change for users yet
/// (the owner resets passwords in Admin), so the page shows the display name and sign out.
/// </summary>
public sealed class AccountModel(AppDbContext db) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public string DisplayName => User.Identity?.Name ?? string.Empty;

    public async Task OnGetAsync()
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
    }
}
