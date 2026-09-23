using System.Security.Claims;

namespace AniLingo.Web.Features.Auth;

public sealed class CurrentAccountContext(IHttpContextAccessor accessor)
{
    public string ProfileId =>
        accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Authenticated account ID is unavailable.");

    public bool IsOwner =>
        accessor.HttpContext?.User.IsInRole(AccountRoles.Owner) == true;
}
