using AniLingo.Web.Data;
using AniLingo.Web.Features.Acquisition.Api;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings.ApiKeys;

/// <summary>
/// Owner-only management of acquisition automation API keys: create (the raw key is shown exactly
/// once, in <see cref="CreatedRawKey"/>, on the redirect back to this page after
/// <see cref="OnPostCreateAsync"/>) and revoke. Nothing but a SHA-256 hash of the key is ever
/// persisted, so a reload of this page never shows it again.
/// </summary>
[Authorize(Roles = AccountRoles.Owner)]
public sealed class IndexModel(AcquisitionApiKeyService keys, AppDbContext db) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public IReadOnlyList<AcquisitionApiKey> Keys { get; private set; } = [];

    public string? CreatedKeyName => TempData["ApiKeyCreatedName"] as string;
    public string? CreatedRawKey => TempData["ApiKeyCreatedRaw"] as string;
    public string? Notice => TempData["ApiKeyNotice"] as string;
    public string? Error => TempData["ApiKeyError"] as string;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        Keys = await keys.ListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCreateAsync(string name, CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["ApiKeyError"] = Ui["settings.apiKeys.nameRequired"];
            return RedirectToPage();
        }

        var (key, rawKey) = await keys.CreateAsync(name.Trim(), cancellationToken);
        TempData["ApiKeyCreatedName"] = key.Name;
        TempData["ApiKeyCreatedRaw"] = rawKey;
        TempData["ApiKeyNotice"] = Ui.Format("settings.apiKeys.createdNotice", ("name", key.Name));
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        var revoked = await keys.RevokeAsync(id, cancellationToken);
        TempData[revoked ? "ApiKeyNotice" : "ApiKeyError"] = revoked
            ? Ui["settings.apiKeys.revokedNotice"]
            : Ui["settings.apiKeys.revokeUnknown"];
        return RedirectToPage();
    }
}
