using AniLingo.Web.Features.Acquisition.DownloadClients;
using AniLingo.Web.Features.Acquisition.Indexers;
using AniLingo.Web.Features.Acquisition.Prowlarr;
using AniLingo.Web.Features.Acquisition.Sabnzbd;
using Microsoft.AspNetCore.DataProtection;

namespace AniLingo.Tests;

[TestClass]
public sealed class AcquisitionSettingsMigrationTests
{
    [TestMethod]
    public async Task ProwlarrSettingsMoveIntoTheIndexerListOnceAndTheLegacyFileIsRemoved()
    {
        var directory = SabnzbdTestSupport.CreateTemporaryDirectory();
        try
        {
            var protection = new EphemeralDataProtectionProvider();
            var legacy = new ProwlarrSettingsStore(protection, directory);
            await legacy.SaveAsync(
                new ProwlarrConnection(
                    new ProwlarrSettings("https://prowlarr.example", [5000, 5070], [], 100),
                    "prowlarr-key"));
            var indexers = new IndexerStore(protection, directory);

            var first = await IndexerSettingsMigration.MigrateProwlarrAsync(legacy, indexers);
            var second = await IndexerSettingsMigration.MigrateProwlarrAsync(legacy, indexers);

            Assert.AreEqual(IndexerSettingsMigrationOutcome.Migrated, first);
            Assert.AreEqual(IndexerSettingsMigrationOutcome.NothingToMigrate, second);
            Assert.IsFalse(legacy.Exists, "The legacy prowlarr.json is removed once migrated.");

            var entries = await indexers.LoadAllAsync();
            var entry = entries.Single();
            Assert.AreEqual(IndexerType.Prowlarr, entry.Type);
            Assert.AreEqual("https://prowlarr.example", entry.Settings.BaseUrl);
            Assert.AreEqual("prowlarr-key", entry.ApiKey);
            CollectionAssert.AreEqual(new[] { 5000, 5070 }, entry.Settings.Categories);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task ProwlarrMigrationIsSkippedAndTheLegacyFileStillRemovedWhenAnIndexerAlreadyExists()
    {
        var directory = SabnzbdTestSupport.CreateTemporaryDirectory();
        try
        {
            var protection = new EphemeralDataProtectionProvider();
            var legacy = new ProwlarrSettingsStore(protection, directory);
            await legacy.SaveAsync(new ProwlarrConnection(ProwlarrSettings.CreateDefault("https://prowlarr.example"), "legacy-key"));
            var indexers = new IndexerStore(protection, directory);
            var existing = new IndexerEntry(
                Guid.NewGuid(), "Already configured", IndexerType.Prowlarr, true, 1,
                IndexerSettings.CreateDefault("https://already.example", IndexerType.Prowlarr), "existing-key");
            await indexers.SaveAsync(existing);

            var outcome = await IndexerSettingsMigration.MigrateProwlarrAsync(legacy, indexers);

            Assert.AreEqual(IndexerSettingsMigrationOutcome.CanonicalAlreadyPresent, outcome);
            Assert.IsFalse(legacy.Exists);
            var entries = await indexers.LoadAllAsync();
            Assert.AreEqual(1, entries.Count, "The pre-existing entry is not duplicated.");
            Assert.AreEqual("existing-key", entries.Single().ApiKey);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task SabnzbdSettingsMoveIntoTheDownloadClientListOnceAndTheLegacyFileIsRemoved()
    {
        var directory = SabnzbdTestSupport.CreateTemporaryDirectory();
        try
        {
            var protection = new EphemeralDataProtectionProvider();
            var legacy = new SabnzbdSettingsStore(protection, directory);
            await legacy.SaveAsync(new SabnzbdStoredSettings("http://sabnzbd:8080", "secret-key", "books", "anime"));
            var resolver = new SabnzbdConnectionResolver(legacy, SabnzbdTestSupport.Configuration());
            var clients = new DownloadClientStore(protection, directory);

            var first = await DownloadClientSettingsMigration.MigrateSabnzbdAsync(resolver, clients, legacy);
            var second = await DownloadClientSettingsMigration.MigrateSabnzbdAsync(resolver, clients, legacy);

            Assert.AreEqual(DownloadClientSettingsMigrationOutcome.Migrated, first);
            Assert.AreEqual(DownloadClientSettingsMigrationOutcome.NothingToMigrate, second);
            Assert.IsFalse(legacy.Exists);

            var entries = await clients.LoadAllAsync();
            var entry = entries.Single();
            Assert.AreEqual(DownloadClientType.Sabnzbd, entry.Type);
            Assert.AreEqual("http://sabnzbd:8080", entry.Settings.BaseUrl);
            Assert.AreEqual("secret-key", entry.Secret);
            Assert.AreEqual("books", entry.Settings.BooksCategory);
            Assert.AreEqual("anime", entry.Settings.AnimeCategory);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task SabnzbdMigrationIsSkippedAndTheLegacyFileStillRemovedWhenAClientAlreadyExists()
    {
        var directory = SabnzbdTestSupport.CreateTemporaryDirectory();
        try
        {
            var protection = new EphemeralDataProtectionProvider();
            var legacy = new SabnzbdSettingsStore(protection, directory);
            await legacy.SaveAsync(new SabnzbdStoredSettings("http://sabnzbd:8080", "legacy-key", "books", "anime"));
            var resolver = new SabnzbdConnectionResolver(legacy, SabnzbdTestSupport.Configuration());
            var clients = new DownloadClientStore(protection, directory);
            var existing = new DownloadClientEntry(
                Guid.NewGuid(), "Already configured", DownloadClientType.Sabnzbd, true, 1,
                new DownloadClientSettings("http://already.example", null, "books", "anime", null), "existing-key");
            await clients.SaveAsync(existing);

            var outcome = await DownloadClientSettingsMigration.MigrateSabnzbdAsync(resolver, clients, legacy);

            Assert.AreEqual(DownloadClientSettingsMigrationOutcome.CanonicalAlreadyPresent, outcome);
            Assert.IsFalse(legacy.Exists);
            var entries = await clients.LoadAllAsync();
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("existing-key", entries.Single().Secret);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task MissingLegacyFilesNeedNoMigration()
    {
        var directory = SabnzbdTestSupport.CreateTemporaryDirectory();
        try
        {
            var protection = new EphemeralDataProtectionProvider();
            var legacyIndexer = new ProwlarrSettingsStore(protection, directory);
            var indexers = new IndexerStore(protection, directory);
            Assert.AreEqual(
                IndexerSettingsMigrationOutcome.NothingToMigrate,
                await IndexerSettingsMigration.MigrateProwlarrAsync(legacyIndexer, indexers));

            var legacyClient = new SabnzbdSettingsStore(protection, directory);
            var resolver = new SabnzbdConnectionResolver(legacyClient, SabnzbdTestSupport.Configuration());
            var clients = new DownloadClientStore(protection, directory);
            Assert.AreEqual(
                DownloadClientSettingsMigrationOutcome.NothingToMigrate,
                await DownloadClientSettingsMigration.MigrateSabnzbdAsync(resolver, clients, legacyClient));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
