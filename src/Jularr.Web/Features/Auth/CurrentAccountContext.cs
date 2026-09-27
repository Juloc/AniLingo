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
}
