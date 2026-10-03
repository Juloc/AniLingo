using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.DownloadClients;
using Jularr.Web.Features.Acquisition.Import;
using Jularr.Web.Features.Acquisition.Wanted;
using Jularr.Web.Features.Operations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jularr.Tests;

[TestClass]
public sealed class WantedAcquisitionTests
{
    [TestMethod]
    public async Task ImportingStatusIsOpenAndPersistsThroughTheRequestStore()
    {
        await using var fixture = await Fixture.CreateAsync();
        var store = new AcquisitionAccessStore(fixture.Db);
        var request = await store.CreateAsync(
            Draft(),
            "owner",
            AcquisitionRequestStatus.Importing,
            "owner",
            CancellationToken.None);

        Assert.IsTrue(request.IsOpen);
        Assert.AreEqual(
            "importing",
            AcquisitionAccessNames.Status(request.Status));
        Assert.AreEqual(
            AcquisitionRequestStatus.Importing,
            AcquisitionAccessNames.ParseStatus("importing"));

        var open = await store.ListAsync(
            MediaAcquisitionKind.Manga,
            requestedByProfileId: null,
            openOnly: true,
            limit: 20,
            CancellationToken.None);
        Assert.IsTrue(open.Any(item => item.Id == request.Id));
    }

    [TestMethod]
    public async Task SucceededDownloadUsesGenericDispatcherAndCompletesRequest()
    {
        await using var fixture = await Fixture.CreateAsync();
        var setup = await fixture.CreateInFlightAsync(
            AcquisitionRequestStatus.Downloading);
        var handler = new RecordingWantedHandler();
        var adapter = new RecordingImportAdapter(
            CompletedDownloadImportResult.Completed(
                "Imported through canonical dispatcher.",
                "/Manga/Series/123"));

        var services = fixture.Services(
            handler,
            adapter,
            new FixedLocationResolver("/mapped/manga"));

        var advanced = await WantedAcquisitionService.ProcessOnceAsync(
            services,
            DateTime.UtcNow,
            CancellationToken.None);

        var stored = await setup.Store.GetAsync(
            setup.Request.Id,
            CancellationToken.None);
        Assert.IsTrue(advanced > 0);
        Assert.AreEqual(
            AcquisitionRequestStatus.Completed,
            stored!.Status);
        Assert.AreEqual(
            "/Manga/Series/123",
            stored.ResultUrl);
        Assert.AreEqual(1, adapter.Imports);
        Assert.AreEqual("/mapped/manga", adapter.LastSourcePath);
        Assert.AreEqual(0, handler.Problems);
    }

    [TestMethod]
    public async Task TemporaryImportFailureStaysImportingAndDoesNotGrabAnotherRelease()
    {
        await using var fixture = await Fixture.CreateAsync();
        var setup = await fixture.CreateInFlightAsync(
            AcquisitionRequestStatus.Downloading);
        var handler = new RecordingWantedHandler();
        var adapter = new RecordingImportAdapter(
            CompletedDownloadImportResult.RetryLater(
                "NAS is temporarily unavailable."));

        var services = fixture.Services(
            handler,
            adapter,
            new FixedLocationResolver("/mapped/manga"));

        await WantedAcquisitionService.ProcessOnceAsync(
            services,
            DateTime.UtcNow,
            CancellationToken.None);

        var stored = await setup.Store.GetAsync(
            setup.Request.Id,
            CancellationToken.None);
        Assert.AreEqual(
            AcquisitionRequestStatus.Importing,
            stored!.Status);
        Assert.AreEqual(
            "NAS is temporarily unavailable.",
            stored.StatusMessage);
        Assert.AreEqual(0, handler.Problems);
    }

