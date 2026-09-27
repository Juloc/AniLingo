using Jularr.Web.Features.Acquisition;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.DownloadClients;
using Jularr.Web.Features.Acquisition.Health;
using Jularr.Web.Features.Acquisition.Import;
using Jularr.Web.Features.Acquisition.Indexers;
using Jularr.Web.Features.Acquisition.Prowlarr;
using Jularr.Web.Features.Acquisition.Sabnzbd;
using Jularr.Web.Features.Acquisition.Wanted;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Operations;
using Jularr.Web.Features.ReadingAcquisition;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jularr.Tests;

/// <summary>
/// End-to-end Manga Wanted lifecycle: the real request service, reading engine and Wanted
/// handler against a fake indexer and a fake SABnzbd (#457, review #485 items 2, 11, 12, 14).
/// </summary>
[TestClass]
public sealed class ReadingWantedLifecycleTests
{
    private const string First = "Frieren Vol 01 Digital CBZ";
    private const string Second = "Frieren Vol 01 CBZ";

    [TestMethod]
    public async Task FailedDownloadContinuesWithTheNextUntriedRelease()
    {
        await using var host = await Host.CreateAsync(First, Second);
        var request = await host.StartAsync();
        var firstOperation = request.OperationId!.Value;

        await host.Operations.MarkFailedAsync(firstOperation, "Out of retention");
        await host.ProcessAsync(DateTime.UtcNow);

        var stored = await host.GetAsync(request.Id);
        Assert.AreEqual(AcquisitionRequestStatus.Downloading, stored.Status);
        Assert.AreNotEqual(firstOperation, stored.OperationId);
        Assert.AreEqual(2, host.Environment.Client.Grabs.Count);
        StringAssert.StartsWith(stored.StatusMessage, "The download failed: Out of retention.");
        StringAssert.Contains(stored.StatusMessage, "Trying ");
    }

    [TestMethod]
    public async Task CancelledDownloadFailsTheRequestWithoutGrabbingAnotherRelease()
    {
        await using var host = await Host.CreateAsync(First, Second);
        var request = await host.StartAsync();

        await host.Operations.MarkCancelledAsync(request.OperationId!.Value, "Cancelled in SABnzbd by the owner.");
        await host.ProcessAsync(DateTime.UtcNow);
        await host.ProcessAsync(DateTime.UtcNow.AddDays(2));

        var stored = await host.GetAsync(request.Id);
        Assert.AreEqual(AcquisitionRequestStatus.Failed, stored.Status);
        StringAssert.Contains(stored.StatusMessage, "cancelled");
        Assert.AreEqual(1, host.Environment.Client.Grabs.Count);
        Assert.AreEqual(request.OperationId, stored.OperationId);
    }

    [TestMethod]
    public async Task CompletedDownloadImportsThroughTheRemotePathMapping()
    {
        await using var host = await Host.CreateAsync(First);
        await host.ImportSettings.UpdateAsync(current => current with
        {
            RemotePathMappings = [new RemotePathMapping("/downloads", "/data/sab")]
        });
        var request = await host.StartAsync();
        var operation = (await host.Operations.GetAsync(request.OperationId!.Value))!;
        host.CompleteInSabnzbd(operation.ExternalId!, "/downloads/manga/Frieren Vol 01");
        await host.Operations.MarkSucceededAsync(operation.Id, "Downloaded.");

        await host.ProcessAsync(DateTime.UtcNow);

        var stored = await host.GetAsync(request.Id);
        Assert.AreEqual(AcquisitionRequestStatus.Completed, stored.Status);
        Assert.AreEqual("/Manga/Series/1", stored.ResultUrl);
        Assert.AreEqual(1, host.Importer.Imports);
        Assert.AreEqual("/data/sab/manga/Frieren Vol 01", host.Importer.LastSourcePath);
    }

    [TestMethod]
    public async Task MissingHistoryPathWaitsAsImportingAndTimesOutWithoutAnotherGrab()
    {
        await using var host = await Host.CreateAsync(First, Second);
        var request = await host.StartAsync();
        await host.Operations.MarkSucceededAsync(request.OperationId!.Value, "Downloaded.");

        // SABnzbd no longer reports the job (history purged), so there is no path to import.
        await host.ProcessAsync(DateTime.UtcNow);
        var waiting = await host.GetAsync(request.Id);
        Assert.AreEqual(AcquisitionRequestStatus.Importing, waiting.Status);

        await host.ProcessAsync(DateTime.UtcNow + WantedAcquisitionService.CompletedImportTimeout + TimeSpan.FromMinutes(1));

        var stored = await host.GetAsync(request.Id);
        Assert.AreEqual(AcquisitionRequestStatus.Failed, stored.Status);
        StringAssert.Contains(stored.StatusMessage, "Gave up importing");
        Assert.AreEqual(0, host.Importer.Imports);
        Assert.AreEqual(1, host.Environment.Client.Grabs.Count);
    }

