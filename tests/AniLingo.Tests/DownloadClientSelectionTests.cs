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
    public async Task SelectorOrdersByPriorityWithinProtocolAndSkipsDisabled()
    {
        await using var environment = await Environment.CreateAsync();
        await environment.Clients.SaveAsync(Entry("low-priority-usenet", DownloadClientType.Sabnzbd, priority: 5));
        await environment.Clients.SaveAsync(Entry("high-priority-usenet", DownloadClientType.Sabnzbd, priority: 1));
        await environment.Clients.SaveAsync(Entry("disabled-usenet", DownloadClientType.Sabnzbd, priority: 0, enabled: false));
        await environment.Clients.SaveAsync(Entry("only-torrent", DownloadClientType.QBittorrent, priority: 1));

        var selected = await environment.Selector.SelectAsync(DownloadProtocol.Usenet, CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "high-priority-usenet", "low-priority-usenet" },
            selected.Select(entry => entry.Name).ToArray());
    }

    [TestMethod]
    public async Task SelectorSkipsUnhealthyEntries()
    {
        await using var environment = await Environment.CreateAsync();
        var unhealthy = Entry("unhealthy", DownloadClientType.Sabnzbd, priority: 1);
        var healthy = Entry("healthy", DownloadClientType.Sabnzbd, priority: 2);
        await environment.Clients.SaveAsync(unhealthy);
        await environment.Clients.SaveAsync(healthy);
        await environment.Health.RecordAsync(
            new AcquisitionHealthStatus(AcquisitionHealthKind.DownloadClient, unhealthy.Id, unhealthy.Name, true, false, "auth failed", DateTimeOffset.UtcNow));

        var selected = await environment.Selector.SelectAsync(DownloadProtocol.Usenet, CancellationToken.None);

        Assert.AreEqual("healthy", selected.Single().Name);
    }

    [TestMethod]
    public async Task SubmissionFailsOverToTheNextEnabledClientOnRejection()
    {
        await using var environment = await Environment.CreateAsync();
        var first = Entry("first", DownloadClientType.Sabnzbd, priority: 1);
        var second = Entry("second", DownloadClientType.Sabnzbd, priority: 2);
        await environment.Clients.SaveAsync(first);
        await environment.Clients.SaveAsync(second);

        // The first (highest-priority) entry always rejects; the second accepts.
        var client = new FakeDownloadClient(
            DownloadClientType.Sabnzbd,
            DownloadProtocol.Usenet,
            "sab",
            succeeds: entry => entry.Name == "second");
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
        var first = Entry("first", DownloadClientType.Sabnzbd, priority: 1);
        var second = Entry("second", DownloadClientType.Sabnzbd, priority: 2);
        await environment.Clients.SaveAsync(first);
        await environment.Clients.SaveAsync(second);

        var client = new FakeDownloadClient(DownloadClientType.Sabnzbd, DownloadProtocol.Usenet, "sab", succeeds: _ => false);
        var service = environment.NewSubmissionService(client);

        var outcome = await service.SubmitAsync(Spec(), CancellationToken.None);

        Assert.IsFalse(outcome.Accepted);
        // Both enabled entries were tried (once each) before giving up.
        Assert.AreEqual(2, client.SubmitCalls);
        var operation = await new OperationStore(environment.Db).GetAsync(outcome.OperationId);
        Assert.AreEqual(OperationStatus.Failed, operation!.Status);
    }

    [TestMethod]
    public async Task SubmissionFailsImmediatelyWhenNoClientSupportsTheProtocol()
    {
        await using var environment = await Environment.CreateAsync();
        await environment.Clients.SaveAsync(Entry("torrent-only", DownloadClientType.QBittorrent, priority: 1));
        var service = environment.NewSubmissionService(new FakeDownloadClient(DownloadClientType.QBittorrent, DownloadProtocol.Torrent, "qbit", succeeds: _ => true));

        var outcome = await service.SubmitAsync(Spec(), CancellationToken.None);

        Assert.IsFalse(outcome.Accepted);
        StringAssert.Contains(outcome.Message, "usenet");
    }

    private static DownloadSubmissionSpec Spec() =>
        new(
            "test-download",
            "Test download",
            "subject",
            null,
            DownloadProtocol.Usenet,
            new Uri("https://indexer.example/a.nzb"),
            MagnetUri: null,
            "release-name");

    private static DownloadClientEntry Entry(
        string name,
        DownloadClientType type,
        int priority,
        bool enabled = true) =>
        new(
            Guid.NewGuid(),
            name,
            type,
            enabled,
            priority,
            new DownloadClientSettings("http://client.example:8080", null, "books", "anime", null),
            "secret");

    private sealed class FakeDownloadClient(
        DownloadClientType type,
        DownloadProtocol protocol,
        string providerId,
        Func<DownloadClientEntry, bool> succeeds) : IDownloadClient
    {
        public int SubmitCalls { get; private set; }

        public DownloadClientType Type => type;
        public DownloadProtocol Protocol => protocol;
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
                new Dictionary<DownloadClientType, IDownloadClient> { [client.Type] = client },
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
