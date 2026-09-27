using System.Security.Claims;
using Jularr.Web.Features.Operations;

namespace Jularr.Web.Features.Auth;

public sealed class CurrentAccountContext(
    IHttpContextAccessor accessor,
    OperationProfileContext? operationProfile = null)
{
    public string ProfileId =>
        accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? operationProfile?.ProfileId
        ?? throw new InvalidOperationException("Authenticated account ID is unavailable.");

    public bool IsOwner =>
        accessor.HttpContext?.User.IsInRole(AccountRoles.Owner) == true;

    /// <summary>
    /// Account context for background work on behalf of one profile. It is
    /// instance-bound, so concurrent jobs for different profiles never mix.
    /// </summary>
    public static CurrentAccountContext ForProfile(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        return new CurrentAccountContext(new ProfileAccessor(profileId));
    }

    private sealed class ProfileAccessor(string profileId) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, profileId)],
                    "background"))
        };
    }
}
