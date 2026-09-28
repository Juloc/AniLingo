using System.Security.Cryptography;
using System.Text;
using Jularr.Web.Data;
using Jularr.Web.Features.Playback.Decision;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Devices;

/// <summary>
/// One row of the "KnownDevices" registry, joined with the owning account's name and (when live)
/// its current playback method for display on Admin &gt; Devices and Profile &gt; Devices (#527).
/// </summary>
public sealed record KnownDeviceRow(
    string Id,
    string ProfileId,
    string ProfileName,
    string ClientKind,
    string? Label,
    string? AppVersion,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc,
    bool IsOnline,
    string? LivePlaybackMethodKey)
{
    /// <summary>UI catalog key of the status pill: the live playback method, else online/offline.</summary>
    public string StatusLabelKey => LivePlaybackMethodKey ?? (IsOnline ? "admin.devices.online" : "admin.devices.offline");

    /// <summary>Extra CSS class for the status pill; empty (neutral) when offline.</summary>
    public string StatusPillClass => LivePlaybackMethodKey is not null || IsOnline ? "status-ok" : "";
}

/// <summary>Raw "KnownDevices" row, before it is joined with account names or live sessions.</summary>
internal sealed record KnownDeviceRawRow(
    string Id,
    string ProfileId,
    string ClientKind,
    string? Label,
    string? AppVersion,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc);

/// <summary>
/// Stable device key derived from what a client already reports: the signed-in profile, its
/// coarse kind (web/pwa/android/android_tv/other, see <see cref="ClientKinds"/>) and, when a
/// client sends one, its display name (Chrome, Android TV, ...). Jularr's client contract has no
/// real device fingerprint yet, so the same browser/app on the same account is treated as one
/// device across sign-ins; a different browser, app or account produces a new entry.
/// </summary>
public static class DeviceKey
{
    public static string Compute(string profileId, string clientKind, string? label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientKind);

        var source = string.Join(
            '␟',
            profileId,
            clientKind.Trim().ToLowerInvariant(),
            (label ?? string.Empty).Trim().ToLowerInvariant());
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexStringLower(hash)[..32];
    }
}

/// <summary>
/// Known clients/devices across every account (#527, part of epic #510): who has connected, from
/// what kind of app, when it was first/last seen, and whether it is live right now. Rows are
/// created/refreshed from <see cref="PlaybackPlanService"/> each time a client opens a playback
/// session, the same touch point <see cref="PlaybackStreamSessionStore"/> uses. Revoking a device
/// ends its live playback session(s) and forgets it; Jularr's cookie auth has no separate
/// per-device token to invalidate, so a harder "sign out everywhere" for one account already
/// exists as the account-wide action on Admin &gt; Users
/// (<see cref="Auth.OwnerAuthService.InvalidateSessionsAsync"/>).
/// </summary>
public sealed class KnownDeviceRegistry(AppDbContext db, PlaybackStreamSessionStore sessions, TimeProvider time)
{
    /// <summary>A device with no live session still counts as online for this long after it was last touched.</summary>
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(3);

