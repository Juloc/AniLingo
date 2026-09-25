using System.Text.Json;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Operations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class OperationCoverageTests
{
    [TestMethod]
    public async Task ExternalRunningOperationSurvivesWorkerRestartReconciliation()
    {
        var path = TempDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(path);
            var store = new OperationStore(db);

            var localId = await store.CreateAsync(
                new OperationDescriptor(
                    "local-test",
                    "Test",
                    "Local operation",
                    Lane: OperationLane.Normal));

            var externalId = await store.CreateAsync(
                new OperationDescriptor(
                    "sabnzbd-download",
                    "External downloads",
                    "SAB download",
                    Lane: OperationLane.Normal,
                    IsDownload: true,
                    Retryable: false,
                    ExternalProvider: SabnzbdOperationsClient.ProviderId,
                    ExternalId: "SABnzbd_nzo_test"));

            await store.MarkRunningAsync(localId);
            await store.MarkRunningAsync(externalId);

            var recovered = await store.RecoverInterruptedAsync(
                OperationLane.Normal);

            Assert.AreEqual(1, recovered);

            var local = await store.GetAsync(localId);
            var external = await store.GetAsync(externalId);

            Assert.IsNotNull(local);
            Assert.IsNotNull(external);
            Assert.AreEqual(OperationStatus.Interrupted, local.Status);
            Assert.AreEqual(OperationStatus.Running, external.Status);
            Assert.AreEqual("sabnzbd", external.ExternalProvider);
            Assert.AreEqual("SABnzbd_nzo_test", external.ExternalId);
            Assert.IsFalse(external.CanCancel);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [TestMethod]
    public void SabnzbdSnapshotParsesQueueProgressAndHistoryOutcome()
    {
        using var queue = JsonDocument.Parse(
            """
            {
              "queue": {
                "kbpersec": "512.5",
                "slots": [
                  {
                    "nzo_id": "SABnzbd_nzo_active",
                    "filename": "Example Book",
                    "status": "Downloading",
                    "mb": "100.00",
                    "mbleft": "25.00",
                    "percentage": "75",
                    "timeleft": "0:02:00"
                  }
                ]
              }
            }
            """);

        using var history = JsonDocument.Parse(
            """
            {
              "history": {
                "slots": [
                  {
                    "nzo_id": "SABnzbd_nzo_done",
                    "name": "Finished Book",
                    "status": "Completed",
                    "bytes": 10485760
                  },
                  {
                    "nzo_id": "SABnzbd_nzo_failed",
                    "name": "Failed Book",
                    "status": "Failed",
                    "bytes": 0
                  }
                ]
              }
            }
            """);

        var now = new DateTime(
            2026,
            9,
            25,
            9,
            0,
            0,
            DateTimeKind.Utc);

        var snapshot = SabnzbdOperationsClient.ParseSnapshot(
            queue.RootElement,
            history.RootElement,
            now);

        Assert.AreEqual(3, snapshot.Jobs.Count);
        Assert.AreEqual(512.5 * 1024d, snapshot.QueueBytesPerSecond);

        var active = snapshot.Jobs.Single(x => x.Id == "SABnzbd_nzo_active");
        Assert.AreEqual("Example Book", active.Name);
        Assert.AreEqual("Downloading", active.Status);
        Assert.AreEqual(75, active.ProgressPercent);
        Assert.AreEqual(100L * 1024L * 1024L, active.BytesTotal);
        Assert.AreEqual(75L * 1024L * 1024L, active.BytesCompleted);
        Assert.AreEqual(now.AddMinutes(2), active.EtaUtc);
        Assert.IsFalse(active.IsHistory);

        var completed = snapshot.Jobs.Single(x => x.Id == "SABnzbd_nzo_done");
        Assert.AreEqual("Completed", completed.Status);
        Assert.IsTrue(completed.IsHistory);
        Assert.AreEqual(10L * 1024L * 1024L, completed.BytesTotal);

        var failed = snapshot.Jobs.Single(x => x.Id == "SABnzbd_nzo_failed");
        Assert.AreEqual("Failed", failed.Status);
        Assert.IsTrue(failed.IsHistory);
    }

    [TestMethod]
    public async Task ExternalReferenceCanBeAttachedAfterSubmission()
    {
        var path = TempDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(path);
            var store = new OperationStore(db);

            var id = await store.CreateAsync(
                new OperationDescriptor(
                    "sabnzbd-download",
                    "External downloads",
                    "SAB download",
                    IsDownload: true,
                    Retryable: false,
                    ExternalProvider: SabnzbdOperationsClient.ProviderId));

            await store.MarkRunningAsync(id);
            await store.SetExternalReferenceAsync(
                id,
                SabnzbdOperationsClient.ProviderId,
                "SABnzbd_nzo_late");

            var active = await store.ListActiveExternalAsync(
                SabnzbdOperationsClient.ProviderId);

            Assert.AreEqual(1, active.Count);
            Assert.AreEqual(id, active[0].Id);
            Assert.AreEqual("SABnzbd_nzo_late", active[0].ExternalId);
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
            $"anilingo-operation-coverage-{Guid.NewGuid():N}.db");

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
