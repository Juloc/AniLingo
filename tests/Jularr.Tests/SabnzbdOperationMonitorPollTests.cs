using Jularr.Web.Features.Acquisition;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.DownloadClients;
using Jularr.Web.Features.Acquisition.Sabnzbd;
using Jularr.Web.Features.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jularr.Tests;

/// <summary>
/// Multi-client behaviour of the SABnzbd monitor pass (review #485 item 5): every client is
/// polled on its own, downloads stay pinned to the client that accepted them, and a removed or
/// disabled client neither blocks the others nor floods the log.
/// </summary>
[TestClass]
public sealed class SabnzbdOperationMonitorPollTests
{
    private const string UrlA = "http://sab-a:8080";
    private const string UrlB = "http://sab-b:8080";

    [TestMethod]
    public async Task UnreachableClientDoesNotStopUpdatesForOtherClients()
    {
        await using var host = await Host.CreateAsync();
        var onA = await host.CreateDownloadAsync(host.ClientA.Id, "nzo-a");
        var onB = await host.CreateDownloadAsync(host.ClientB.Id, "nzo-b");
        host.Sab.Down.Add(UrlA);
        host.Sab.Completed(UrlB, "nzo-b");

        var active = await host.PollAsync(DateTime.UtcNow);

        Assert.IsTrue(active);
        Assert.AreEqual(OperationStatus.Running, (await host.Operations.GetAsync(onA))!.Status);
        Assert.AreEqual(OperationStatus.Succeeded, (await host.Operations.GetAsync(onB))!.Status);
        Assert.AreEqual(1, host.Logs.Warnings.Count(message => message.Contains("sab-a-name", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task DownloadIsResolvedOnlyOnTheClientThatAcceptedIt()
    {
        await using var host = await Host.CreateAsync();
        var download = await host.CreateDownloadAsync(host.ClientB.Id, "nzo-shared");
        // The same job id means something else on client A; it must not be consulted.
        host.Sab.Failed(UrlA, "nzo-shared");
        host.Sab.Completed(UrlB, "nzo-shared");

        await host.PollAsync(DateTime.UtcNow);

        Assert.AreEqual(OperationStatus.Succeeded, (await host.Operations.GetAsync(download))!.Status);
        CollectionAssert.DoesNotContain(host.Sab.Queried, UrlA);
        CollectionAssert.Contains(host.Sab.Queried, UrlB);
    }

    [TestMethod]
    public async Task DownloadOnARemovedClientFailsSoItsWorkflowCanMoveOn()
    {
        await using var host = await Host.CreateAsync();
        var orphan = await host.CreateDownloadAsync(Guid.NewGuid(), "nzo-gone");

        await host.PollAsync(DateTime.UtcNow);

        var stored = (await host.Operations.GetAsync(orphan))!;
        Assert.AreEqual(OperationStatus.Failed, stored.Status);
        Assert.AreEqual(SabnzbdOperationMonitorService.RemovedClientMessage, stored.Error);
        Assert.AreEqual(0, host.Sab.Queried.Count);
    }

    [TestMethod]
    public async Task DownloadOnADisabledClientStaysActiveAndItsWarningIsRateLimited()
    {
        await using var host = await Host.CreateAsync();
        var parked = await host.CreateDownloadAsync(host.Disabled.Id, "nzo-parked");
        var now = DateTime.UtcNow;

        await host.PollAsync(now);
        await host.PollAsync(now.AddMinutes(1));
        await host.PollAsync(now.AddSeconds(3));
        await host.PollAsync(now + SabnzbdOperationMonitorService.WarningInterval + TimeSpan.FromMinutes(1));

        Assert.AreEqual(OperationStatus.Running, (await host.Operations.GetAsync(parked))!.Status);
        Assert.AreEqual(2, host.Logs.Warnings.Count(message => message.Contains("disabled", StringComparison.Ordinal)));
        Assert.AreEqual(0, host.Sab.Queried.Count);
    }

    private sealed class Host : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        private readonly SabnzbdOperationMonitorService monitor;

        private Host(
            SabnzbdTestEnvironment environment,
            ServiceProvider services,
            SabnzbdOperationMonitorService monitor,
            RoutingSabnzbdClient sab,
            RecordingLogger<SabnzbdOperationMonitorService> logs,
            DownloadClientEntry clientA,
            DownloadClientEntry clientB,
            DownloadClientEntry disabled)
        {
            Environment = environment;
            this.services = services;
            this.monitor = monitor;
            Sab = sab;
            Logs = logs;
            ClientA = clientA;
            ClientB = clientB;
            Disabled = disabled;
        }

        public SabnzbdTestEnvironment Environment { get; }
        public RoutingSabnzbdClient Sab { get; }
        public RecordingLogger<SabnzbdOperationMonitorService> Logs { get; }
        public DownloadClientEntry ClientA { get; }
        public DownloadClientEntry ClientB { get; }
        public DownloadClientEntry Disabled { get; }
        public OperationStore Operations => new(Environment.Db);

        public static async Task<Host> CreateAsync()
        {
            var environment = await SabnzbdTestSupport.CreateEnvironmentAsync();
            var clients = environment.NewDownloadClientStore();
            foreach (var existing in await clients.LoadAllAsync())
            {
                await clients.DeleteAsync(existing.Id);
            }

            var clientA = Entry("sab-a-name", UrlA, enabled: true, priority: 1);
            var clientB = Entry("sab-b-name", UrlB, enabled: true, priority: 2);
            var disabled = Entry("sab-off-name", "http://sab-off:8080", enabled: false, priority: 3);
            await clients.SaveAsync(clientA);
            await clients.SaveAsync(clientB);
            await clients.SaveAsync(disabled);

            var sab = new RoutingSabnzbdClient();
            var provider = new ServiceCollection()
                .AddSingleton(environment.Db)
                .AddSingleton<ISabnzbdClient>(sab)
                .AddSingleton(_ => new AcquisitionAccessStore(environment.Db))
                .BuildServiceProvider();
            var logs = new RecordingLogger<SabnzbdOperationMonitorService>();
            var monitor = new SabnzbdOperationMonitorService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                environment.NewDownloadClientStore(),
                logs);

            return new Host(environment, provider, monitor, sab, logs, clientA, clientB, disabled);
        }

        public Task<bool> PollAsync(DateTime nowUtc) =>
            monitor.PollOnceAsync(services, nowUtc, CancellationToken.None);

        public async Task<Guid> CreateDownloadAsync(Guid clientId, string nzoId)
        {
            var store = Operations;
            var id = await store.CreateAsync(
                new OperationDescriptor(
                    "reading-usenet-download",
                    DownloadClientSubmissionService.OperationCategory,
                    "Download Manga",
                    "Frieren",
                    "owner",
                    IsDownload: true));
            await store.MarkRunningAsync(id);
            await store.SetExternalReferenceAsync(id, SabnzbdClient.ProviderId, nzoId);
            await store.SetDetailsAsync(
                id,
                new DownloadOperationDetails(clientId, MediaAcquisitionKind.Manga, "manga").Serialize());
            return id;
        }

        private static DownloadClientEntry Entry(string name, string url, bool enabled, int priority) =>
            new(
                Guid.NewGuid(),
                name,
                DownloadClientType.Sabnzbd,
                enabled,
                priority,
                DownloadClientSettings.CreateDefault(url),
                "secret-key");

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            await Environment.DisposeAsync();
        }
    }

    private sealed class RoutingSabnzbdClient : ISabnzbdClient
    {
        private readonly Dictionary<string, List<SabnzbdHistoryJob>> history = new(StringComparer.Ordinal);

        public HashSet<string> Down { get; } = new(StringComparer.Ordinal);
        public List<string> Queried { get; } = [];

        public void Completed(string url, string nzoId) => Add(url, nzoId, "Completed", SabnzbdFailureKind.None);

        public void Failed(string url, string nzoId) => Add(url, nzoId, "Failed", SabnzbdFailureKind.Unknown);

        private void Add(string url, string nzoId, string status, SabnzbdFailureKind failure)
        {
            if (!history.TryGetValue(url, out var jobs))
            {
                history[url] = jobs = [];
            }

            jobs.Add(new SabnzbdHistoryJob(
                nzoId,
                "Frieren",
                status,
                "manga",
                $"/downloads/complete/manga/{nzoId}",
                failure == SabnzbdFailureKind.None ? null : "Repair failed",
                failure,
                DateTimeOffset.UtcNow));
        }

        public Task<SabnzbdQueueSnapshot> GetQueueAsync(SabnzbdConnection connection, CancellationToken cancellationToken)
        {
            var url = connection.Settings.BaseUrl.TrimEnd('/');
            Queried.Add(url);
            if (Down.Contains(url))
            {
                throw new HttpRequestException("Connection refused");
            }

            return Task.FromResult(new SabnzbdQueueSnapshot(false, null, null, []));
        }

        public Task<SabnzbdHistorySnapshot> GetHistoryAsync(
            SabnzbdConnection connection,
            IReadOnlyCollection<string>? nzoIds,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SabnzbdHistorySnapshot(
                history.TryGetValue(connection.Settings.BaseUrl.TrimEnd('/'), out var jobs) ? jobs : []));

        public Task<SabnzbdConnectionTestResult> TestAsync(SabnzbdConnection connection, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SabnzbdGrabResult> GrabAsync(SabnzbdConnection connection, SabnzbdGrabRequest grab, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SabnzbdGrabResult> AddFileAsync(SabnzbdConnection connection, Stream nzb, string fileName, string? category, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SabnzbdActionResult> CancelAsync(SabnzbdConnection connection, string nzoId, bool deleteFiles, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SabnzbdActionResult> DeleteHistoryAsync(SabnzbdConnection connection, string nzoId, bool deleteFiles, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SabnzbdActionResult> RetryAsync(SabnzbdConnection connection, string nzoId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SabnzbdActionResult> PauseAsync(SabnzbdConnection connection, string nzoId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SabnzbdActionResult> ResumeAsync(SabnzbdConnection connection, string nzoId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
