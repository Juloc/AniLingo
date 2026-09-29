using Jularr.Web.Data;
using Jularr.Web.Features.Auth;

namespace Jularr.Web.Features.Acquisition.Access;

/// <summary>
/// One-time move of the retired per-media-type "adding from search" rule into the capability matrix
/// (#436). Before, the owner chose per media type whether other users could not add (<c>disabled</c>),
/// request (<c>request</c>, the default) or add at once (<c>automatic</c>). That is now the role default
/// of the <c>User</c> role in the matrix: <c>disabled</c> becomes Browse and <c>automatic</c> becomes
/// Instant, unless the owner already changed that role default in the matrix. Afterwards the old column
/// is reset to its default so nothing is migrated twice and the value is never read again. Running it
/// again is a no-op.
/// </summary>
public static class UserAddModeMigration
{
    public static async Task<int> MigrateAsync(
        AcquisitionAccessStore store,
        MediaCapabilityStore capabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(capabilities);

        var legacy = await store.ListLegacyUserAddModesAsync(cancellationToken);
        if (legacy.Count == 0)
        {
            return 0;
        }

        var policy = await capabilities.LoadAsync(cancellationToken);
        var migrated = 0;
        foreach (var (kind, mode) in legacy)
        {
            var target = mode switch
            {
                "disabled" => MediaCapability.Browse,
                "automatic" => MediaCapability.Instant,
                _ => (MediaCapability?)null
            };
            var mediaType = AcquisitionAccessNames.WorkType(kind);
            var builtIn = MediaCapabilityPolicy.Default.RoleDefault(AccountRole.User, mediaType);
            if (target is { } capability && policy.RoleDefault(AccountRole.User, mediaType) == builtIn)
            {
                await capabilities.SetRoleDefaultAsync(AccountRole.User, mediaType, capability, cancellationToken);
                migrated++;
            }
        }

        await store.ResetLegacyUserAddModesAsync(cancellationToken);
        return migrated;
    }

    public static async Task RunAtStartupAsync(
        IServiceProvider services,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(log);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var migrated = await MigrateAsync(
            new AcquisitionAccessStore(db),
            scope.ServiceProvider.GetRequiredService<MediaCapabilityStore>(),
            cancellationToken);
        if (migrated > 0)
        {
            log($"Moved {migrated} 'adding from search' rule(s) into the media capability matrix.");
        }
    }
}