    /// <summary>Creates or refreshes one device's row. Called when a client opens a playback session.</summary>
    public async Task TouchAsync(
        string profileId,
        string clientKind,
        string? label,
        string? appVersion,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var normalizedKind = ClientKinds.Normalize(clientKind);
        var id = DeviceKey.Compute(profileId, normalizedKind, label);
        var now = time.GetUtcNow().UtcDateTime;

        var updated = await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "KnownDevices"
            SET "LastSeenUtc" = {0},
                "Label" = COALESCE({1}, "Label"),
                "AppVersion" = COALESCE({2}, "AppVersion"),
                "UserAgent" = COALESCE({3}, "UserAgent")
            WHERE "Id" = {4}
            """,
            [now, label!, appVersion!, userAgent!, id],
            cancellationToken);

        if (updated == 0)
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT OR IGNORE INTO "KnownDevices"
                    ("Id", "ProfileId", "ClientKind", "Label", "AppVersion", "UserAgent", "FirstSeenUtc", "LastSeenUtc")
                VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {6})
                """,
                [id, profileId, normalizedKind, label!, appVersion!, userAgent!, now],
                cancellationToken);
        }
    }

    /// <summary>Every known device across every account (Admin &gt; Devices, owner-only).</summary>
    public async Task<IReadOnlyList<KnownDeviceRow>> ListAllAsync(CancellationToken cancellationToken)
    {
        var raw = await db.Database
            .SqlQueryRaw<KnownDeviceRawRow>(
                """
                SELECT "Id", "ProfileId", "ClientKind", "Label", "AppVersion", "FirstSeenUtc", "LastSeenUtc"
                FROM "KnownDevices"
                """)
            .ToListAsync(cancellationToken);

        return await JoinAsync(raw, cancellationToken);
    }

    /// <summary>One profile's own known devices (Profile &gt; Devices).</summary>
    public async Task<IReadOnlyList<KnownDeviceRow>> ListForProfileAsync(string profileId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        var raw = await db.Database
            .SqlQueryRaw<KnownDeviceRawRow>(
                """
                SELECT "Id", "ProfileId", "ClientKind", "Label", "AppVersion", "FirstSeenUtc", "LastSeenUtc"
                FROM "KnownDevices"
                WHERE "ProfileId" = {0}
                """,
                profileId)
            .ToListAsync(cancellationToken);

        return await JoinAsync(raw, cancellationToken);
    }

    /// <summary>
    /// Forgets a device and ends its live session(s). <paramref name="requesterProfileId"/> null
    /// means an owner-context call (Admin &gt; Devices) that can revoke any account's device;
    /// otherwise the device must belong to that profile (Profile &gt; Devices self-service can
    /// never revoke another account's device).
    /// </summary>
    public async Task<bool> RevokeAsync(
        string deviceId,
        string? requesterProfileId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var device = await db.Database
            .SqlQueryRaw<KnownDeviceRawRow>(
                """
                SELECT "Id", "ProfileId", "ClientKind", "Label", "AppVersion", "FirstSeenUtc", "LastSeenUtc"
                FROM "KnownDevices"
                WHERE "Id" = {0}
                """,
                deviceId)
            .SingleOrDefaultAsync(cancellationToken);

        if (device is null)
        {
            return false;
        }

        if (requesterProfileId is not null &&
            !string.Equals(device.ProfileId, requesterProfileId, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var session in sessions.ListForProfile(device.ProfileId)
                     .Where(x => string.Equals(x.Selections.ClientKind, device.ClientKind, StringComparison.Ordinal)))
        {
            sessions.RemoveAny(session.Id);
        }

        await db.Database.ExecuteSqlRawAsync(
            """DELETE FROM "KnownDevices" WHERE "Id" = {0}""",
            [deviceId],
            cancellationToken);

        return true;
    }

    private async Task<IReadOnlyList<KnownDeviceRow>> JoinAsync(
        IReadOnlyList<KnownDeviceRawRow> raw,
        CancellationToken cancellationToken)
    {
        if (raw.Count == 0)
        {
            return [];
        }

        var profileIds = raw.Select(x => x.ProfileId).Distinct().ToArray();
        var names = await db.OwnerAccounts
            .AsNoTracking()
            .Where(x => profileIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.UserName, cancellationToken);

        var live = sessions.ListAll();
        var now = time.GetUtcNow().UtcDateTime;

        return raw
            .OrderByDescending(x => x.LastSeenUtc)
            .Select(device =>
            {
                var liveSession = live.FirstOrDefault(x =>
                    string.Equals(x.ProfileId, device.ProfileId, StringComparison.Ordinal) &&
                    string.Equals(x.Selections.ClientKind, device.ClientKind, StringComparison.Ordinal));
                var isOnline = liveSession is not null || now - device.LastSeenUtc <= OnlineWindow;

                return new KnownDeviceRow(
                    device.Id,
                    device.ProfileId,
                    names.GetValueOrDefault(device.ProfileId, device.ProfileId),
                    device.ClientKind,
                    device.Label,
                    device.AppVersion,
                    device.FirstSeenUtc,
                    device.LastSeenUtc,
                    isOnline,
                    liveSession is null ? null : PlaybackMethodKey(liveSession.Plan.Mode));
            })
            .ToArray();
    }

    private static string PlaybackMethodKey(PlaybackDeliveryMode mode) => mode switch
    {
        PlaybackDeliveryMode.DirectPlay => "playback.mode.direct_play",
        PlaybackDeliveryMode.DirectStream => "playback.mode.direct_stream",
        PlaybackDeliveryMode.Transcode => "playback.mode.transcode",
        _ => "playback.mode.unavailable"
    };
}