    [TestMethod]
    [DataRow(CompletedDownloadImportDisposition.NeedsReview)]
    [DataRow(CompletedDownloadImportDisposition.Failed)]
    public async Task AnImportTheImporterEndedFailsTheRequestWithoutAnotherRelease(CompletedDownloadImportDisposition disposition)
    {
        await using var fixture = await Fixture.CreateAsync();
        var setup = await fixture.CreateInFlightAsync(
            AcquisitionRequestStatus.Downloading);
        var handler = new RecordingWantedHandler();
        var adapter = new RecordingImportAdapter(
            disposition == CompletedDownloadImportDisposition.NeedsReview
                ? CompletedDownloadImportResult.NeedsReview("One file needs a decision.")
                : CompletedDownloadImportResult.Failed("The library folder is not writable."));

        var services = fixture.Services(
            handler,
            adapter,
            new FixedLocationResolver("/mapped/manga"));

        await WantedAcquisitionService.ProcessOnceAsync(
            services,
            DateTime.UtcNow,
            CancellationToken.None);

        var stored = await setup.Store.GetAsync(
            setup.Request.Id,
            CancellationToken.None);
        Assert.AreEqual(AcquisitionRequestStatus.Failed, stored!.Status);
        StringAssert.Contains(stored.StatusMessage, disposition == CompletedDownloadImportDisposition.NeedsReview ? "needs a decision" : "not writable");
        Assert.AreEqual(0, handler.Problems, "The release is not at fault, so no other release is tried.");
        Assert.AreEqual(1, adapter.Imports);
    }

    [TestMethod]
    [DataRow(CompletedDownloadImportDisposition.NeedsReview, DownloadImportState.ManualReview)]
    [DataRow(CompletedDownloadImportDisposition.Failed, DownloadImportState.Failed)]
    [DataRow(CompletedDownloadImportDisposition.RejectedRelease, DownloadImportState.Rejected)]
    public async Task ManualDownloadWhoseImportEndedIsRecordedOnceAndNeverImportedAgain(
        CompletedDownloadImportDisposition disposition,
        DownloadImportState expectedState)
    {
        await using var fixture = await Fixture.CreateAsync();
        var operations = new OperationStore(fixture.Db);
        var operationId = await operations.CreateAsync(
            new OperationDescriptor(
                CompletedDownloadImportService.ManualDownloadOperationKind,
                "External downloads",
                "SABnzbd download",
                IsDownload: true,
                ExternalProvider: "sabnzbd",
                ExternalId: "job-manual",
                Details: new DownloadOperationDetails(Guid.NewGuid(), MediaAcquisitionKind.Manga, "manga").Serialize()),
            CancellationToken.None);
        await operations.MarkRunningAsync(operationId, CancellationToken.None);
        await operations.MarkSucceededAsync(operationId, "Downloaded.", CancellationToken.None);
        var adapter = new RecordingImportAdapter(
            new CompletedDownloadImportResult(disposition, "The importer ended it."));
        var services = fixture.Services(new RecordingWantedHandler(), adapter, new FixedLocationResolver("/mapped/manga"));

        await WantedAcquisitionService.ProcessOnceAsync(services, DateTime.UtcNow, CancellationToken.None);
        await WantedAcquisitionService.ProcessOnceAsync(services, DateTime.UtcNow.AddMinutes(2), CancellationToken.None);

        Assert.AreEqual(1, adapter.Imports, "An ended import is not repeated on the next pass.");
        Assert.IsTrue(DownloadOperationDetails.TryParse((await operations.GetAsync(operationId))!.Details, out var details));
        Assert.AreEqual(expectedState, details!.Import!.State);
    }

    [TestMethod]
    public async Task SharedWantedPassRunsMonitoredHandlerWithoutARequest()
    {
        await using var fixture = await Fixture.CreateAsync();
        var monitored = new RecordingMonitoredWantedHandler();
        var services = fixture.Services(
            new RecordingWantedHandler(),
            new RecordingImportAdapter(CompletedDownloadImportResult.Completed("unused")),
            new FixedLocationResolver("/unused"),
            monitored);

        var advanced = await WantedAcquisitionService.ProcessOnceAsync(
            services,
            DateTime.UtcNow,
            CancellationToken.None);

        Assert.AreEqual(1, advanced);
        Assert.AreEqual(1, monitored.Passes);
    }

