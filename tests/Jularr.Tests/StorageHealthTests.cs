using System.Net;
using System.Net.Sockets;
using Jularr.Web.Data;
using Jularr.Web.Features.Library;
using Jularr.Web.Features.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jularr.Tests;

// #411: canonical storage state, the Wake-on-LAN rule, coalesced on-demand wake and the
// bounded start attempt. No test sends a real packet: the sender is a fake.
[TestClass]
public sealed class StorageHealthTests
{
    private const string Mac = "AA:BB:CC:DD:EE:FF";

    [TestMethod]
    public void HealthFollowsTheWakeOnLanRule()
    {
        Assert.AreEqual(
            StorageHealthState.Online,
            StorageHealth.Resolve(StorageAvailabilityState.Available, wakeConfigured: false, null));
        Assert.AreEqual(
            StorageHealthState.Online,
            StorageHealth.Resolve(StorageAvailabilityState.FileMissing, wakeConfigured: true, null));
        Assert.AreEqual(
            StorageHealthState.Starting,
            StorageHealth.Resolve(StorageAvailabilityState.Starting, wakeConfigured: true, null));
        Assert.AreEqual(
            StorageHealthState.OfflineExpected,
            StorageHealth.Resolve(StorageAvailabilityState.Offline, wakeConfigured: true, "root_not_found"),
            "An unreachable Wake-on-LAN NAS sleeps on purpose.");
        Assert.AreEqual(
            StorageHealthState.OfflineExpected,
            StorageHealth.Resolve(StorageAvailabilityState.Unknown, wakeConfigured: true, null));
        Assert.AreEqual(
            StorageHealthState.OfflineUnexpected,
            StorageHealth.Resolve(StorageAvailabilityState.Offline, wakeConfigured: false, "root_not_found"),
            "Without Wake-on-LAN an unreachable root is a problem.");
        Assert.AreEqual(
            StorageHealthState.Error,
            StorageHealth.Resolve(StorageAvailabilityState.Offline, wakeConfigured: true, StorageDiagnosticCodes.WakeTimeout));
        Assert.AreEqual(
            StorageHealthState.Error,
            StorageHealth.Resolve(StorageAvailabilityState.Unreachable, wakeConfigured: true, "permission_denied"));

        Assert.AreEqual("offline_expected", StorageHealth.Name(StorageHealthState.OfflineExpected));
        Assert.AreEqual("8.4 TB", StorageHealth.FormatBytes(8_400_000_000_000));
    }

    [TestMethod]
    public async Task ConcurrentStartRequestsShareOneWakeAttemptAndContinueWhenOnline()
    {
        using var temp = new TempDirectory();
        var rootPath = Path.Combine(temp.Path, "nas");
        var sender = new FakeWakeSender(() => BootLater(rootPath, TimeSpan.FromMilliseconds(150)));
        var (availability, wake) = CreateCoordinators(sender, TimeSpan.FromSeconds(10));
        var target = new StorageWakeTarget(Guid.NewGuid(), rootPath, Mac, new IPEndPoint(IPAddress.Broadcast, 9), ExpectedNonEmpty: true);

        var attempts = new Task<LibraryRootAvailabilitySnapshot>[12];
        Parallel.For(0, attempts.Length, index => attempts[index] = wake.StartAsync(target));

        Assert.IsTrue(attempts.All(attempt => ReferenceEquals(attempt, attempts[0])), "All requests join one attempt.");
        Assert.IsTrue(wake.IsStarting(target.RootId));
        Assert.AreEqual(StorageHealthState.Starting, availability.GetCached(target.RootId, true)?.Health);

        var result = await attempts[0].WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual(StorageHealthState.Online, result.Health);
        Assert.AreEqual(1, sender.Sent, "Concurrent requests send exactly one Wake-on-LAN packet.");
        Assert.IsFalse(wake.IsStarting(target.RootId));
    }

