using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Import;
using Jularr.Web.Features.Acquisition.Monitoring;
using Jularr.Web.Features.Library;
using Jularr.Web.Features.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jularr.Tests;

// #411: offline storage never turns into deleted media, confident moves are relinked,
// uncertain ones need attention, and integrity findings come from the database only.
[TestClass]
public sealed class MediaIntegrityTests
{
    [TestMethod]
    public async Task MovedOrRenamedFileIsRelinkedAndKeepsItsIdentity()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        var root = await host.AddRootAsync("Anime");
        var original = Path.GetFullPath(host.WriteMedia(Path.Combine("Frieren", "Season 01", "Frieren - S01E01.mkv")));
        await ScanAsync(host, root.Id);
        var mediaId = await SingleMediaIdAsync(host);

        var renamed = Path.Combine(host.LibraryPath, "Frieren", "Season 01", "Frieren - S01E01 - Journey.mkv");
        File.Move(original, renamed);
        var result = await ScanAsync(host, root.Id);

        Assert.AreEqual(1, result.Relinked);
        Assert.AreEqual(0, result.Removed);
        Assert.AreEqual(0, result.Discovered);

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var media = await db.MediaFiles.AsNoTracking().SingleAsync();
        Assert.AreEqual(mediaId, media.Id, "The relinked file keeps its identity and analysis.");
        Assert.AreEqual(Path.GetFullPath(renamed), media.Path);
        Assert.IsTrue(await db.MediaAnalyses.AnyAsync(x => x.MediaFileId == mediaId));
    }

    [TestMethod]
    public async Task AmbiguousMovesAreNotGuessed()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        var root = await host.AddRootAsync("Anime");
        var sameTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var first = host.WriteMedia(Path.Combine("Frieren", "Season 01", "Frieren - S01E01.mkv"));
        var second = host.WriteMedia(Path.Combine("Frieren", "Season 01", "Frieren - S01E02.mkv"));
        File.SetLastWriteTimeUtc(first, sameTime);
        File.SetLastWriteTimeUtc(second, sameTime);
        await ScanAsync(host, root.Id);

        File.Move(first, Path.Combine(host.LibraryPath, "Frieren", "Season 01", "Frieren - S01E01 - A.mkv"));
        File.Move(second, Path.Combine(host.LibraryPath, "Frieren", "Season 01", "Frieren - S01E02 - B.mkv"));
        var result = await ScanAsync(host, root.Id);

        Assert.AreEqual(0, result.Relinked, "Two files with the same size and time cannot be told apart.");
        Assert.AreEqual(2, result.Removed);
        Assert.AreEqual(2, result.Discovered);
        Assert.AreEqual(2, result.Warnings.Count(x => x.Reason == "Needs attention: possibly moved media"));
    }

    [TestMethod]
    public async Task EmptyAndDuplicateMediaAreReportedWithoutTouchingStorage()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        var root = await host.AddRootAsync("Anime");
        host.WriteMedia(Path.Combine("Frieren", "Season 01", "Frieren - S01E01.mkv"));
        host.WriteMedia(Path.Combine("Frieren", "Season 01", "Frieren - S01E01 - Copy.mkv"));
        var empty = Path.Combine(host.LibraryPath, "Frieren", "Season 01", "Frieren - S01E02.mkv");
        await File.WriteAllBytesAsync(empty, []);

        var result = await ScanAsync(host, root.Id);
        Assert.AreEqual(1, result.Warnings.Count(x => x.Reason == "Empty media file"));

        // The summary is read from the database; the storage may be asleep meanwhile.
        Directory.Delete(host.LibraryPath, recursive: true);
        await using var scope = host.Services.CreateAsyncScope();
        var summary = (await new StorageIntegrityService(
                scope.ServiceProvider.GetRequiredService<AppDbContext>())
            .SummarizeAsync(CancellationToken.None))[root.Id];

        Assert.AreEqual(1, summary.DuplicateEpisodes);
        Assert.AreEqual(1, summary.EmptyFiles);
        Assert.AreEqual(3, summary.UnreadableFiles, "The fake probe rejects every file.");
        Assert.IsTrue(summary.NeedsAttention);
    }

    [TestMethod]
    public async Task OfflineStorageKeepsItsLibraryAndDefersStartupReconciliation()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        var root = await host.AddRootAsync("Anime");
        host.WriteMedia(Path.Combine("Frieren", "Season 01", "Frieren - S01E01.mkv"));
        await ScanAsync(host, root.Id);

        // The NAS goes to sleep: the mount point disappears.
        Directory.Delete(host.LibraryPath, recursive: true);
        var startup = new LibraryStartupScanService(
            host.ScopeFactory,
            host.Scans,
            new IdleLifetime(),
            NullLogger<LibraryStartupScanService>.Instance);

        var pending = await startup.QueueStartupAsync([root.Id], CancellationToken.None);
        CollectionAssert.AreEqual(new[] { root.Id }, pending.ToArray());
        Assert.AreEqual(0, (await host.ListScansAsync()).Count, "No scan runs against offline storage.");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.AreEqual(1, await db.MediaFiles.CountAsync(), "Offline storage never marks media as missing.");
        }

        // Once the storage is back, the owed reconciliation runs.
        host.WriteMedia(Path.Combine("Frieren", "Season 01", "Frieren - S01E01.mkv"));
        pending = await startup.QueueStartupAsync(pending, CancellationToken.None);
        Assert.AreEqual(0, pending.Count);
        Assert.AreEqual(1, (await host.ListScansAsync()).Count);
    }

    [TestMethod]
    public async Task ScanOfAnEmptyMountNeverRemovesMedia()
    {
        await using var host = await LibraryScanTestHost.CreateAsync();
        var root = await host.AddRootAsync("Anime");
        host.WriteMedia(Path.Combine("Frieren", "Season 01", "Frieren - S01E01.mkv"));
        host.WriteMedia(Path.Combine("Bocchi", "Season 01", "Bocchi - S01E01.mkv"));
        await ScanAsync(host, root.Id);

        // An unmounted share shows up as an empty directory.
        foreach (var directory in Directory.EnumerateDirectories(host.LibraryPath))
        {
            Directory.Delete(directory, recursive: true);
        }

        await Assert.ThrowsExactlyAsync<IOException>(() => ScanAsync(host, root.Id));
        await Assert.ThrowsExactlyAsync<IOException>(() => ScanFolderAsync(host, root.Id, "Frieren"));

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.AreEqual(2, await db.MediaFiles.CountAsync());
    }

    [TestMethod]
    public async Task ImportWaitsWhileTheLibraryStorageIsOffline()
    {
        const string release = "Frieren.S01E02.1080p.WEB-DL.AAC.H.264-GRP";
        await using var environment = await AnimeAcquisitionEnvironment.CreateAsync();
        await environment.SeedFrierenAsync();
        await environment.StartAcquisitionAsync(
            [new AnimeEpisodeKey(AnimeAcquisitionEnvironment.AnimeKey, 1, 2, 2)],
            release);
        var download = environment.AddCompletedDownload(release, $"{release}.mkv");
        var completed = await environment.CompleteLatestDownloadAsync(download);

        // The NAS behind the library root is offline while the download completes.
        var parked = environment.Root.Path + "-offline";
        Directory.Move(environment.Root.Path, parked);
        var deferred = await environment.ImportCompletedAsync(completed, download);

        Assert.AreEqual(AnimeImportStatus.Importing, deferred!.Status, "The import is deferred, not failed.");
        StringAssert.Contains(deferred.Message, "media storage");
        Assert.IsTrue(File.Exists(Path.Combine(download, $"{release}.mkv")), "Nothing moves towards offline storage.");
        Assert.IsFalse(Directory.Exists(environment.Root.Path), "No folder is created in place of the offline mount.");

        Directory.Move(parked, environment.Root.Path);
        var imported = await environment.ImportCompletedAsync(completed, download);

        Assert.AreEqual(AnimeImportStatus.Imported, imported!.Status, imported.Message);
        Assert.IsNotNull(await environment.MediaFileAsync(1, 2));
    }

    private static async Task<ScanResult> ScanAsync(LibraryScanTestHost host, Guid rootId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LibraryScanner>()
            .ScanAsync(rootId, CancellationToken.None);
    }

    private static async Task<ScanResult> ScanFolderAsync(LibraryScanTestHost host, Guid rootId, string folder)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LibraryScanner>()
            .ScanFolderAsync(rootId, folder, null, CancellationToken.None);
    }

    private static async Task<Guid> SingleMediaIdAsync(LibraryScanTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.MediaFiles.AsNoTracking().Select(x => x.Id).SingleAsync();
    }

    private sealed class IdleLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }
}
