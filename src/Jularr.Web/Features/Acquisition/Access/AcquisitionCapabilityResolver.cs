using System.Security.Claims;
using Jularr.Web.Features.Auth;

namespace Jularr.Web.Features.Acquisition.Access;

/// <summary>
/// Resolves the request/add capability of an acquisition kind without forcing every content domain
/// into MediaCore. Existing media kinds keep using the canonical MediaCapability matrix. Games is
/// intentionally outside MediaCore and therefore uses the same conservative role defaults until a
/// Games-specific profile capability editor is introduced.
/// </summary>
public static class AcquisitionCapabilityResolver
{
    public static async Task<MediaCapability> ResolveAsync(
        MediaAcquisitionKind kind,
        ClaimsPrincipal? user,
        IMediaCapabilityService mediaCapabilities,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediaCapabilities);

        if (AcquisitionAccessNames.TryWorkType(kind, out var mediaType))
        {
            return await mediaCapabilities.GetEffectiveCapabilityAsync(
                user,
                mediaType,
                cancellationToken);
        }

        if (kind != MediaAcquisitionKind.Game)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (user?.Identity?.IsAuthenticated != true)
        {
            return MediaCapability.Hidden;
        }

        // Match the existing capability matrix's defaults without inventing a fake WorkMediaType:
        // Owner/MediaManager may add immediately; ordinary authenticated users create requests.
        return JularrPolicies.Allows(user, JularrPolicies.AdminMedia)
            ? MediaCapability.Instant
            : MediaCapability.Request;
    }
}
