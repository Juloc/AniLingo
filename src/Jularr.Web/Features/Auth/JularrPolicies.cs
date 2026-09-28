using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Jularr.Web.Features.Auth;

/// <summary>
/// Every named authorization policy and the roles it admits, in one table
/// (docs/INFORMATION_ARCHITECTURE.md §8). Program.cs registers these policies, page
/// attributes and handler checks refer to them by name, and navigation filters on them.
/// </summary>
public static class JularrPolicies
{
    /// <summary>Day-to-day media operations: library, acquisition, queue, subtitles, scans, operations, logs, requests.</summary>
    public const string AdminMedia = "admin.media";

    /// <summary>Users, integrations, API keys, storage roots, AI providers and localization.</summary>
    public const string AdminSystem = "admin.system";

    /// <summary>Deleting media or library files.</summary>
    public const string MediaDelete = "media.delete";

    /// <summary>Renaming or moving library files.</summary>
    public const string MediaRename = "media.rename";

    /// <summary>Changing metadata or chapter mappings.</summary>
    public const string MappingEdit = "mapping.edit";

    /// <summary>Indexers, download clients, import and naming profiles, reading sources, request policy.</summary>
    public const string AcquisitionSettings = "acquisition.settings";

    /// <summary>Stopping another user's playback session.</summary>
    public const string SessionsStopOthers = "sessions.stop-others";

    private static readonly AccountRole[] OwnerOnly = [AccountRole.Owner];
    private static readonly AccountRole[] OwnerAndMediaManager = [AccountRole.Owner, AccountRole.MediaManager];

    public static IReadOnlyDictionary<string, IReadOnlyList<AccountRole>> Roles { get; } =
        new Dictionary<string, IReadOnlyList<AccountRole>>(StringComparer.Ordinal)
        {
            [AdminMedia] = OwnerAndMediaManager,
            [AdminSystem] = OwnerOnly,
            [MediaDelete] = OwnerOnly,
            [MediaRename] = OwnerOnly,
            [MappingEdit] = OwnerAndMediaManager,
            [AcquisitionSettings] = OwnerAndMediaManager,
            [SessionsStopOthers] = OwnerAndMediaManager
        };

    public static void Register(AuthorizationOptions options)
    {
        foreach (var (name, roles) in Roles)
        {
            options.AddPolicy(name, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(roles.Select(role => role.ToString())));
        }
    }

    /// <summary>
    /// Evaluates a policy against a principal without the authorization service. Every policy
    /// is role-based, so this matches what the registered policy decides.
    /// </summary>
    public static bool Allows(ClaimsPrincipal? user, string policy)
    {
        if (!Roles.TryGetValue(policy, out var roles))
        {
            throw new ArgumentException($"Unknown authorization policy '{policy}'.", nameof(policy));
        }

        return user?.Identity?.IsAuthenticated == true
            && roles.Any(role => user.IsInRole(role.ToString()));
    }
}
