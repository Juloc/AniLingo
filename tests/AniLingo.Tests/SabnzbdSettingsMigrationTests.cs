using AniLingo.Web.Features.Acquisition.Sabnzbd;
using AniLingo.Web.Features.Books;
using Microsoft.AspNetCore.DataProtection;

namespace AniLingo.Tests;

[TestClass]
public sealed class SabnzbdSettingsMigrationTests
{
    private const string LegacyBooksJson = """
        {
          "SabnzbdBaseUrl": "http://sabnzbd:8080",
          "SabnzbdApiKey": "legacy-secret",
          "SabnzbdCategory": "books",
          "InboxPath": "/books-inbox"
        }
        """;

    [TestMethod]
    public async Task LegacyBooksSettingsMoveIntoCanonicalStoreOnce()
    {
        var directory = SabnzbdTestSupport.CreateTemporaryDirectory();
        try
        {
            var legacyPath = Path.Combine(directory.FullName, "books", "integrations.json");
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
            await File.WriteAllTextAsync(legacyPath, LegacyBooksJson);
            var store = new SabnzbdSettingsStore(
                new EphemeralDataProtectionProvider(),
                new DirectoryInfo(Path.Combine(directory.FullName, "acquisition")));

            var first = await SabnzbdSettingsMigration.MigrateLegacyBooksSettingsAsync(store, legacyPath);
            var second = await SabnzbdSettingsMigration.MigrateLegacyBooksSettingsAsync(store, legacyPath);

            Assert.AreEqual(SabnzbdSettingsMigrationOutcome.Migrated, first);
            Assert.AreEqual(SabnzbdSettingsMigrationOutcome.NothingToMigrate, second);

            var canonical = await store.LoadAsync();
            Assert.AreEqual("http://sabnzbd:8080", canonical.BaseUrl);
            Assert.AreEqual("legacy-secret", canonical.ApiKey);
            Assert.AreEqual("books", canonical.BooksCategory);
            Assert.IsNull(canonical.AnimeCategory);

            // The Books file keeps only the inbox; the plaintext key is gone.
            var legacyRaw = await File.ReadAllTextAsync(legacyPath);
            Assert.IsFalse(legacyRaw.Contains("legacy-secret", StringComparison.Ordinal));
            Assert.IsFalse(legacyRaw.Contains("Sabnzbd", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(
                Path.GetFullPath("/books-inbox"),
                BookIntegrationSettingsStore.Load(legacyPath).InboxPath);

            var canonicalRaw = await File.ReadAllTextAsync(store.StorePath);
            Assert.IsFalse(canonicalRaw.Contains("legacy-secret", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task ExistingCanonicalSettingsWinAndLegacyFieldsAreRemoved()
    {
        var directory = SabnzbdTestSupport.CreateTemporaryDirectory();
        try
        {
            var legacyPath = Path.Combine(directory.FullName, "integrations.json");
            await File.WriteAllTextAsync(legacyPath, LegacyBooksJson);
            var store = new SabnzbdSettingsStore(new EphemeralDataProtectionProvider(), directory);
            await store.SaveAsync(
                new SabnzbdStoredSettings("http://canonical:8080", "canonical-key", null, "anime"));

            var outcome = await SabnzbdSettingsMigration.MigrateLegacyBooksSettingsAsync(store, legacyPath);

            Assert.AreEqual(SabnzbdSettingsMigrationOutcome.CanonicalAlreadyPresent, outcome);
            var canonical = await store.LoadAsync();
            Assert.AreEqual("http://canonical:8080", canonical.BaseUrl);
            Assert.AreEqual("canonical-key", canonical.ApiKey);
            Assert.IsFalse(
                (await File.ReadAllTextAsync(legacyPath)).Contains("legacy-secret", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task MissingOrInboxOnlyBooksSettingsNeedNoMigration()
    {
        var directory = SabnzbdTestSupport.CreateTemporaryDirectory();
        try
        {
            var legacyPath = Path.Combine(directory.FullName, "integrations.json");
            var store = new SabnzbdSettingsStore(new EphemeralDataProtectionProvider(), directory);

            Assert.AreEqual(
                SabnzbdSettingsMigrationOutcome.NothingToMigrate,
                await SabnzbdSettingsMigration.MigrateLegacyBooksSettingsAsync(store, legacyPath));

            await BookIntegrationSettingsStore.SaveAsync(
                new BookIntegrationSettings("/books-inbox"),
                CancellationToken.None,
                legacyPath);

            Assert.AreEqual(
                SabnzbdSettingsMigrationOutcome.NothingToMigrate,
                await SabnzbdSettingsMigration.MigrateLegacyBooksSettingsAsync(store, legacyPath));
            Assert.IsFalse(store.Exists);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void RetiredBooksConfigurationKeysAreReported()
    {
        var configuration = SabnzbdTestSupport.Configuration(new Dictionary<string, string?>
        {
            ["Books:SABnzbd:ApiKey"] = "old",
            ["Books:SABnzbd:Category"] = "books",
            [SabnzbdConfigurationKeys.BaseUrl] = "http://sabnzbd:8080"
        });

        var retired = SabnzbdSettingsMigration.FindRetiredConfigurationKeys(configuration);

        CollectionAssert.AreEqual(
            new[] { "Books:SABnzbd:ApiKey", "Books:SABnzbd:Category" },
            retired.ToArray());
        Assert.AreEqual(
            SabnzbdConfigurationKeys.ApiKey,
            SabnzbdConfigurationKeys.Retired["Books:SABnzbd:ApiKey"]);
    }
}
