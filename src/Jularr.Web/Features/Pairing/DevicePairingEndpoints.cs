using Jularr.Web.Features.Auth;
using Jularr.Web.Features.ClientApi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;

namespace Jularr.Web.Features.Pairing;

/// <summary>
/// Android TV device-code pairing (#489): a fresh TV install shows a short code instead of a
/// server URL/password form; a signed-in phone or browser approves it; the TV exchanges its
/// device code for the exact same cookie session <c>/session/login</c> would have given it. State
/// lives only in <see cref="DevicePairingStore"/> (in-memory, short-TTL, single-use) — see that
/// class for why this deliberately has no database table.
/// </summary>
public static class DevicePairingEndpoints
{
    public static IEndpointRouteBuilder MapPairingApiV1(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"{ClientApiContract.BasePath}/pairing");

        group.MapPost("/start", (DevicePairingStore pairing) =>
            Results.Ok(pairing.Start()))
            .AllowAnonymous()
            .RequireRateLimiting("pairing-start");

        group.MapPost("/approve", (
            DevicePairingApproveRequest request,
            DevicePairingStore pairing,
            CurrentAccountContext account,
            HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(request.UserCode))
            {
                return BadRequest(
                    "invalid_user_code",
                    "userCode is required.");
            }

            var attemptKey = account.ProfileId;
            var outcome = pairing.Approve(request.UserCode, account.ProfileId, attemptKey);

            return outcome switch
            {
                DevicePairingApproveOutcome.Approved =>
                    Results.Ok(new DevicePairingApproveResponse(true)),
                DevicePairingApproveOutcome.RateLimited =>
                    Results.Json(
                        new ClientErrorResponse(
                            "rate_limited",
                            "Too many pairing attempts. Try again in a minute."),
                        statusCode: StatusCodes.Status429TooManyRequests),
                _ => NotFound(
                    "pairing_not_found",
                    "This code is invalid or has expired. Ask the TV for a new one.")
            };
        })
        .RequireAuthorization()
        .RequireRateLimiting("pairing-approve");

        group.MapPost("/poll", async (
            DevicePairingPollRequest request,
            DevicePairingStore pairing,
            OwnerAuthService ownerAuth,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.DeviceCode))
            {
                return BadRequest(
                    "invalid_device_code",
                    "deviceCode is required.");
            }

            var result = pairing.Poll(request.DeviceCode);

            if (result.Outcome == DevicePairingPollOutcome.Pending)
            {
                return Results.Ok(new DevicePairingPollResponse("pending", result.IntervalSeconds));
            }

            if (result.Outcome == DevicePairingPollOutcome.InvalidOrExpired)
            {
                return NotFound(
                    "pairing_not_found",
                    "This device code is invalid, expired or already used.");
            }

            var pairedAccount = await ownerAuth.GetEnabledAccountAsync(
                result.AccountId!,
                cancellationToken);

            if (pairedAccount is null)
            {
                return Results.Json(
                    new ClientErrorResponse(
                        "account_unavailable",
                        "The approving account is no longer available. Start pairing again."),
                    statusCode: StatusCodes.Status409Conflict);
            }

            await httpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                OwnerAuthService.CreatePrincipal(pairedAccount),
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    AllowRefresh = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30),
                });

            return Results.Ok(new DevicePairingPollResponse(
                "approved",
                result.IntervalSeconds,
                new ClientAccountResponse(
                    pairedAccount.Id,
                    pairedAccount.UserName,
                    pairedAccount.Role == AccountRole.Owner ? "owner" : "user")));
        })
        .AllowAnonymous()
        .RequireRateLimiting("pairing-poll");

        return endpoints;
    }

    private static IResult NotFound(string code, string message) =>
        Results.NotFound(new ClientErrorResponse(code, message));

    private static IResult BadRequest(string code, string message) =>
        Results.BadRequest(new ClientErrorResponse(code, message));
}

public sealed record DevicePairingApproveRequest(string UserCode);

public sealed record DevicePairingPollRequest(string DeviceCode);

public sealed record DevicePairingApproveResponse(bool Approved);

public sealed record DevicePairingPollResponse(
    string Status,
    int IntervalSeconds,
    ClientAccountResponse? Account = null);
