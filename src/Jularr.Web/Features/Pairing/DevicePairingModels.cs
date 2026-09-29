namespace Jularr.Web.Features.Pairing;

/// <summary>
/// Result of starting a device-code pairing (#489). Handed straight back to the TV: it never
/// sees anything else about the pending pairing.
/// </summary>
public sealed record DevicePairingStart(
    string DeviceCode,
    string UserCode,
    int ExpiresInSeconds,
    int IntervalSeconds);

public enum DevicePairingApproveOutcome
{
    Approved,
    InvalidOrExpired,
    RateLimited,
}

public enum DevicePairingPollOutcome
{
    Pending,
    Approved,
    InvalidOrExpired,
}

/// <summary>
/// Result of one TV poll. <see cref="AccountId"/> is only set when <see cref="Outcome"/> is
/// <see cref="DevicePairingPollOutcome.Approved"/>; the pairing entry is removed (single-use) the
/// moment that happens, so a repeated poll with the same device code always comes back
/// <see cref="DevicePairingPollOutcome.InvalidOrExpired"/>.
/// </summary>
public sealed record DevicePairingPollResult(
    DevicePairingPollOutcome Outcome,
    int IntervalSeconds,
    string? AccountId = null);
