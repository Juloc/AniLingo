using System.Text.Json;
using AniLingo.Web.Features.Books;

namespace AniLingo.Web.Features.Acquisition.Sabnzbd;

public enum SabnzbdSettingsMigrationOutcome
{
    NothingToMigrate,
    Migrated,
    CanonicalAlreadyPresent
}

/// <summary>
/// One-time move of the SABnzbd fields that earlier builds stored in the
/// Books integration file (<c>/data/books/integrations.json</c>) into the
/// canonical <see cref="SabnzbdSettingsStore"/>. After it runs, the Books
/// file only keeps the Books inbox path. Running it again is a no-op.
/// </summary>
public static class SabnzbdSettingsMigration
{
    private const string LegacyBaseUrl = "SabnzbdBaseUrl";
    private const string LegacyApiKey = "SabnzbdApiKey";
    private const string LegacyCategory = "SabnzbdCategory";

    public static async Task<SabnzbdSettingsMigrationOutcome> MigrateLegacyBooksSettingsAsync(
        SabnzbdSettingsStore store,
        string? legacyBooksSettingsPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var path = legacyBooksSettingsPath ?? BookIntegrationSettingsStore.SettingsPath;

        if (!File.Exists(path))
        {
            return SabnzbdSettingsMigrationOutcome.NothingToMigrate;
        }

        LegacyBooksSettings legacy;
        try
        {
            legacy = ReadLegacy(await File.ReadAllTextAsync(path, cancellationToken));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Books integration settings contain invalid JSON; SABnzbd settings could not be migrated.",
                exception);
        }

        if (!legacy.HasSabnzbdProperties)
        {
            return SabnzbdSettingsMigrationOutcome.NothingToMigrate;
        }

        var outcome = legacy.HasSabnzbdValues
            ? SabnzbdSettingsMigrationOutcome.CanonicalAlreadyPresent
            : SabnzbdSettingsMigrationOutcome.NothingToMigrate;
        if (legacy.HasSabnzbdValues && !store.Exists)
        {
            // Canonical settings win once they exist; the legacy copy is only
            // imported into an empty canonical store.
            await store.SaveAsync(
                new SabnzbdStoredSettings(
                    legacy.BaseUrl,
                    legacy.ApiKey,
                    BooksCategory: legacy.Category,
                    AnimeCategory: null),
                cancellationToken);
            outcome = SabnzbdSettingsMigrationOutcome.Migrated;
        }

        // Rewrite the Books file without the SABnzbd fields so the plaintext
        // API key does not remain on disk and nothing can read it again.
        await BookIntegrationSettingsStore.SaveAsync(
            new BookIntegrationSettings(legacy.InboxPath),
            cancellationToken,
            path);

        return outcome;
    }

    /// <summary>
    /// Returns every retired Books-only configuration key that is still set.
    /// Retired keys are not read; startup logs the canonical replacement.
    /// </summary>
    public static IReadOnlyList<string> FindRetiredConfigurationKeys(
        IConfiguration configuration) =>
        SabnzbdConfigurationKeys.Retired.Keys
            .Where(key => !string.IsNullOrWhiteSpace(configuration[key]))
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static async Task RunAtStartupAsync(
        IServiceProvider services,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        foreach (var key in FindRetiredConfigurationKeys(configuration))
        {
            log(
                $"Configuration key '{key}' is no longer read. Use "
                + $"'{SabnzbdConfigurationKeys.Retired[key]}' instead.");
        }

        SabnzbdSettingsMigrationOutcome outcome;
        try
        {
            outcome = await MigrateLegacyBooksSettingsAsync(
                services.GetRequiredService<SabnzbdSettingsStore>(),
                cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or ArgumentException
                or IOException
                or UnauthorizedAccessException)
        {
            // The legacy file is left untouched so the owner can recover the
            // values; SABnzbd stays unconfigured until saved in Settings.
            log($"Could not migrate SABnzbd settings from the Books integration: {exception.Message}");
            return;
        }

        switch (outcome)
        {
            case SabnzbdSettingsMigrationOutcome.Migrated:
                log("Moved SABnzbd settings from the Books integration into the shared SABnzbd settings.");
                break;
            case SabnzbdSettingsMigrationOutcome.CanonicalAlreadyPresent:
                log("Removed stale SABnzbd fields from the Books integration; shared SABnzbd settings already exist.");
                break;
        }
    }

    private static LegacyBooksSettings ReadLegacy(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new LegacyBooksSettings(false, null, null, null, null);
        }

        var hasProperties = root
            .EnumerateObject()
            .Any(property =>
                property.Name.Equals(LegacyBaseUrl, StringComparison.OrdinalIgnoreCase)
                || property.Name.Equals(LegacyApiKey, StringComparison.OrdinalIgnoreCase)
                || property.Name.Equals(LegacyCategory, StringComparison.OrdinalIgnoreCase));

        return new LegacyBooksSettings(
            hasProperties,
            ReadString(root, LegacyBaseUrl),
            ReadString(root, LegacyApiKey),
            ReadString(root, LegacyCategory),
            ReadString(root, nameof(BookIntegrationSettings.InboxPath)));
    }

    private static string? ReadString(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                var value = property.Value.GetString()?.Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }

    private sealed record LegacyBooksSettings(
        bool HasSabnzbdProperties,
        string? BaseUrl,
        string? ApiKey,
        string? Category,
        string? InboxPath)
    {
        public bool HasSabnzbdValues =>
            BaseUrl is not null || ApiKey is not null || Category is not null;
    }
}
