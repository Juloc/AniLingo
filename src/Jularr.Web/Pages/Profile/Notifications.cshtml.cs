using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Events;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Profile;

/// <summary>
/// Per-profile event preferences (#429): which categories send an in-app notification. Admin-only
/// categories (system/infrastructure problems) only show up for owner/media-manager profiles,
/// mirroring who they are ever delivered to.
/// </summary>
public sealed class NotificationsModel(
    AppDbContext db,
    NotificationSubscriptionStore subscriptions,
    CurrentAccountContext account) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public IReadOnlyDictionary<JularrEventCategory, NotificationMode> Preferences { get; private set; } =
        new Dictionary<JularrEventCategory, NotificationMode>();

    public bool IsAdmin { get; private set; }

    /// <summary>Categories a profile may configure: admin-audience ones only for owner/media manager.</summary>
    public IEnumerable<JularrEventCategory> VisibleCategories =>
        Enum.GetValues<JularrEventCategory>()
            .Where(category => IsAdmin || JularrEventCategories.Of(category).Audience == JularrEventAudience.Profile);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        IsAdmin = account.Can(JularrPolicies.AdminMedia);
        Preferences = await subscriptions.GetAllAsync(account.ProfileId, cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        IsAdmin = account.Can(JularrPolicies.AdminMedia);

        foreach (var category in VisibleCategories)
        {
            var raw = Request.Form[$"mode.{category}"].ToString();
            if (Enum.TryParse<NotificationMode>(raw, out var mode) && mode is NotificationMode.Off or NotificationMode.InApp)
            {
                await subscriptions.SetAsync(account.ProfileId, category, mode, cancellationToken);
            }
        }

        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        TempData["Status"] = Ui["notifications.settings.saved"];
        return RedirectToPage();
    }
}
