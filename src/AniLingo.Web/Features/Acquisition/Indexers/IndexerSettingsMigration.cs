using AniLingo.Web.Features.Acquisition.Prowlarr;

namespace AniLingo.Web.Features.Acquisition.Indexers;

public enum IndexerSettingsMigrationOutcome
{
    NothingToMigrate,
    Migrated,
    CanonicalAlreadyPresent
}

/// <summary>
/// One-time move of the single legacy Prowlarr connection
/// (<c>prowlarr.json</c>) into the canonical indexer list as one Prowlarr
/// entry. After it runs, the legacy file is removed and nothing reads it
/// again. Running it again is a no-op.
/// </summary>
public static class IndexerSettingsMigration
{
    public static async Task<IndexerSettingsMigrationOutcome> MigrateProwlarrAsync(
        ProwlarrSettingsStore legacyStore,
        IndexerStore indexerStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(legacyStore);
        ArgumentNullException.ThrowIfNull(indexerStore);

        if (!legacyStore.Exists)
        {
            return IndexerSettingsMigrationOutcome.NothingToMigrate;
        }

        var existing = await indexerStore.LoadAllAsync(cancellationToken);
        if (existing.Any(entry => entry.Type == IndexerType.Prowlarr))
        {
            TryDelete(legacyStore.StorePath);
            return IndexerSettingsMigrationOutcome.CanonicalAlreadyPresent;
        }

        var connection = await legacyStore.LoadAsync(cancellationToken);
        if (connection is null)
        {
            return IndexerSettingsMigrationOutcome.NothingToMigrate;
        }

        await indexerStore.SaveAsync(
            new IndexerEntry(
                Guid.NewGuid(),
                "Prowlarr",
                IndexerType.Prowlarr,
                Enabled: true,
                Priority: 1,
                new IndexerSettings(
                    connection.Settings.BaseUrl,
                    connection.Settings.Categories,
                    connection.Settings.IndexerIds,
                    connection.Settings.SearchLimit),
                connection.ApiKey),
            cancellationToken);

        TryDelete(legacyStore.StorePath);
        return IndexerSettingsMigrationOutcome.Migrated;
    }

    public static async Task RunAtStartupAsync(
        IServiceProvider services,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        var legacyStore = services.GetRequiredService<ProwlarrSettingsStore>();
        var indexerStore = services.GetRequiredService<IndexerStore>();

        IndexerSettingsMigrationOutcome outcome;
        try
        {
            outcome = await MigrateProwlarrAsync(legacyStore, indexerStore, cancellationToken);
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or ArgumentException
                or IOException
                or UnauthorizedAccessException
                or System.Security.Cryptography.CryptographicException)
        {
            log($"Could not migrate Prowlarr settings into the canonical indexer list: {exception.Message}");
            return;
        }

        switch (outcome)
        {
            case IndexerSettingsMigrationOutcome.Migrated:
                log("Moved the Prowlarr settings into the canonical indexer list.");
                break;
            case IndexerSettingsMigrationOutcome.CanonicalAlreadyPresent:
                log("Removed the legacy Prowlarr settings file; the canonical indexer list already has a Prowlarr entry.");
                break;
        }

        try
        {
            await RemoveUnsupportedEntriesAsync(indexerStore, log, cancellationToken);
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or ArgumentException
                or IOException
                or UnauthorizedAccessException
                or System.Security.Cryptography.CryptographicException)
        {
            log($"Could not clean up unsupported indexer entries: {exception.Message}");
        }
    }

    /// <summary>
    /// One-time, idempotent cleanup: AniLingo is usenet-only, so a Torznab (torrent) indexer entry
    /// persisted by an earlier build is no longer readable as a supported <see cref="IndexerType"/>
    /// value and must be dropped rather than silently reinterpreted. Named entries are logged so the
    /// owner knows what was removed and can reconfigure a usenet replacement if needed. Usenet
    /// entries (Prowlarr, Newznab) are never touched; running this again after cleanup is a no-op.
    /// </summary>
    public static async Task<int> RemoveUnsupportedEntriesAsync(
        IndexerStore indexerStore,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(indexerStore);
        ArgumentNullException.ThrowIfNull(log);

        var all = await indexerStore.LoadAllAsync(cancellationToken);
        var unsupported = all.Where(entry => !Enum.IsDefined(entry.Type)).ToArray();
        foreach (var entry in unsupported)
        {
            await indexerStore.DeleteAsync(entry.Id, cancellationToken);
            log($"Removed indexer '{entry.Name}': torrent indexers (Torznab) are no longer supported; AniLingo is usenet-only.");
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
