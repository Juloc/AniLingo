using AniLingo.Web.Data;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class StorageAvailabilityTests
{
    [TestMethod]
    public void WakeMacAddressIsNormalizedAndMagicPacketIsCorrect()
    {
        Assert.IsTrue(WakeOnLanService.TryNormalizeMacAddress(
            "aa-bb-cc-dd-ee-ff",
            out var normalized));
        Assert.AreEqual("AA:BB:CC:DD:EE:FF", normalized);

        var packet = WakeOnLanService.BuildMagicPacket(normalized!);

        Assert.AreEqual(102, packet.Length);
        CollectionAssert.AreEqual(
            Enumerable.Repeat((byte)0xFF, 6).ToArray(),
            packet[..6]);

        var expectedMac = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };
        for (var copy = 0; copy < 16; copy++)
        {
            CollectionAssert.AreEqual(
                expectedMac,
                packet[(6 + copy * 6)..(12 + copy * 6)]);
        }
    }

    [TestMethod]
    public void InvalidWakeConfigurationIsRejected()
    {
        Assert.IsFalse(WakeOnLanService.TryNormalizeMacAddress(
            "not-a-mac",
            out _));
        Assert.IsFalse(WakeOnLanService.TryResolveBroadcastAddress(
            "not-an-ip",
            out _));
        Assert.IsTrue(WakeOnLanService.TryResolveBroadcastAddress(
            null,
            out var defaultBroadcast));
        Assert.AreEqual("255.255.255.255", defaultBroadcast!.ToString());
    }

    [TestMethod]
    public void PlaybackRetryScheduleIsBoundedAndBacksOff()
    {
        Assert.AreEqual(TimeSpan.Zero, PlaybackAvailabilityRetry.DelayForAttempt(0));
        Assert.AreEqual(TimeSpan.FromSeconds(1), PlaybackAvailabilityRetry.DelayForAttempt(1));
        Assert.AreEqual(TimeSpan.FromSeconds(2), PlaybackAvailabilityRetry.DelayForAttempt(2));
        Assert.AreEqual(TimeSpan.FromSeconds(4), PlaybackAvailabilityRetry.DelayForAttempt(3));
        Assert.AreEqual(TimeSpan.FromSeconds(5), PlaybackAvailabilityRetry.DelayForAttempt(4));
        Assert.AreEqual(TimeSpan.FromSeconds(5), PlaybackAvailabilityRetry.DelayForAttempt(20));
        Assert.AreEqual(
            TimeSpan.FromSeconds(60),
            PlaybackAvailabilityRetry.MaximumAutomaticRetryWindow);
    }

    [TestMethod]
    public async Task RootProbeDistinguishesOnlineOfflineAndStarting()
    {
        var coordinator = new StorageAvailabilityCoordinator();
        var rootId = Guid.NewGuid();
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-storage-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempRoot);

            var online = await coordinator.ProbeAsync(
                rootId,
                tempRoot,
                wakeConfigured: true,
                force: true,
                CancellationToken.None);

            Assert.AreEqual(StorageAvailabilityState.Available, online.State);

            Directory.Delete(tempRoot, recursive: true);

            var offline = await coordinator.ProbeAsync(
                rootId,
                tempRoot,
                wakeConfigured: true,
                force: true,
                CancellationToken.None);

            Assert.AreEqual(StorageAvailabilityState.Offline, offline.State);
            Assert.IsTrue(offline.IsRetryable);

            Assert.IsTrue(coordinator.TryMarkWakeStarting(rootId));

            var starting = await coordinator.ProbeAsync(
                rootId,
                tempRoot,
                wakeConfigured: true,
                force: true,
                CancellationToken.None);

            Assert.AreEqual(StorageAvailabilityState.Starting, starting.State);
            Assert.IsFalse(coordinator.TryMarkWakeStarting(rootId));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task MediaProbeDistinguishesOfflineRootFromMissingFile()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-media-availability-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(tempRoot, "anilingo.db");
        var libraryPath = Path.Combine(tempRoot, "anime");
        Directory.CreateDirectory(libraryPath);

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
                .Options;

            await using var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var root = new LibraryRoot
            {
                Name = "Anime",
                Path = libraryPath
            };
            var anime = new Anime
            {
                Key = "test",
                Title = "Test"
            };
            var episode = new Episode
            {
                AnimeId = anime.Id,
                SeasonNumber = 1,
                Number = 1,
                Title = "Episode 1"
            };
            var media = new MediaFile
            {
                LibraryRootId = root.Id,
                EpisodeId = episode.Id,
                Path = Path.Combine(libraryPath, "Test", "Season 01", "episode.mkv"),
                SizeBytes = 123,
                LastWriteTimeUtc = DateTime.UtcNow
            };

            db.LibraryRoots.Add(root);
            db.Anime.Add(anime);
            db.Episodes.Add(episode);
            db.MediaFiles.Add(media);
            await db.SaveChangesAsync();

            var coordinator = new StorageAvailabilityCoordinator();
            var rootAvailability = new LibraryRootAvailabilityService(
                db,
                coordinator);
            var mediaAvailability = new MediaAvailabilityService(
                db,
                rootAvailability);

            var missing = await mediaAvailability.CheckMediaAsync(
                media.Id,
                force: true,
                CancellationToken.None);

            Assert.IsNotNull(missing);
            Assert.AreEqual(StorageAvailabilityState.FileMissing, missing.State);
            Assert.IsFalse(missing.Retryable);

            Directory.Delete(libraryPath, recursive: true);

            var offline = await mediaAvailability.CheckMediaAsync(
                media.Id,
                force: true,
                CancellationToken.None);

            Assert.IsNotNull(offline);
            Assert.AreEqual(StorageAvailabilityState.Offline, offline.State);
            Assert.IsTrue(offline.Retryable);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