    [TestMethod]
    public async Task RepeatedSearchesWithoutAReleaseDoNotRepeatTheProblem()
    {
        await using var host = await Host.CreateAsync();
        var request = await host.StartAsync();
        Assert.AreEqual(AcquisitionRequestStatus.Approved, request.Status);

        await host.ProcessAsync(DateTime.UtcNow.AddDays(1));
        await host.ProcessAsync(DateTime.UtcNow.AddDays(3));

        var stored = await host.GetAsync(request.Id);
        Assert.AreEqual(AcquisitionRequestStatus.Approved, stored.Status);
        Assert.AreEqual(
            1,
            CountOccurrences(stored.StatusMessage!, "No release found"),
            stored.StatusMessage);
    }

    [TestMethod]
    public async Task ProblemFromAFailedDownloadIsShownOnceThenConsumed()
    {
        await using var host = await Host.CreateAsync(First);
        var request = await host.StartAsync();

        await host.Operations.MarkFailedAsync(request.OperationId!.Value, "Out of retention");
        await host.ProcessAsync(DateTime.UtcNow);
        var afterFailure = await host.GetAsync(request.Id);
        Assert.AreEqual(AcquisitionRequestStatus.Approved, afterFailure.Status);
        StringAssert.StartsWith(afterFailure.StatusMessage, "The download failed: Out of retention.");

        await host.ProcessAsync(DateTime.UtcNow.AddDays(1));
        var later = await host.GetAsync(request.Id);
        Assert.IsFalse(
            later.StatusMessage!.Contains("Out of retention", StringComparison.Ordinal),
            later.StatusMessage);
        Assert.AreEqual(1, CountOccurrences(later.StatusMessage, "Every matching release was tried already."));
    }

    [TestMethod]
    public async Task RetryingASupersededDownloadIsRefused()
    {
        await using var host = await Host.CreateAsync(First, Second);
        var request = await host.StartAsync();
        var firstOperation = request.OperationId!.Value;
        await host.Operations.MarkFailedAsync(firstOperation, "Out of retention");
        await host.ProcessAsync(DateTime.UtcNow);

        var outcome = await host.Downloads.RetryAsync(firstOperation, CancellationToken.None);

        Assert.IsFalse(outcome.Success);
        StringAssert.Contains(outcome.Message, "newer release");
        Assert.AreEqual(0, host.Environment.Client.Retried.Count);
    }

