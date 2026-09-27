using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Admin;
using Jularr.Web.Features.MediaMapping;
using Jularr.Web.Features.Playback.Decision;
using Jularr.Web.Features.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jularr.Tests;

/// <summary>The Admin overview (#518) must render even on a brand-new server with nothing in it.</summary>
[TestClass]
public sealed class AdminOverviewServiceTests
{
    [TestMethod]
    public async Task OverviewBuildsWithEmptyStoresAndReportsNothingToDo()
    {
        var path = TempDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(path);

            var service = new AdminOverviewService(
                db,
                new AcquisitionAccessStore(db),
                new MediaMappingReviewStore(NullLogger<MediaMappingReviewStore>.Instance),
                new LibraryRootAvailabilityService(db, new StorageAvailabilityCoordinator()),
                new PlaybackStreamSessionStore(TimeProvider.System));

            var overview = await service.GetAsync(CancellationToken.None);

            Assert.AreEqual(0, overview.FailedOperations);
            Assert.AreEqual(0, overview.ActiveDownloads);
            Assert.AreEqual(0, overview.ActiveProcessing);
            Assert.AreEqual(0, overview.OpenAcquisitionRequests);
            Assert.AreEqual(0, overview.UnresolvedMappings);
            Assert.AreEqual(0, overview.StorageRootsOffline);
            Assert.AreEqual(0, overview.BlockedJobs);
            Assert.AreEqual(0, overview.WatchingNow);
            Assert.AreEqual(0, overview.TranscodingNow);
            Assert.IsTrue(overview.ProcessCpuPercent is >= 0 and <= 100);
            Assert.IsTrue(overview.ProcessWorkingSetBytes > 0, "The current process always has a working set.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static string TempDatabasePath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"jularr-admin-overview-{Guid.NewGuid():N}.db");

    private static async Task<AppDbContext> CreateDatabaseAsync(string path)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path};Foreign Keys=True")
            .Options;

        var db = new AppDbContext(options);
        await DatabaseMigrationBridge.UpgradeAsync(db);
        return db;
    }
}
