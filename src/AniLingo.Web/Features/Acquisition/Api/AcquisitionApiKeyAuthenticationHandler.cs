using System.Security.Claims;
using System.Text.Encodings.Web;
using AniLingo.Web.Features.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AniLingo.Web.Features.Acquisition.Api;

/// <summary>
/// Authenticates the acquisition automation API via the <c>X-Api-Key</c> header. A valid, active
/// key is treated as an owner session scoped to this API: it is only ever wired into the
/// acquisition API's own authorization policy (see Program.cs), never into the cookie-based
/// client/browser surfaces, so a leaked key cannot do anything outside acquisition automation.
/// </summary>
public sealed class AcquisitionApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AcquisitionApiKeyService keys)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AcquisitionApiKey";
    public const string HeaderName = "X-Api-Key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values))
        {
            return AuthenticateResult.NoResult();
        }

        var rawKey = values.ToString();
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return AuthenticateResult.NoResult();
        }

        var key = await keys.ValidateAsync(rawKey, Context.RequestAborted);
        if (key is null)
        {
            return AuthenticateResult.Fail("The API key is invalid or has been revoked.");
        }

        var identity = new ClaimsIdentity(SchemeName);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, key.Id.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Name, key.Name));
        identity.AddClaim(new Claim(ClaimTypes.Role, AccountRoles.Owner));
        identity.AddClaim(new Claim("auth_method", "api_key"));

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Response.WriteAsJsonAsync(new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7235#section-3.1",
            Title = "Authentication required.",
            Status = StatusCodes.Status401Unauthorized,
            Detail = "A valid X-Api-Key header or an authenticated owner session is required."
        }, options: null, contentType: "application/problem+json");
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Response.WriteAsJsonAsync(new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.3",
            Title = "Access denied.",
            Status = StatusCodes.Status403Forbidden,
            Detail = "Only the owner account may use the acquisition automation API."
        }, options: null, contentType: "application/problem+json");
    }
}