    [TestMethod]
    public async Task RetryingTheLastDownloadOfAWaitingRequestResumesTracking()
    {
        await using var host = await Host.CreateAsync(First);
        var request = await host.StartAsync();
        var operation = request.OperationId!.Value;
        await host.Operations.MarkFailedAsync(operation, "Out of retention");
        await host.ProcessAsync(DateTime.UtcNow);
        Assert.AreEqual(AcquisitionRequestStatus.Approved, (await host.GetAsync(request.Id)).Status);

        var outcome = await host.Downloads.RetryAsync(operation, CancellationToken.None);

        Assert.IsTrue(outcome.Success, outcome.Message);
        var stored = await host.GetAsync(request.Id);
        Assert.AreEqual(AcquisitionRequestStatus.Downloading, stored.Status);
        Assert.AreEqual(operation, stored.OperationId);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static ProwlarrReleaseCandidate Candidate(string title) =>
        new(
            title,
            "Test indexer",
            1,
            "usenet",
            100L * 1024 * 1024,
            null,
            null,
            DateTimeOffset.UtcNow,
            0,
            1,
            title,
            null,
            AnimeReleaseParser.Parse(title),
            [],
            new Uri($"https://indexer.invalid/download/{Uri.EscapeDataString(title)}"),
            null);

    private sealed class Host : IAsyncDisposable
    {
        private readonly ServiceProvider services;

        private Host(SabnzbdTestEnvironment environment, ServiceProvider services, RecordingImporter importer)
        {
            Environment = environment;
            this.services = services;
            Importer = importer;
            ImportSettings = services.GetRequiredService<AnimeImportSettingsStore>();
        }

        public SabnzbdTestEnvironment Environment { get; }
        public RecordingImporter Importer { get; }
        public AnimeImportSettingsStore ImportSettings { get; }
        public OperationStore Operations => new(Environment.Db);
        public AcquisitionAccessStore Requests => new(Environment.Db);
        public SabnzbdDownloadService Downloads => Environment.NewDownloadService(Environment.NewAcquisitionStore());

        public static async Task<Host> CreateAsync(params string[] releases)
        {
            var environment = await SabnzbdTestSupport.CreateEnvironmentAsync();
            var protection = environment.Protection;
            var directory = environment.Directory;

            var indexers = new IndexerStore(protection, directory);
            await indexers.SaveAsync(new IndexerEntry(
                Guid.NewGuid(),
                "Test indexer",
                IndexerType.Newznab,
                Enabled: true,
                Priority: 1,
                new IndexerSettings("https://indexer.invalid", [7030], [], 100),
                "indexer-key"));
            var candidates = releases.Select(Candidate).ToArray();
            var coordinator = new IndexerSearchCoordinator(
                new Dictionary<IndexerType, IIndexer> { [IndexerType.Newznab] = new FixedIndexer(candidates) },
                indexers,
                new AcquisitionHealthStore(directory),
                NullLogger<IndexerSearchCoordinator>.Instance);

            var importer = new RecordingImporter();
            var provider = new ServiceCollection()
                .AddSingleton(environment.Db)
                .AddSingleton(TimeProvider.System)
                .AddSingleton(coordinator)
                .AddSingleton(_ => environment.NewDownloadClientStore())
                .AddSingleton(_ => environment.NewSubmissionService())
                .AddSingleton<IDownloadClient>(new SabnzbdDownloadClient(environment.Client))
                .AddSingleton(new AnimeImportSettingsStore(directory.FullName))
                .AddSingleton(_ => new AcquisitionAccessStore(environment.Db))
                .AddSingleton(new CurrentAccountContext(new HttpContextAccessor()))
                .AddSingleton<ReleaseRequestTracker>()
                .AddSingleton<ReadingAcquisitionEngine>()
                .AddSingleton<IAcquisitionRequestExecutor, MangaAcquisitionRequestExecutor>()
                .AddSingleton<AcquisitionRequestService>()
                .AddSingleton<IWantedRequestHandler, MangaWantedRequestHandler>()
                .AddSingleton<ICompletedDownloadImportAdapter>(importer)
                .AddSingleton<CompletedDownloadDispatcher>()
                .AddSingleton<CompletedDownloadImportService>()
                .AddSingleton<ICompletedDownloadLocationResolver, CompletedDownloadLocationResolver>()
                .AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>))
                .BuildServiceProvider();

            return new Host(environment, provider, importer);
        }

        /// <summary>Creates an approved Manga request and lets Wanted run its first search.</summary>
        public async Task<AcquisitionRequest> StartAsync()
        {
            var created = await Requests.CreateAsync(
                new AcquisitionRequestDraft(
                    MediaAcquisitionKind.Manga,
                    "anilist",
                    "154587",
                    "Frieren",
                    null,
                    null),
                "owner",
                AcquisitionRequestStatus.Approved,
                "owner",
                CancellationToken.None);

            await ProcessAsync(DateTime.UtcNow);
            return await GetAsync(created.Id);
        }

        public Task<int> ProcessAsync(DateTime nowUtc) =>
            WantedAcquisitionService.ProcessOnceAsync(services, nowUtc, CancellationToken.None);

        public async Task<AcquisitionRequest> GetAsync(Guid id) =>
            (await Requests.GetAsync(id, CancellationToken.None))!;

        public void CompleteInSabnzbd(string nzoId, string storagePath) =>
            Environment.Client.History = new SabnzbdHistorySnapshot(
            [
                new SabnzbdHistoryJob(
                    nzoId,
                    Path.GetFileName(storagePath),
                    "Completed",
                    "manga",
                    storagePath,
                    null,
                    SabnzbdFailureKind.None,
                    DateTimeOffset.UtcNow)
            ]);

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            await Environment.DisposeAsync();
        }
    }

    private sealed class FixedIndexer(IReadOnlyList<ProwlarrReleaseCandidate> releases) : IIndexer
    {
        public IndexerType Type => IndexerType.Newznab;

        public Task<IndexerConnectionTestResult> TestAsync(IndexerEntry entry, CancellationToken cancellationToken) =>
            Task.FromResult(new IndexerConnectionTestResult(true));

        public Task<IReadOnlyList<ProwlarrReleaseCandidate>> SearchAsync(
            IndexerEntry entry,
            IndexerSearchQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult(releases);
    }

    private sealed class RecordingImporter : ICompletedDownloadImportAdapter
    {
        public MediaAcquisitionKind Kind => MediaAcquisitionKind.Manga;
        public int Imports { get; private set; }
        public string? LastSourcePath { get; private set; }

        public Task<CompletedDownloadImportResult> ImportAsync(
            CompletedDownloadImportRequest request,
            CancellationToken cancellationToken)
        {
            Imports++;
            LastSourcePath = request.SourcePath;
            return Task.FromResult(CompletedDownloadImportResult.Completed("Imported 1 Manga chapter(s).", "/Manga/Series/1"));
        }
    }
}
