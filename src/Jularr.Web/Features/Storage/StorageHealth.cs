namespace Jularr.Web.Features.Storage;

// The one owner- and user-facing storage state. It is derived from the probe result and the
// root's Wake-on-LAN configuration and never needs its own persistence: a WOL-enabled root
// that cannot be reached is sleeping on purpose, a root without WOL that cannot be reached is
// a problem, and a failed start attempt or unreadable storage is an error.
public enum StorageHealthState
{
    Online,
    Starting,
    OfflineExpected,
    OfflineUnexpected,
    Error
}

public static class StorageDiagnosticCodes
{
    // Wake-on-LAN was sent but the storage did not become readable within the start timeout.
    public const string WakeTimeout = "wake_timeout";

    // The magic packet could not be sent from the Jularr host or container.
    public const string WakeSendFailed = "wake_send_failed";

    public static bool IsStartFailure(string? code) =>
        code is WakeTimeout or WakeSendFailed;
}

public static class StorageHealth
{
    public static StorageHealthState Resolve(
        StorageAvailabilityState state,
        bool wakeConfigured,
        string? diagnosticCode) =>
        state switch
        {
            // A missing file is a media problem on otherwise readable storage.
            StorageAvailabilityState.Available or StorageAvailabilityState.FileMissing =>
                StorageHealthState.Online,
            StorageAvailabilityState.Starting => StorageHealthState.Starting,
            StorageAvailabilityState.Unreachable => StorageHealthState.Error,
            _ when StorageDiagnosticCodes.IsStartFailure(diagnosticCode) => StorageHealthState.Error,
            _ => wakeConfigured
                ? StorageHealthState.OfflineExpected
                : StorageHealthState.OfflineUnexpected
        };

    // Decimal units, as NAS vendors report volume sizes.
    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{value:0.#} {units[unit]}");
    }

    // Stable wire names for clients.
    public static string Name(StorageHealthState health) =>
        health switch
        {
            StorageHealthState.Online => "online",
            StorageHealthState.Starting => "starting",
            StorageHealthState.OfflineExpected => "offline_expected",
            StorageHealthState.OfflineUnexpected => "offline_unexpected",
            _ => "error"
        };
}