    [TestMethod]
    public async Task UnsuitableCompletedReleaseReturnsToWantedHandler()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.CreateInFlightAsync(
            AcquisitionRequestStatus.Importing);
        var handler = new RecordingWantedHandler();
        var adapter = new RecordingImportAdapter(
            CompletedDownloadImportResult.RejectRelease(
                "Package contained no usable CBZ."));
        var services = fixture.Services(
            handler,
            adapter,
            new FixedLocationResolver("/mapped/manga"));

        await WantedAcquisitionService.ProcessOnceAsync(
            services,
            DateTime.UtcNow,
            CancellationToken.None);

        Assert.AreEqual(1, handler.Problems);
        Assert.AreEqual(
            "Package contained no usable CBZ.",
            handler.LastProblem);
    }

    [TestMethod]
    public async Task CompletedPathResolverUsesOriginatingClientAndCanonicalRemoteMapping()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"jularr-wanted-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var protection = new EphemeralDataProtectionProvider();
            var clients = new DownloadClientStore(
                protection,
                new DirectoryInfo(Path.Combine(directory, "acquisition")));
            var entry = new DownloadClientEntry(
                Guid.NewGuid(),
                "SAB",
                DownloadClientType.Sabnzbd,
                Enabled: true,
                Priority: 0,
                DownloadClientSettings.CreateDefault("http://sab.invalid"),
                "secret");
            await clients.SaveAsync(entry);

            var settings = new AnimeImportSettingsStore(directory);
            await settings.UpdateAsync(
                current => current.WithRemotePathMappings(
                    MediaAcquisitionKind.Manga,
                    [
                        new RemotePathMapping(
                            "/remote/complete",
                            "/mnt/downloads")
                    ]));

            var client = new RecordingDownloadClient(
                new DownloadClientJobStatus(
                    "job-1",
                    "Manga",
                    DownloadClientJobState.Completed,
                    100,
                    null,
                    1_000,
                    0,
                    null,
                    "/remote/complete/manga/title",
                    null));
            var resolver = new CompletedDownloadLocationResolver(
                clients,
                client,
                settings,
                NullLogger<CompletedDownloadLocationResolver>.Instance);
            var operation = Operation(
                Guid.NewGuid(),
                "job-1",
                new DownloadOperationDetails(
                    entry.Id,
                    MediaAcquisitionKind.Manga,
                    "manga").Serialize());

            var result = await resolver.ResolveAsync(
                operation,
                MediaAcquisitionKind.Manga,
                CancellationToken.None);

            Assert.IsTrue(result.Resolved);
            Assert.AreEqual(
                "/mnt/downloads/manga/title",
                result.SourcePath);
            Assert.AreEqual(entry.Id, client.LastEntryId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task CompletedPathResolverAppliesOnlyTheMediaTypesOwnRemoteMappings()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"jularr-wanted-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var clients = new DownloadClientStore(
                new EphemeralDataProtectionProvider(),
                new DirectoryInfo(Path.Combine(directory, "acquisition")));
            var entry = new DownloadClientEntry(
                Guid.NewGuid(),
                "SAB",
                DownloadClientType.Sabnzbd,
                Enabled: true,
                Priority: 0,
                DownloadClientSettings.CreateDefault("http://sab.invalid"),
                "secret");
            await clients.SaveAsync(entry);

            // The same reported folder is mounted differently for each media type.
            var settings = new AnimeImportSettingsStore(directory);
            await settings.UpdateAsync(
                current => current
                    .WithRemotePathMappings(MediaAcquisitionKind.Manga, [new RemotePathMapping("/remote/complete", "/mnt/manga")])
                    .WithRemotePathMappings(MediaAcquisitionKind.Book, [new RemotePathMapping("/remote/complete", "/mnt/books")]));

            var resolver = new CompletedDownloadLocationResolver(
                clients,
                new RecordingDownloadClient(
                    new DownloadClientJobStatus(
                        "job-1",
                        "Release",
                        DownloadClientJobState.Completed,
                        100,
                        null,
                        1_000,
                        0,
                        null,
                        "/remote/complete/release",
                        null)),
                settings,
                NullLogger<CompletedDownloadLocationResolver>.Instance);
            var operation = Operation(
                Guid.NewGuid(),
                "job-1",
                new DownloadOperationDetails(entry.Id, MediaAcquisitionKind.Manga, "manga").Serialize());

            var manga = await resolver.ResolveAsync(operation, MediaAcquisitionKind.Manga, CancellationToken.None);
            var book = await resolver.ResolveAsync(operation, MediaAcquisitionKind.Book, CancellationToken.None);
            var anime = await resolver.ResolveAsync(operation, MediaAcquisitionKind.Anime, CancellationToken.None);

            Assert.AreEqual("/mnt/manga/release", manga.SourcePath);
            Assert.AreEqual("/mnt/books/release", book.SourcePath);
            Assert.AreEqual("/remote/complete/release", anime.SourcePath, "Anime has no mapping for this folder.");
            Assert.AreEqual("/remote/complete/release", manga.ReportedPath, "The reported path is kept next to the mapped one.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task DispatcherDoesNotFallThroughToAnotherMediaImporter()
    {
        var manga = new RecordingImportAdapter(
            CompletedDownloadImportResult.Completed("ok", "/manga"));
        var novels = new RecordingImportAdapter(
            CompletedDownloadImportResult.Completed("wrong", "/novel"),
            MediaAcquisitionKind.LightNovel);
        var dispatcher = new CompletedDownloadDispatcher(
            [manga, novels]);

        var result = await dispatcher.DispatchAsync(
            new CompletedDownloadImportRequest(
                Request(),
                Operation(Guid.NewGuid(), "job", null),
                "/source"),
            CancellationToken.None);

        Assert.AreEqual(
            CompletedDownloadImportDisposition.Completed,
            result.Disposition);
        Assert.AreEqual(1, manga.Imports);
        Assert.AreEqual(0, novels.Imports);
    }

    private static AcquisitionRequestDraft Draft() =>
        new(
            MediaAcquisitionKind.Manga,
            "anilist",
            "1",
            "Manga",
            null,
            null);

    private static AcquisitionRequest Request() =>
        new(
            Guid.NewGuid(),
            MediaAcquisitionKind.Manga,
            "anilist",
            "1",
            "Manga",
            null,
            null,
            null,
            "owner",
            AcquisitionRequestStatus.Importing,
            null,
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            "owner",
            DateTime.UtcNow);

    private static OperationSnapshot Operation(
        Guid id,
        string externalId,
        string? details) =>
        new(
            id,
            "test-download",
            "External downloads",
            OperationLane.Normal,
            OperationStatus.Succeeded,
            "owner",
            "Download",
            "Manga",
            100,
            "Done",
            null,
            true,
            1_000,
            1_000,
            null,
            null,
            1,
            true,
            "sabnzbd",
            externalId,
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow,
            DateTime.UtcNow,
            details);

    private sealed class RecordingWantedHandler : IWantedRequestHandler
    {
        public MediaAcquisitionKind Kind =>
            MediaAcquisitionKind.Manga;

        public int Problems { get; private set; }
        public string? LastProblem { get; private set; }

        public bool IsSearchDue(
            AcquisitionRequest request,
            DateTime nowUtc) =>
            false;

        public Task ContinueAfterProblemAsync(
            AcquisitionRequest request,
            string problem,
            CancellationToken cancellationToken)
        {
            Problems++;
            LastProblem = problem;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingMonitoredWantedHandler : IMonitoredWantedHandler
    {
        public MediaAcquisitionKind Kind => MediaAcquisitionKind.Book;

        public int Passes { get; private set; }

        public Task<int> ProcessAsync(
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            Passes++;
            return Task.FromResult(1);
        }
    }

    private sealed class RecordingImportAdapter(
        CompletedDownloadImportResult result,
        MediaAcquisitionKind kind = MediaAcquisitionKind.Manga)
        : ICompletedDownloadImportAdapter
    {
        public MediaAcquisitionKind Kind => kind;
        public int Imports { get; private set; }
        public string? LastSourcePath { get; private set; }

        public Task<CompletedDownloadImportResult> ImportAsync(
            CompletedDownloadImportRequest request,
            CancellationToken cancellationToken)
        {
            Imports++;
            LastSourcePath = request.SourcePath;
            return Task.FromResult(result);
        }
    }

    private sealed class FixedLocationResolver(string sourcePath)
        : ICompletedDownloadLocationResolver
    {
        public Task<CompletedDownloadLocation> ResolveAsync(
            OperationSnapshot operation,
            MediaAcquisitionKind kind,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new CompletedDownloadLocation(
                    true,
                    sourcePath,
                    "Resolved."));
    }

    private sealed class RecordingDownloadClient(
        DownloadClientJobStatus status) : IDownloadClient
    {
        public string ProviderId => "sabnzbd";
        public Guid? LastEntryId { get; private set; }

        public Task<DownloadClientTestResult> TestAsync(
            DownloadClientEntry entry,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new DownloadClientTestResult(true));

        public Task<DownloadClientSubmitResult> SubmitAsync(
            DownloadClientEntry entry,
            DownloadClientSubmitRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DownloadClientJobStatus>> GetStatusAsync(
            DownloadClientEntry entry,
            IReadOnlyCollection<string> externalIds,
            CancellationToken cancellationToken)
        {
            LastEntryId = entry.Id;
            return Task.FromResult<IReadOnlyList<DownloadClientJobStatus>>(
                [status]);
        }

        public Task<bool> DeleteAsync(
            DownloadClientEntry entry,
            string externalId,
            bool deleteFiles,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string directory;

        private Fixture(
            string directory,
            AppDbContext db)
        {
            this.directory = directory;
            Db = db;
        }

        public AppDbContext Db { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"jularr-wanted-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite(
                        $"Data Source={Path.Combine(directory, "app.db")};Foreign Keys=True")
                    .Options);
            await DatabaseMigrationBridge.UpgradeAsync(db);
            return new Fixture(directory, db);
        }

        public async Task<(
            AcquisitionAccessStore Store,
            AcquisitionRequest Request,
            Guid OperationId)> CreateInFlightAsync(
            AcquisitionRequestStatus status)
        {
            var store = new AcquisitionAccessStore(Db);
            var request = await store.CreateAsync(
                Draft(),
                "owner",
                status,
                "owner",
                CancellationToken.None);

            var operations = new OperationStore(Db);
            var operationId = await operations.CreateAsync(
                new OperationDescriptor(
                    "test-download",
                    "External downloads",
                    "Download",
                    "Manga",
                    "owner",
                    IsDownload: true),
                CancellationToken.None);
            await operations.MarkRunningAsync(
                operationId,
                CancellationToken.None);
            await operations.MarkSucceededAsync(
                operationId,
                "Downloaded.",
                CancellationToken.None);
            await store.UpdateStatusAsync(
                request.Id,
                status,
                null,
                operationId,
                null,
                null,
                CancellationToken.None);

            return (
                store,
                (await store.GetAsync(
                    request.Id,
                    CancellationToken.None))!,
                operationId);
        }

        public IServiceProvider Services(
            IWantedRequestHandler handler,
            ICompletedDownloadImportAdapter adapter,
            ICompletedDownloadLocationResolver resolver,
            IMonitoredWantedHandler? monitored = null)
        {
            var services = new ServiceCollection()
                .AddSingleton(Db)
                .AddSingleton(new AcquisitionAccessStore(Db))
                .AddSingleton<IWantedRequestHandler>(handler)
                .AddSingleton<ICompletedDownloadImportAdapter>(adapter)
                .AddSingleton<ICompletedDownloadLocationResolver>(resolver)
                .AddSingleton<CompletedDownloadDispatcher>()
                .AddSingleton<CompletedDownloadImportService>()
                .AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));

            if (monitored is not null)
            {
                services.AddSingleton<IMonitoredWantedHandler>(monitored);
            }

            return services.BuildServiceProvider();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}