    [TestMethod]
    public async Task StartAttemptIsBoundedAndCanBeRetried()
    {
        using var temp = new TempDirectory();
        var sender = new FakeWakeSender();
        var (availability, wake) = CreateCoordinators(sender, TimeSpan.FromMilliseconds(200));
        var target = new StorageWakeTarget(Guid.NewGuid(), Path.Combine(temp.Path, "never"), Mac, new IPEndPoint(IPAddress.Broadcast, 9), false);

        var failed = await wake.StartAsync(target).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual(StorageHealthState.Error, failed.Health);
        Assert.AreEqual(StorageDiagnosticCodes.WakeTimeout, failed.DiagnosticCode);
        Assert.AreEqual(StorageHealthState.Error, availability.GetCached(target.RootId, true)?.Health,
            "The failed start stays visible instead of falling back to 'sleeping'.");

        var retried = wake.StartAsync(target);
        Assert.AreEqual(StorageHealthState.Starting, availability.GetCached(target.RootId, true)?.Health);
        await retried.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(2, sender.Sent, "A retry after a failed start sends a new packet.");
    }

    [TestMethod]
    public async Task UnsendablePacketIsReportedAsError()
    {
        using var temp = new TempDirectory();
        var sender = new FakeWakeSender(failure: new SocketException((int)SocketError.NetworkUnreachable));
        var (_, wake) = CreateCoordinators(sender, TimeSpan.FromSeconds(10));
        var target = new StorageWakeTarget(Guid.NewGuid(), Path.Combine(temp.Path, "nas"), Mac, new IPEndPoint(IPAddress.Broadcast, 9), false);

        var result = await wake.StartAsync(target).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual(StorageHealthState.Error, result.Health);
        Assert.AreEqual(StorageDiagnosticCodes.WakeSendFailed, result.DiagnosticCode);
    }

    [TestMethod]
    public async Task PlaybackWakesSleepingStorageOnceAndBrowsingNeverDoes()
    {
        await using var fixture = await StorageFixture.CreateAsync(wakeOnLan: true);
        var sender = new FakeWakeSender(() => BootLater(fixture.MediaPath, TimeSpan.FromSeconds(1), createFile: true));
        var (media, roots) = fixture.Services(sender, TimeSpan.FromSeconds(10));

        // Browsing, health checks and background work only observe the storage.
        var observed = await media.CheckMediaAsync(fixture.MediaFileId, force: true, CancellationToken.None);
        var root = await roots.CheckAsync(fixture.RootId, force: true, CancellationToken.None);
        Assert.AreEqual(StorageHealthState.OfflineExpected, observed!.Health);
        Assert.AreEqual(StorageHealthState.OfflineExpected, root!.Health);
        Assert.AreEqual(0, sender.Sent, "Observing sleeping storage must not wake it.");

        // Several players press Play while the NAS starts (true concurrency is covered on the
        // coordinator above; one DbContext serves these calls one after another).
        for (var player = 0; player < 5; player++)
        {
            var requested = await media.CheckMediaAsync(fixture.MediaFileId, force: true, CancellationToken.None, wake: true);
            Assert.AreEqual(StorageAvailabilityState.Starting, requested!.State);
            Assert.AreEqual(StorageHealthState.Starting, requested.Health);
        }

        // The operation continues by itself once the bounded start attempt sees the storage.
        var ready = await roots.RequireAsync(fixture.RootId, waitForStart: true, CancellationToken.None);
        Assert.AreEqual(StorageHealthState.Online, ready!.Health);
        Assert.AreEqual(1, sender.Sent);

        var playable = await media.CheckMediaAsync(fixture.MediaFileId, force: true, CancellationToken.None, wake: true);
        Assert.AreEqual(StorageAvailabilityState.Available, playable!.State);
        Assert.AreEqual(1, sender.Sent, "Online storage is never woken again.");
    }

    [TestMethod]
    public async Task StorageWithoutWakeOnLanIsUnexpectedlyOfflineAndNotWoken()
    {
        await using var fixture = await StorageFixture.CreateAsync(wakeOnLan: false);
        var sender = new FakeWakeSender();
        var (media, roots) = fixture.Services(sender, TimeSpan.FromSeconds(10));

        var required = await roots.RequireAsync(fixture.RootId, waitForStart: true, CancellationToken.None);
        var playback = await media.CheckMediaAsync(fixture.MediaFileId, force: true, CancellationToken.None, wake: true);

        Assert.AreEqual(StorageHealthState.OfflineUnexpected, required!.Health);
        Assert.AreEqual(StorageHealthState.OfflineUnexpected, playback!.Health);
        Assert.AreEqual(0, sender.Sent);
    }

