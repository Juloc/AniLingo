using AniLingo.Web.Data;
using AniLingo.Web.Features.Acquisition.DownloadClients;
using AniLingo.Web.Features.Acquisition.Health;
using AniLingo.Web.Features.Operations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniLingo.Tests;

[TestClass]
public sealed class DownloadClientSelectionTests
{
    [TestMethod]
    public async Task SelectorOrdersByPriorityAndSkipsDisabled()
    {
        await using var environment = await Environment.CreateAsync();
        await environment.Clients.SaveAsync(Entry("low-priority", priority: 5));
        await environment.Clients.SaveAsync(Entry("high-priority", priority: 1));
        await environment.Clients.SaveAsync(Entry("disabled", priority: 0, enabled: false));

        var selected = await environment.Selector.SelectAsync(CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "high-priority", "low-priority" },
            selected.Select(entry => entry.Name).ToArray());
    }

    [TestMethod]
    public async Task SelectorSkipsUnhealthyEntries()
    {
        await using var environment = await Environment.CreateAsync();
        var unhealthy = Entry("unhealthy", priority: 1);
        var healthy = Entry("healthy", priority: 2);
        await environment.Clients.SaveAsync(unhealthy);
        await environment.Clients.SaveAsync(healthy);
        await environment.Health.RecordAsync(
            new AcquisitionHealthStatus(AcquisitionHealthKind.DownloadClient, unhealthy.Id, unhealthy.Name, true, false, "auth failed", DateTimeOffset.UtcNow));

        var selected = await environment.Selector.SelectAsync(CancellationToken.None);

        Assert.AreEqual("healthy", selected.Single().Name);
    }

    [TestMethod]
    public async Task SubmissionFailsOverToTheNextEnabledClientOnRejection()
    {
        await using var environment = await Environment.CreateAsync();
        var first = Entry("first", priority: 1);
        var second = Entry("second", priority: 2);
        await environment.Clients.SaveAsync(first);
        await environment.Clients.SaveAsync(second);

        // The first (highest-priority) entry always rejects; the second accepts.
        var client = new FakeDownloadClient("sab", succeeds: entry => entry.Name == "second");
        var service = environment.NewSubmissionService(client);

        var outcome = await service.SubmitAsync(Spec(), CancellationToken.None);

        Assert.IsTrue(outcome.Accepted);
        Assert.AreEqual(second.Id, outcome.ClientEntryId);
        Assert.AreEqual(2, client.SubmitCalls, "Both the rejected first attempt and the accepted second attempt were tried.");
    }

    [TestMethod]
    public async Task SubmissionTriesEachEnabledClientBeforeFailing()
    {
        await using var environment = await Environment.CreateAsync();
        var first = Entry("first", priority: 1);
        var second = Entry("second", priority: 2);
        await environment.Clients.SaveAsync(first);
        await environment.Clients.SaveAsync(second);

        var client = new FakeDownloadClient("sab", succeeds: _ => false);
        var service = environment.NewSubmissionService(client);

        var outcome = await service.SubmitAsync(Spec(), CancellationToken.None);

        Assert.IsFalse(outcome.Accepted);
        // Both enabled entries were tried (once each) before giving up.
        Assert.AreEqual(2, client.SubmitCalls);
        var operation = await new OperationStore(environment.Db).GetAsync(outcome.OperationId);
        Assert.AreEqual(OperationStatus.Failed, operation!.Status);
    }

    [TestMethod]
    public async Task SubmissionFailsImmediatelyWhenNoClientIsConfigured()
    {
        await using var environment = await Environment.CreateAsync();
        var service = environment.NewSubmissionService(new FakeDownloadClient("sab", succeeds: _ => true));

        var outcome = await service.SubmitAsync(Spec(), CancellationToken.None);

        Assert.IsFalse(outcome.Accepted);
        StringAssert.Contains(outcome.Message, "No enabled, healthy download client is configured");
    }

    private static DownloadSubmissionSpec Spec() =>
        new(
            "test-download",
            "Test download",
            "subject",
            null,
            new Uri("https://indexer.example/a.nzb"),
            "release-name");

    private static DownloadClientEntry Entry(
        string name,
        int priority,
        bool enabled = true) =>
        new(
            Guid.NewGuid(),
            name,
            DownloadClientType.Sabnzbd,
            enabled,
            priority,
            new DownloadClientSettings("http://client.example:8080", "books", "anime"),
            "secret");

    private sealed class FakeDownloadClient(
        string providerId,
        Func<DownloadClientEntry, bool> succeeds) : IDownloadClient
    {
        public int SubmitCalls { get; private set; }

        public string ProviderId => providerId;

        public Task<DownloadClientTestResult> TestAsync(DownloadClientEntry entry, CancellationToken cancellationToken) =>
            Task.FromResult(new DownloadClientTestResult(true, "1.0"));

        public Task<DownloadClientSubmitResult> SubmitAsync(
            DownloadClientEntry entry,
            DownloadClientSubmitRequest request,
            CancellationToken cancellationToken)
        {
            SubmitCalls++;
            return Task.FromResult(
                succeeds(entry)
                    ? new DownloadClientSubmitResult(true, $"job-{SubmitCalls}")
                    : new DownloadClientSubmitResult(false, null, "rejected by fake client"));
        }

        public Task<IReadOnlyList<DownloadClientJobStatus>> GetStatusAsync(
            DownloadClientEntry entry,
            IReadOnlyCollection<string> externalIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DownloadClientJobStatus>>([]);

        public Task<bool> DeleteAsync(
            DownloadClientEntry entry,
            string externalId,
            bool deleteFiles,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class Environment : IAsyncDisposable
    {
        private Environment(DirectoryInfo directory, AppDbContext db)
        {
            Directory = directory;
            Db = db;
            Protection = new EphemeralDataProtectionProvider();
        }

        public DirectoryInfo Directory { get; }
        public AppDbContext Db { get; }
        public IDataProtectionProvider Protection { get; }
        public DownloadClientStore Clients => new(Protection, Directory);
        public AcquisitionHealthStore Health => new(Directory);
        public DownloadClientSelector Selector => new(Clients, Health);

        public DownloadClientSubmissionService NewSubmissionService(IDownloadClient client) =>
            new(
                client,
                Selector,
                Db,
                NullLogger<DownloadClientSubmissionService>.Instance);

        public static async Task<Environment> CreateAsync()
        {
            var directory = SabnzbdTestSupport.CreateTemporaryDirectory();
            var db = await SabnzbdTestSupport.CreateDatabaseAsync(Path.Combine(directory.FullName, "anilingo.db"));
            return new Environment(directory, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(recursive: true);
        }
    }
}
