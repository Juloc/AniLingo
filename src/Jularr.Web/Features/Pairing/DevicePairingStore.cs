using System.Security.Cryptography;

namespace Jularr.Web.Features.Pairing;

/// <summary>
/// In-memory, single-process store for Android TV device-code pairing (#489), the same
/// short-lived/single-use/rate-limited shape as
/// <see cref="Jularr.Web.Features.PlaybackSessions.PlaybackSessionStore"/>'s companion pairing.
/// Deliberately has no durable backing store: #570 owns the database migration to PostgreSQL in
/// parallel, and a device-setup code that outlives one process restart is not worth a schema
/// change. A TV that is mid-pairing during a server restart just starts over, the same as an
/// expired code.
/// </summary>
public sealed class DevicePairingStore
{
    public static readonly TimeSpan PairingLifetime = TimeSpan.FromMinutes(5);
    public const int PollIntervalSeconds = 5;
    public static readonly TimeSpan ApproveAttemptWindow = TimeSpan.FromMinutes(1);
    public const int MaxApproveAttemptsPerWindow = 10;

    private const string UserCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int UserCodeGroupLength = 4;

    private readonly object gate = new();
    private readonly TimeProvider timeProvider;
    private readonly Dictionary<string, PairingEntry> byDeviceCode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> deviceCodeByUserCode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ApproveAttemptBucket> approveAttempts =
        new(StringComparer.Ordinal);

    public DevicePairingStore(TimeProvider? timeProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Called by the TV. Creates a new pending pairing with a fresh device/user code pair.</summary>
    public DevicePairingStart Start()
    {
        lock (gate)
        {
            CleanupExpired();

            var deviceCode = CreateUniqueDeviceCode();
            var userCode = CreateUniqueUserCode();
            var expiresAt = timeProvider.GetUtcNow() + PairingLifetime;

            byDeviceCode[deviceCode] = new PairingEntry(userCode, expiresAt);
            deviceCodeByUserCode[userCode] = deviceCode;

            return new DevicePairingStart(
                deviceCode,
                userCode,
                (int)PairingLifetime.TotalSeconds,
                PollIntervalSeconds);
        }
    }

    /// <summary>
    /// Called by the authenticated approval action (phone/web). Attempts are bounded per
    /// <paramref name="attemptKey"/> (normally the approving account id) so a signed-in user
    /// cannot be used to brute-force other pending user codes.
    /// </summary>
    public DevicePairingApproveOutcome Approve(string userCode, string accountId, string attemptKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        var normalizedCode = NormalizeUserCode(userCode);

        lock (gate)
        {
            if (!AllowApproveAttempt(attemptKey))
            {
                return DevicePairingApproveOutcome.RateLimited;
            }

            CleanupExpired();

            if (normalizedCode is null ||
                !deviceCodeByUserCode.TryGetValue(normalizedCode, out var deviceCode) ||
                !byDeviceCode.TryGetValue(deviceCode, out var entry) ||
                entry.ExpiresAtUtc <= timeProvider.GetUtcNow())
            {
                return DevicePairingApproveOutcome.InvalidOrExpired;
            }

            byDeviceCode[deviceCode] = entry with { ApprovedAccountId = accountId };
            return DevicePairingApproveOutcome.Approved;
        }
    }

    /// <summary>
    /// Called by the TV. Single-use: the moment this returns
    /// <see cref="DevicePairingPollOutcome.Approved"/> the entry is gone, so a retried/duplicate
    /// poll (network retry, a second tab, ...) always sees
    /// <see cref="DevicePairingPollOutcome.InvalidOrExpired"/> afterward.
    /// </summary>
    public DevicePairingPollResult Poll(string deviceCode)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            return new DevicePairingPollResult(
                DevicePairingPollOutcome.InvalidOrExpired,
                PollIntervalSeconds);
        }

        lock (gate)
        {
            CleanupExpired();

            if (!byDeviceCode.TryGetValue(deviceCode, out var entry))
            {
                return new DevicePairingPollResult(
                    DevicePairingPollOutcome.InvalidOrExpired,
                    PollIntervalSeconds);
            }

            if (entry.ApprovedAccountId is null)
            {
                return new DevicePairingPollResult(
                    DevicePairingPollOutcome.Pending,
                    PollIntervalSeconds);
            }

            Remove(deviceCode, entry.UserCode);
            return new DevicePairingPollResult(
                DevicePairingPollOutcome.Approved,
                PollIntervalSeconds,
                entry.ApprovedAccountId);
        }
    }

    private bool AllowApproveAttempt(string attemptKey)
    {
        var key = string.IsNullOrWhiteSpace(attemptKey) ? "unknown" : attemptKey.Trim();
        var now = timeProvider.GetUtcNow();

        if (!approveAttempts.TryGetValue(key, out var bucket) ||
            now - bucket.WindowStart >= ApproveAttemptWindow)
        {
            approveAttempts[key] = new ApproveAttemptBucket(now, 1);
            return true;
        }

        if (bucket.Count >= MaxApproveAttemptsPerWindow)
        {
            return false;
        }

        approveAttempts[key] = bucket with { Count = bucket.Count + 1 };
        return true;
    }

    private void Remove(string deviceCode, string userCode)
    {
        byDeviceCode.Remove(deviceCode);
        deviceCodeByUserCode.Remove(userCode);
    }

    private void CleanupExpired()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var (deviceCode, entry) in byDeviceCode
                     .Where(x => x.Value.ExpiresAtUtc <= now)
                     .ToArray())
        {
            Remove(deviceCode, entry.UserCode);
        }

        foreach (var key in approveAttempts
                     .Where(x => now - x.Value.WindowStart >= ApproveAttemptWindow)
                     .Select(x => x.Key)
                     .ToArray())
        {
            approveAttempts.Remove(key);
        }
    }

    private string CreateUniqueDeviceCode()
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var code = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
            if (!byDeviceCode.ContainsKey(code))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not allocate a unique device pairing code.");
    }

    private string CreateUniqueUserCode()
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var code = string.Concat(
                RandomGroup(UserCodeGroupLength),
                "-",
                RandomGroup(UserCodeGroupLength));

            if (!deviceCodeByUserCode.ContainsKey(code))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not allocate a unique pairing user code.");
    }

    private static string RandomGroup(int length) =>
        string.Create(length, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = UserCodeAlphabet[RandomNumberGenerator.GetInt32(0, UserCodeAlphabet.Length)];
            }
        });

    /// <summary>Accepts the code with or without the separator/casing the TV displays it with.</summary>
    private static string? NormalizeUserCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var cleaned = new string(code
                .Trim()
                .ToUpperInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray());

        if (cleaned.Length != UserCodeGroupLength * 2 ||
            !cleaned.All(c => UserCodeAlphabet.Contains(c)))
        {
            return null;
        }

        return string.Concat(
            cleaned.AsSpan(0, UserCodeGroupLength),
            "-",
            cleaned.AsSpan(UserCodeGroupLength, UserCodeGroupLength));
    }

    private sealed record PairingEntry(
        string UserCode,
        DateTimeOffset ExpiresAtUtc,
        string? ApprovedAccountId = null);

    private sealed record ApproveAttemptBucket(
        DateTimeOffset WindowStart,
        int Count);
}
