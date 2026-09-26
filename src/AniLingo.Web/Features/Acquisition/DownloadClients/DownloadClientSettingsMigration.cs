using AniLingo.Web.Features.Acquisition.Sabnzbd;

namespace AniLingo.Web.Features.Acquisition.DownloadClients;

public enum DownloadClientSettingsMigrationOutcome
{
    NothingToMigrate,
    Migrated,
    CanonicalAlreadyPresent
}

/// <summary>
/// One-time move of the single legacy SABnzbd connection (<c>sabnzbd.json</c>,
/// plus any configured overrides) into the canonical download client list as
/// one SABnzbd entry. After it runs, the legacy file is removed and nothing
/// reads it again. Running it again is a no-op. Runs after the existing
/// Books-integration → <c>sabnzbd.json</c> migration so any values still
/// there are carried forward.
/// </summary>
public static class DownloadClientSettingsMigration
{
    public static async Task<DownloadClientSettingsMigrationOutcome> MigrateSabnzbdAsync(
        SabnzbdConnectionResolver resolver,
        DownloadClientStore clientStore,
        SabnzbdSettingsStore legacyStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(clientStore);
        ArgumentNullException.ThrowIfNull(legacyStore);

        if (!legacyStore.Exists)
        {
            return DownloadClientSettingsMigrationOutcome.NothingToMigrate;
        }

        var existing = await clientStore.LoadAllAsync(cancellationToken);
        if (existing.Any(entry => entry.Type == DownloadClientType.Sabnzbd))
        {
            TryDelete(legacyStore.StorePath);
            return DownloadClientSettingsMigrationOutcome.CanonicalAlreadyPresent;
        }

        var resolved = await resolver.ResolveAsync(cancellationToken);
        if (resolved.Connection is null)
        {
            // Incomplete legacy settings (missing URL or key): nothing usable to migrate.
            return DownloadClientSettingsMigrationOutcome.NothingToMigrate;
        }

        await clientStore.SaveAsync(
            new DownloadClientEntry(
                Guid.NewGuid(),
                "SABnzbd",
                DownloadClientType.Sabnzbd,
                Enabled: true,
                Priority: 1,
                new DownloadClientSettings(
                    resolved.Connection.Settings.BaseUrl,
                    resolved.Connection.Settings.BooksCategory,
                    resolved.Connection.Settings.AnimeCategory),
                resolved.Connection.ApiKey),
            cancellationToken);

        TryDelete(legacyStore.StorePath);
        return DownloadClientSettingsMigrationOutcome.Migrated;
    }

    public static async Task RunAtStartupAsync(
        IServiceProvider services,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        var resolver = services.GetRequiredService<SabnzbdConnectionResolver>();
        var clientStore = services.GetRequiredService<DownloadClientStore>();
        var legacyStore = services.GetRequiredService<SabnzbdSettingsStore>();

        DownloadClientSettingsMigrationOutcome outcome;
        try
        {
            outcome = await MigrateSabnzbdAsync(resolver, clientStore, legacyStore, cancellationToken);
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or ArgumentException
                or IOException
                or UnauthorizedAccessException
                or System.Security.Cryptography.CryptographicException)
        {
            log($"Could not migrate SABnzbd settings into the canonical download client list: {exception.Message}");
            return;
        }

        switch (outcome)
        {
            case DownloadClientSettingsMigrationOutcome.Migrated:
                log("Moved the SABnzbd settings into the canonical download client list.");
                break;
            case DownloadClientSettingsMigrationOutcome.CanonicalAlreadyPresent:
                log("Removed the legacy SABnzbd settings file; the canonical download client list already has a SABnzbd entry.");
                break;
        }

        try
        {
            await RemoveUnsupportedEntriesAsync(clientStore, log, cancellationToken);
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or ArgumentException
                or IOException
                or UnauthorizedAccessException
                or System.Security.Cryptography.CryptographicException)
        {
            log($"Could not clean up unsupported download client entries: {exception.Message}");
        }
    }

    /// <summary>
    /// One-time, idempotent cleanup: AniLingo is usenet-only, so a qBittorrent (torrent) download
    /// client entry persisted by an earlier build is no longer readable as a supported
    /// <see cref="DownloadClientType"/> value and must be dropped rather than silently
    /// reinterpreted. Named entries are logged so the owner knows what was removed and can
    /// reconfigure a SABnzbd replacement if needed. SABnzbd entries are never touched; running
    /// this again after cleanup is a no-op.
    /// </summary>
    public static async Task<int> RemoveUnsupportedEntriesAsync(
        DownloadClientStore clientStore,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clientStore);
        ArgumentNullException.ThrowIfNull(log);

        var all = await clientStore.LoadAllAsync(cancellationToken);
        var unsupported = all.Where(entry => !Enum.IsDefined(entry.Type)).ToArray();
        foreach (var entry in unsupported)
        {
            await clientStore.DeleteAsync(entry.Id, cancellationToken);
            log($"Removed download client '{entry.Name}': torrent download clients (qBittorrent) are no longer supported; AniLingo is usenet-only.");
        }

        return unsupported.Length;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
