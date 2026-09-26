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
                new DownloadClientSettings("http://already.example", "books", "anime"), "existing-key");
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
    public async Task RemoveUnsupportedEntriesAsyncRemovesTorznabIndexersAndKeepsUsenetOnesIdempotently()
    {
        // AniLingo is usenet-only: an indexer entry persisted by an earlier build with a torrent
        // (Torznab) type is no longer a defined IndexerType member and must be dropped, never
        // silently reinterpreted as a supported type. Simulated here by casting the removed type's
        // old numeric value (Torznab was IndexerType value 2) the same way an old JSON file would
        // deserialize.
        var directory = SabnzbdTestSupport.CreateTemporaryDirectory();
        try
        {
            var protection = new EphemeralDataProtectionProvider();
            var indexers = new IndexerStore(protection, directory);
            var torrentEntry = new IndexerEntry(
                Guid.NewGuid(), "Old torrent indexer", (IndexerType)2, true, 1,
                IndexerSettings.CreateDefault("https://torrent-indexer.example", IndexerType.Newznab), "torrent-key");
            var usenetEntry = new IndexerEntry(
                Guid.NewGuid(), "Usenet indexer", IndexerType.Newznab, true, 1,
                IndexerSettings.CreateDefault("https://usenet-indexer.example", IndexerType.Newznab), "usenet-key");
            await indexers.SaveAsync(torrentEntry);
            await indexers.SaveAsync(usenetEntry);

            var messages = new List<string>();
            var removed = await IndexerSettingsMigration.RemoveUnsupportedEntriesAsync(indexers, messages.Add);

            Assert.AreEqual(1, removed);
            Assert.AreEqual(1, messages.Count);
            StringAssert.Contains(messages[0], "Old torrent indexer");
            var remaining = await indexers.LoadAllAsync();
            Assert.AreEqual(usenetEntry.Id, remaining.Single().Id);

            // Idempotent: nothing left to remove on a second run, no warning logged.
            var secondMessages = new List<string>();
            var secondRemoved = await IndexerSettingsMigration.RemoveUnsupportedEntriesAsync(indexers, secondMessages.Add);
            Assert.AreEqual(0, secondRemoved);
            Assert.AreEqual(0, secondMessages.Count);
            Assert.AreEqual(usenetEntry.Id, (await indexers.LoadAllAsync()).Single().Id);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task RemoveUnsupportedEntriesAsyncRemovesQBittorrentClientsAndKeepsSabnzbdOnesIdempotently()
    {
        // AniLingo is usenet-only: a download client entry persisted by an earlier build with a
        // torrent (qBittorrent) type is no longer a defined DownloadClientType member and must be
        // dropped. Simulated by casting the removed type's old numeric value (QBittorrent was
        // DownloadClientType value 1) the same way an old JSON file would deserialize.
        var directory = SabnzbdTestSupport.CreateTemporaryDirectory();
        try
        {
            var protection = new EphemeralDataProtectionProvider();
            var clients = new DownloadClientStore(protection, directory);
            var torrentEntry = new DownloadClientEntry(
                Guid.NewGuid(), "Old qBittorrent client", (DownloadClientType)1, true, 1,
                new DownloadClientSettings("http://qbittorrent.example:8080", null, "anime"), "torrent-secret");
            var usenetEntry = new DownloadClientEntry(
                Guid.NewGuid(), "SABnzbd", DownloadClientType.Sabnzbd, true, 1,
                new DownloadClientSettings("http://sabnzbd.example:8080", "books", "anime"), "usenet-secret");
            await clients.SaveAsync(torrentEntry);
            await clients.SaveAsync(usenetEntry);

            var messages = new List<string>();
            var removed = await DownloadClientSettingsMigration.RemoveUnsupportedEntriesAsync(clients, messages.Add);

            Assert.AreEqual(1, removed);
            Assert.AreEqual(1, messages.Count);
            StringAssert.Contains(messages[0], "Old qBittorrent client");
            var remaining = await clients.LoadAllAsync();
            Assert.AreEqual(usenetEntry.Id, remaining.Single().Id);

            // Idempotent: nothing left to remove on a second run, no warning logged.
            var secondMessages = new List<string>();
            var secondRemoved = await DownloadClientSettingsMigration.RemoveUnsupportedEntriesAsync(clients, secondMessages.Add);
            Assert.AreEqual(0, secondRemoved);
            Assert.AreEqual(0, secondMessages.Count);
            Assert.AreEqual(usenetEntry.Id, (await clients.LoadAllAsync()).Single().Id);
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
