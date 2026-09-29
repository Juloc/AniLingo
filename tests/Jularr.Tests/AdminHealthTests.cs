using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.DownloadClients;
using Jularr.Web.Features.Acquisition.Health;
using Jularr.Web.Features.Acquisition.Indexers;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Health;
using Jularr.Web.Features.Storage;
using Jularr.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jularr.Tests;

/// <summary>
/// Admin &gt; Health (#528, part of epic #510): dedicated system-health and update-status page.
/// Focuses on the parsing/classification/aggregation logic (ffmpeg/ffprobe version parsing,
/// GitHub release comparison, OK/warning/error state derivation, empty-server aggregation) rather
/// than plumbing already covered elsewhere (OperationStoreTests, StorageAvailabilityTests,
/// AcquisitionHealthTests).
/// </summary>
[TestClass]
public sealed class AdminHealthTests
{
    [TestMethod]
    public void HealthPageUsesTheAdminSystemPolicy()
    {
        var authorize = typeof(Jularr.Web.Pages.Admin.HealthModel)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .SingleOrDefault();

        Assert.IsNotNull(authorize, "Admin/Health must require authorization.");
        Assert.AreEqual(
            JularrPolicies.AdminSystem,
            authorize.Policy,
            "Admin/Health is owner-only diagnostics, not day-to-day media admin.");
    }

    [TestMethod]
    [DataRow("ffmpeg version 6.1.1-3ubuntu5 Copyright (c) 2000-2023 the FFmpeg developers\nbuilt with gcc", "6.1.1-3ubuntu5")]
    [DataRow("ffprobe version 6.1.1-3ubuntu5 Copyright (c) 2000-2023 the FFmpeg developers", "6.1.1-3ubuntu5")]
    [DataRow("ffmpeg version n7.0\n", "n7.0")]
    [DataRow("", null)]
    [DataRow("customtool build 42 ready", "customtool build 42 ready")]
    public void ParseToolVersionReadsTheFirstLineVersionToken(string output, string? expected)
    {
        Assert.AreEqual(expected, SystemHealthService.ParseToolVersion(output));
    }

    [TestMethod]
    public void BuildResultIsUpToDateWhenTheNormalizedTagMatchesTheRunningVersion()
    {
        var now = DateTimeOffset.UtcNow;

        var matching = GitHubReleaseCheckService.BuildResult("0.1.0-alpha.49", "v0.1.0-alpha.49", "https://github.com/Juloc/Jularr/releases/tag/v0.1.0-alpha.49", now);
        Assert.AreEqual(UpdateCheckState.UpToDate, matching.State);
        Assert.AreEqual("0.1.0-alpha.49", matching.LatestVersion);
        Assert.AreEqual(now, matching.CheckedAtUtc);

        var newer = GitHubReleaseCheckService.BuildResult("0.1.0-alpha.49", "v0.1.0-alpha.50", "https://github.com/Juloc/Jularr/releases/tag/v0.1.0-alpha.50", now);
        Assert.AreEqual(UpdateCheckState.UpdateAvailable, newer.State);
        Assert.AreEqual("0.1.0-alpha.50", newer.LatestVersion);
        Assert.AreEqual("https://github.com/Juloc/Jularr/releases/tag/v0.1.0-alpha.50", newer.ReleaseUrl);

        // Tags without a leading "v" (unusual, but not something the check should choke on).
        var noVPrefix = GitHubReleaseCheckService.BuildResult("0.1.0-alpha.49", "0.1.0-alpha.49", null, now);
        Assert.AreEqual(UpdateCheckState.UpToDate, noVPrefix.State);
    }

    [TestMethod]
    public void GetCachedNeverTriggersAnHttpRequest()
    {
        // Admin/Health's GET must never block on GitHub (#528): GetCached only reads memory.
        var service = new GitHubReleaseCheckService(new ThrowingHttpClientFactory(), TimeProvider.System);

        Assert.IsNull(service.GetCached(), "No check has been requested yet.");
    }

    [TestMethod]
    public void StorageRootHealthStateReflectsOnlineAndWritable()
    {
        var rootId = Guid.NewGuid();

        Assert.AreEqual(HealthState.Error, new StorageRootHealthRow(rootId, "Anime", "/data/anime", Online: false, Writable: false).State);
        Assert.AreEqual(HealthState.Warning, new StorageRootHealthRow(rootId, "Anime", "/data/anime", Online: true, Writable: false).State);
        Assert.AreEqual(HealthState.Ok, new StorageRootHealthRow(rootId, "Anime", "/data/anime", Online: true, Writable: true).State);
    }