    private static (StorageAvailabilityCoordinator Availability, StorageWakeCoordinator Wake) CreateCoordinators(
        IWakeOnLanPacketSender sender,
        TimeSpan startTimeout)
    {
        var availability = new StorageAvailabilityCoordinator();
        var wake = new StorageWakeCoordinator(
            availability,
            sender,
            new StorageWakeOptions { StartTimeout = startTimeout, PollInterval = TimeSpan.FromMilliseconds(20) },
            NullLogger<StorageWakeCoordinator>.Instance);
        return (availability, wake);
    }

    // Simulates a NAS that becomes readable a moment after the magic packet.
    private static void BootLater(string path, TimeSpan delay, bool createFile = false) =>
        _ = Task.Run(async () =>
        {
            await Task.Delay(delay);
            if (createFile)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, [0x00]);
            }
            else
            {
                Directory.CreateDirectory(path);
                await File.WriteAllTextAsync(Path.Combine(path, "marker"), "");
            }
        });

    private sealed class FakeWakeSender(Action? onSend = null, Exception? failure = null) : IWakeOnLanPacketSender
    {
        private int sent;

        public int Sent => Volatile.Read(ref sent);

        public Task SendAsync(string normalizedMacAddress, IPEndPoint broadcastEndpoint, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref sent);
            if (failure is not null)
            {
                throw failure;
            }

            onSend?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"jularr-storage-health-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    // One library root with one known media file whose storage (the root folder) is absent.
    private sealed class StorageFixture : IAsyncDisposable
    {
        private readonly TempDirectory temp;
        private readonly AppDbContext db;

        private StorageFixture(TempDirectory temp, AppDbContext db, Guid rootId, Guid mediaFileId, string mediaPath)
        {
            this.temp = temp;
            this.db = db;
            RootId = rootId;
            MediaFileId = mediaFileId;
            MediaPath = mediaPath;
        }

        public Guid RootId { get; }
        public Guid MediaFileId { get; }
        public string MediaPath { get; }

        public static async Task<StorageFixture> CreateAsync(bool wakeOnLan)
        {
            var temp = new TempDirectory();
            var libraryPath = Path.Combine(temp.Path, "anime");
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(temp.Path, "jularr.db")};Foreign Keys=True")
                .Options);
            await db.Database.EnsureCreatedAsync();

            var root = new LibraryRoot
            {
                Name = "bay4plex",
                Path = libraryPath,
                WakeOnLanEnabled = wakeOnLan,
                WakeMacAddress = wakeOnLan ? Mac : null
            };
            var anime = new Anime { Key = "frieren", Title = "Frieren" };
            var episode = new Episode { AnimeId = anime.Id, Number = 1, Title = "Episode 1" };
            var media = new MediaFile
            {
                LibraryRootId = root.Id,
                EpisodeId = episode.Id,
                Path = Path.Combine(libraryPath, "Frieren", "Season 01", "Frieren - S01E01.mkv"),
                SizeBytes = 1,
                LastWriteTimeUtc = DateTime.UtcNow
            };
            db.LibraryRoots.Add(root);
            db.Anime.Add(anime);
            db.Episodes.Add(episode);
            db.MediaFiles.Add(media);
            await db.SaveChangesAsync();

            return new StorageFixture(temp, db, root.Id, media.Id, media.Path);
        }

        public (MediaAvailabilityService Media, LibraryRootAvailabilityService Roots) Services(
            IWakeOnLanPacketSender sender,
            TimeSpan startTimeout)
        {
            var (availability, wake) = CreateCoordinators(sender, startTimeout);
            var roots = new LibraryRootAvailabilityService(db, availability, wake);
            return (new MediaAvailabilityService(db, roots), roots);
        }

        public async ValueTask DisposeAsync()
        {
            await db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            temp.Dispose();
        }
    }
}
