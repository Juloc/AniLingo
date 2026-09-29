using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.MediaCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Admin.Capabilities;

/// <summary>
/// Owner-only editor of the per-media-type capability matrix (#436): the default capability of each
/// configurable role and the per-person overrides. It writes the canonical
/// <see cref="MediaCapabilityStore"/> policy that the request experience, permission-derived shell and
/// discovery resolve against.
/// </summary>
[Authorize(Policy = JularrPolicies.AdminSystem)]
public sealed class IndexModel(AppDbContext db, MediaCapabilityStore store, OwnerAuthService accounts) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public MediaCapabilityPolicy Policy { get; private set; } = MediaCapabilityPolicy.Default;

    /// <summary>Non-owner accounts whose overrides can be edited (the owner is always unrestricted).</summary>
    public IReadOnlyList<LocalAccountSummary> Users { get; private set; } = [];

    public IReadOnlyList<WorkMediaType> MediaTypes => WorkMediaTypes.All;

    public IReadOnlyList<AccountRole> Roles => MediaCapabilityPolicy.ConfigurableRoles;

    public IReadOnlyList<MediaCapability> Levels { get; } = Enum.GetValues<MediaCapability>();

    public static string RoleField(AccountRole role, WorkMediaType mediaType) =>
        $"rd__{role}__{WorkMediaTypes.ToStorage(mediaType)}";

    public static string UserField(WorkMediaType mediaType) =>
        $"uo__{WorkMediaTypes.ToStorage(mediaType)}";

    public static string MediaTypeLabelKey(WorkMediaType mediaType) =>
        $"admin.capabilities.mediaType.{WorkMediaTypes.ToStorage(mediaType)}";

    public static string LevelLabelKey(MediaCapability capability) =>
        $"admin.capabilities.level.{MediaCapabilityNames.ToStorage(capability)}";

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostRolesAsync(CancellationToken cancellationToken)
    {
        var policy = await store.LoadAsync(cancellationToken);
        foreach (var role in MediaCapabilityPolicy.ConfigurableRoles)
        {
            foreach (var mediaType in WorkMediaTypes.All)
            {
                if (MediaCapabilityNames.TryParse(Request.Form[RoleField(role, mediaType)].ToString()) is { } capability)
                {
                    policy = policy.WithRoleDefault(role, mediaType, capability);
                }
            }
        }

        await store.SaveAsync(policy, cancellationToken);
        return await SavedAsync();
    }

    public async Task<IActionResult> OnPostUserAsync(string profileId, CancellationToken cancellationToken)
    {
        var account = await accounts.GetAsync(profileId, cancellationToken);
        if (account is not null && account.Role != AccountRole.Owner)
        {
            var policy = await store.LoadAsync(cancellationToken);
            foreach (var mediaType in WorkMediaTypes.All)
            {
                // An empty selection means "inherit the role default", stored as no override.
                var capability = MediaCapabilityNames.TryParse(Request.Form[UserField(mediaType)].ToString());
                policy = policy.WithUserOverride(profileId, mediaType, capability);
            }

            await store.SaveAsync(policy, cancellationToken);
        }

        return await SavedAsync();
    }

    public async Task<IActionResult> OnPostResetUserAsync(string profileId, CancellationToken cancellationToken)
    {
        await store.ClearUserAsync(profileId, cancellationToken);
        return await SavedAsync();
    }

    /// <summary>The effective capability shown in the matrix cell for a role and media type.</summary>
    public MediaCapability RoleDefaultFor(AccountRole role, WorkMediaType mediaType) =>
        Policy.RoleDefault(role, mediaType);

    /// <summary>The per-user override, or <c>null</c> when the user inherits its role default.</summary>
    public MediaCapability? UserOverrideFor(string profileId, WorkMediaType mediaType) =>
        Policy.UserOverride(profileId, mediaType);

    /// <summary>The resolved effective capability for a user (override or role default).</summary>
    public MediaCapability EffectiveFor(LocalAccountSummary account, WorkMediaType mediaType) =>
        Policy.Resolve(account.Role, account.Id, mediaType);

    private async Task<IActionResult> SavedAsync()
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        TempData["Status"] = Ui["admin.capabilities.saved"];
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        Policy = await store.LoadAsync(cancellationToken);
        Users = (await accounts.ListAsync(cancellationToken))
            .Where(account => account.Role != AccountRole.Owner)
            .OrderBy(account => account.UserName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
