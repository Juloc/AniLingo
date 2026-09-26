using AniLingo.Web.Features.Acquisition.Api;
using AniLingo.Web.Features.Auth;
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
public sealed class IndexModel(AcquisitionApiKeyService keys) : PageModel
{
    public IReadOnlyList<AcquisitionApiKey> Keys { get; private set; } = [];

    public string? CreatedKeyName => TempData["ApiKeyCreatedName"] as string;
    public string? CreatedRawKey => TempData["ApiKeyCreatedRaw"] as string;
    public string? Notice => TempData["ApiKeyNotice"] as string;
    public string? Error => TempData["ApiKeyError"] as string;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Keys = await keys.ListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCreateAsync(string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["ApiKeyError"] = "Enter a name for the new key.";
            return RedirectToPage();
        }

        var (key, rawKey) = await keys.CreateAsync(name.Trim(), cancellationToken);
        TempData["ApiKeyCreatedName"] = key.Name;
        TempData["ApiKeyCreatedRaw"] = rawKey;
        TempData["ApiKeyNotice"] = $"Key '{key.Name}' created. Copy it now; it will not be shown again.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        var revoked = await keys.RevokeAsync(id, cancellationToken);
        TempData[revoked ? "ApiKeyNotice" : "ApiKeyError"] = revoked
            ? "Key revoked; it can no longer authenticate."
            : "That key was already revoked or does not exist.";
        return RedirectToPage();
    }
}