    [TestMethod]
    public void ExternalToolStateReflectsAvailability()
    {
        Assert.AreEqual(HealthState.Ok, new ExternalToolStatus("ffmpeg", true, "6.1.1", null).State);
        Assert.AreEqual(HealthState.Error, new ExternalToolStatus("ffmpeg", false, null, "not found").State);
    }

    [TestMethod]
    public void JobQueueStateWarnsOnlyWhenSomethingIsBlocked()
    {
        Assert.AreEqual(HealthState.Ok, new JobQueueHealth(Running: 3, Queued: 1, Blocked: 0).State);
        Assert.AreEqual(HealthState.Warning, new JobQueueHealth(Running: 0, Queued: 0, Blocked: 1).State);
    }

    [TestMethod]
    public void AcquisitionDependencyStateDistinguishesNotConfiguredFromUnhealthy()
    {
        Assert.IsFalse(new AcquisitionDependencySummary(0, 0, 0, 0).IsConfigured);
        Assert.AreEqual(HealthState.Warning, new AcquisitionDependencySummary(0, 0, 0, 0).State);

        Assert.AreEqual(HealthState.Ok, new AcquisitionDependencySummary(2, 2, 1, 1).State);
        Assert.AreEqual(HealthState.Error, new AcquisitionDependencySummary(1, 2, 1, 1).State);
    }

    [TestMethod]
    public async Task SystemHealthServiceAggregatesCleanlyOnABrandNewServer()
    {
        var databasePath = TempDatabasePath();
        var acquisitionDirectory = new DirectoryInfo(Path.Combine(
            Path.GetTempPath(),
            $"jularr-health-acquisition-{Guid.NewGuid():N}"));
        acquisitionDirectory.Create();

        try
        {
            await using var db = await CreateDatabaseAsync(databasePath);
            var protection = new EphemeralDataProtectionProvider();

            var service = new SystemHealthService(
                db,
                new LibraryRootAvailabilityService(db, new StorageAvailabilityCoordinator()),
                new MediaProcessRunner(NullLogger<MediaProcessRunner>.Instance),
                new IndexerStore(protection, acquisitionDirectory),
                new DownloadClientStore(protection, acquisitionDirectory),
                new AcquisitionHealthStore(acquisitionDirectory));

            var snapshot = await service.GetAsync(CancellationToken.None);

            Assert.IsTrue(snapshot.Database.Reachable, "A freshly migrated database must be reachable.");
            Assert.IsTrue(snapshot.Database.FileSizeBytes > 0, "The migrated database file must exist on disk.");
            Assert.AreEqual(HealthState.Ok, snapshot.Database.State);

            Assert.AreEqual(0, snapshot.StorageRoots.Count, "No library roots are configured yet.");

            // ffmpeg/ffprobe may or may not be installed in the environment running the tests;
            // either way the result must be internally consistent.
            AssertToolResultIsConsistent(snapshot.Ffmpeg);
            AssertToolResultIsConsistent(snapshot.Ffprobe);

            Assert.AreEqual(0, snapshot.JobQueue.Running);
            Assert.AreEqual(0, snapshot.JobQueue.Queued);
            Assert.AreEqual(0, snapshot.JobQueue.Blocked);
            Assert.AreEqual(HealthState.Ok, snapshot.JobQueue.State);

            Assert.AreEqual(0, snapshot.Acquisition.TotalIndexers);
            Assert.AreEqual(0, snapshot.Acquisition.TotalDownloadClients);
            Assert.IsFalse(snapshot.Acquisition.IsConfigured);
            Assert.AreEqual(HealthState.Warning, snapshot.Acquisition.State);
        }
        finally
        {
            File.Delete(databasePath);
            if (acquisitionDirectory.Exists)
            {
                acquisitionDirectory.Delete(recursive: true);
            }
        }
    }

    private static void AssertToolResultIsConsistent(ExternalToolStatus tool)
    {
        if (tool.IsAvailable)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(tool.Version), $"{tool.Name} reported available without a version.");
            Assert.IsNull(tool.Error);
        }
        else
        {
            Assert.IsNull(tool.Version);
            Assert.IsFalse(string.IsNullOrWhiteSpace(tool.Error), $"{tool.Name} reported unavailable without an error.");
        }
    }

    private static string TempDatabasePath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"jularr-health-db-{Guid.NewGuid():N}.db");

    private static async Task<AppDbContext> CreateDatabaseAsync(string path)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path};Foreign Keys=True")
            .Options;

        var db = new AppDbContext(options);
        await DatabaseMigrationBridge.UpgradeAsync(db);
        return db;
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            throw new InvalidOperationException("GetCached must never create an HTTP client.");
    }
}
