using AniLingo.Web.Data;
using AniLingo.Web.Features.Acquisition;
using AniLingo.Web.Features.Acquisition.Api;
using AniLingo.Web.Features.Acquisition.AniListAutoMonitor;
using AniLingo.Web.Features.Acquisition.Backup;
using AniLingo.Web.Features.Acquisition.DownloadClients;
using AniLingo.Web.Features.Acquisition.Health;
using AniLingo.Web.Features.Acquisition.History;
using AniLingo.Web.Features.Acquisition.Import;
using AniLingo.Web.Features.Acquisition.Indexers;
using AniLingo.Web.Features.Acquisition.Monitoring;
using AniLingo.Web.Features.Acquisition.Naming;
using AniLingo.Web.Features.Acquisition.Ownership;
using AniLingo.Web.Features.Acquisition.Pipeline;
using AniLingo.Web.Features.Acquisition.Policy;
using AniLingo.Web.Features.Acquisition.Prowlarr;
using AniLingo.Web.Features.Acquisition.Quality;
using AniLingo.Web.Features.Acquisition.Sabnzbd;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.MediaMapping;
using AniLingo.Web.Features.Metadata;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Features.Sonarr;
using AniLingo.Web.Features.Subtitles;
using AniLingo.Web.Features.Tracking;
using AniLingo.Web.Features.Vocabulary;
using AniLingo.Web.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniLingo.Tests;

/// <summary>
/// The anime acquisition pipeline wired like Program.cs, on an isolated data directory, library
/// root and database, with fakes for Prowlarr and SABnzbd. Sonarr is not configured, so Sonarr
/// ownership comes from the persisted ownership state. <see cref="RestartAsync"/> builds new
/// service instances on the same files, as after a process restart.
/// </summary>
internal sealed class AnimeAcquisitionEnvironment : IAsyncDisposable
{
    public const string AnimeKey = "frieren";
    public const string SimpleProfileId = "simple";

    private ServiceProvider services;

    private AnimeAcquisitionEnvironment(string tempRoot, DbContextOptions<AppDbContext> options, AppDbContext db, LibraryRoot root, IHardLinkCreator? hardLinkCreator)
    {
        TempRoot = tempRoot;
        Options = options;
        Db = db;
        Root = root;
        HardLinkCreator = hardLinkCreator ?? new FileSystemHardLinkCreator();
        services = Build();
    }

    public string TempRoot { get; }
    public DbContextOptions<AppDbContext> Options { get; }
    public AppDbContext Db { get; private set; }
    public LibraryRoot Root { get; }
    public string DataRoot => Path.Combine(TempRoot, "data");
    public DirectoryInfo AcquisitionDirectory => new(Path.Combine(DataRoot, "acquisition"));
    public string Downloads => Path.Combine(TempRoot, "downloads");
    public string SeriesFolder => Path.Combine(Root.Path, "Frieren");
    public FakeProwlarrClient Prowlarr { get; } = new();
    public FakeSabnzbdClient Sabnzbd { get; } = new();
    public IDataProtectionProvider Protection { get; } = new EphemeralDataProtectionProvider();
    public IHardLinkCreator HardLinkCreator { get; }
    public HttpMessageHandler AniListHandler { get; set; } = new NotConnectedAniListHandler();
    public Guid AnimeId { get; private set; }
    public Guid ProwlarrIndexerEntryId { get; private set; }

    public AnimeAcquisitionScheduler Scheduler => services.GetRequiredService<AnimeAcquisitionScheduler>();
    public AnimeMonitoringStore Monitoring => services.GetRequiredService<AnimeMonitoringStore>();
    public AcquisitionOwnershipStore Ownership => services.GetRequiredService<AcquisitionOwnershipStore>();
    public SabnzbdAcquisitionStore Acquisitions => services.GetRequiredService<SabnzbdAcquisitionStore>();
    public AnimeImportStore Imports => services.GetRequiredService<AnimeImportStore>();
    public OperationStore Operations => new(Db);
    public AnimeImportSettingsStore ImportSettings => services.GetRequiredService<AnimeImportSettingsStore>();
    public AcquisitionPolicyStore Policy => services.GetRequiredService<AcquisitionPolicyStore>();
    public AniListAutoMonitorSettingsStore AniListAutoMonitorSettings => services.GetRequiredService<AniListAutoMonitorSettingsStore>();
    public AniListAccountStore AniListAccounts => services.GetRequiredService<AniListAccountStore>();

    /// <summary>Runs an action against a scoped <see cref="AcquisitionApiKeyService"/>.</summary>
    public async Task<T> WithApiKeysAsync<T>(Func<AcquisitionApiKeyService, Task<T>> action)
    {
        await using var scope = services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AcquisitionApiKeyService>());
    }

    /// <summary>Runs an action against a scoped <see cref="AcquisitionApiService"/> — the same
    /// façade the acquisition automation API endpoints call.</summary>
    public async Task<T> WithAcquisitionApiAsync<T>(Func<AcquisitionApiService, Task<T>> action)
    {
        await using var scope = services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AcquisitionApiService>());
    }

    public async Task<AnimeLibraryLocation?> GetLibraryLocationAsync(Guid animeId, Guid? preferredRootId = null)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AnimeAcquisitionInventory>()
            .GetLibraryLocationAsync(animeId, CancellationToken.None, preferredRootId);
    }

    public async Task<AcquisitionBackupBundle> ExportBackupAsync()
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AcquisitionBackupService>().ExportAsync(CancellationToken.None);
    }

    public async Task<AcquisitionBackupPreview> PreviewRestoreAsync(AcquisitionBackupBundle bundle)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AcquisitionBackupService>().PreviewRestoreAsync(bundle, CancellationToken.None);
    }

    public async Task<AcquisitionBackupRestoreResult> RestoreBackupAsync(AcquisitionBackupBundle bundle)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AcquisitionBackupService>().RestoreAsync(bundle, CancellationToken.None);
    }

    public async Task<AniListAutoMonitorRunResult> RunAniListAutoMonitorAsync(string profileId)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AniListAutoMonitorService>().RunForProfileAsync(profileId, CancellationToken.None);
    }

    public async Task<IReadOnlyList<AcquisitionHistoryEntry>> HistoryForAnimeAsync(Guid animeId, int limit = 50)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AcquisitionHistoryService>().ForAnimeAsync(animeId, limit, CancellationToken.None);
    }

    public static Task<AnimeAcquisitionEnvironment> CreateAsync() => CreateAsync(null);

    public static async Task<AnimeAcquisitionEnvironment> CreateAsync(IHardLinkCreator? hardLinkCreator)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"anilingo-acquisition-{Guid.NewGuid():N}");
        var library = Path.Combine(tempRoot, "anime");
        var dictionary = Path.Combine(tempRoot, "dictionary");
        Directory.CreateDirectory(library);
        Directory.CreateDirectory(dictionary);
        Directory.CreateDirectory(Path.Combine(tempRoot, "downloads"));
        await File.WriteAllTextAsync(Path.Combine(dictionary, "jmdict-ger.tsv"), "");
        await File.WriteAllTextAsync(Path.Combine(dictionary, "jmdict-eng-common.tsv"), "");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(tempRoot, "anilingo.db")};Foreign Keys=True")
            .Options;
        var db = new AppDbContext(options);
        await DatabaseMigrationBridge.UpgradeAsync(db);
        var root = new LibraryRoot { Name = "Anime", Path = library };
        db.LibraryRoots.Add(root);
        await db.SaveChangesAsync();

        var environment = new AnimeAcquisitionEnvironment(tempRoot, options, db, root, hardLinkCreator);
        environment.ProwlarrIndexerEntryId = Guid.NewGuid();
        await environment.services.GetRequiredService<IndexerStore>().SaveAsync(
            new IndexerEntry(
                environment.ProwlarrIndexerEntryId,
                "Prowlarr",
                IndexerType.Prowlarr,
                Enabled: true,
                Priority: 1,
                IndexerSettings.CreateDefault("http://prowlarr:9696", IndexerType.Prowlarr),
                "prowlarr-key"));
        await environment.services.GetRequiredService<DownloadClientStore>().SaveAsync(
            new DownloadClientEntry(
                Guid.NewGuid(),
                "SABnzbd",
                DownloadClientType.Sabnzbd,
                Enabled: true,
                Priority: 1,
                new DownloadClientSettings("http://sabnzbd:8080", "books", "anime"),
                "secret-key"));

        // A simple naming profile keeps the expected library paths readable in assertions.
        var naming = environment.services.GetRequiredService<AnimeNamingProfileStore>();
        await naming.UpsertAsync(new AnimeNamingProfile(
            SimpleProfileId,
            "Simple",
            "{Series Title}",
            "Season {season:00}",
            "Specials",
            "{Series Title} - S{season:00}E{episode:00} - {Episode Title}",
            "{Series Title} - {Air-Date} - {Episode Title}",
            "{Series Title} - S{season:00}E{episode:00} - {Episode Title}",
            UseSeasonFolders: true,
            AnimeMultiEpisodeStyle.PrefixedRange,
            ReplaceIllegalCharacters: true,
            AnimeColonReplacement.Smart));
        await naming.SetDefaultAsync(SimpleProfileId);
        return environment;
    }

    /// <summary>
    /// Frieren with S01E01 on disk, an AniList match with <paramref name="episodeCount"/> episodes,
    /// monitored, and (unless <paramref name="mode"/> is null) the given management mode.
    /// </summary>
    public async Task SeedFrierenAsync(
        int episodeCount = 2,
        AnimeManagementMode? mode = AnimeManagementMode.AniLingoManaged,
        bool seasonFolders = true)
    {
        AddLibraryFile(seasonFolders ? ["Frieren", "Season 01", "Frieren - S01E01 - Episode 1.mkv"] : ["Frieren", "Frieren - S01E01 - Episode 1.mkv"]);
        await ScanAsync();

        var anime = await Db.Anime.AsNoTracking().SingleAsync(item => item.Key == AnimeKey);
        AnimeId = anime.Id;
        Db.AnimeMetadata.Add(new AnimeMetadata
        {
            AnimeId = anime.Id,
            Provider = AniListMetadataProvider.ProviderKey,
            ExternalId = "154587",
            PreferredTitle = "Frieren",
            RomajiTitle = "Sousou no Frieren",
            EpisodeCount = episodeCount
        });
        await Db.SaveChangesAsync();

        await Scheduler.RunExclusiveAsync(
            (pipeline, token) => pipeline.UpdateAnimeSettingsAsync(anime.Id, true, false, null, [], token),
            CancellationToken.None);
        if (mode is { } managementMode)
        {
            await Ownership.UpdateAsync(state => SonarrParallelSafety.SetMode(state, AnimeKey, managementMode, DateTimeOffset.UtcNow));
        }
    }

    public string AddLibraryFile(params string[] relativeParts)
    {
        var path = Path.Combine([Root.Path, .. relativeParts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0]);
        return path;
    }

    public async Task<ScanResult> ScanAsync()
    {
        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<LibraryScanner>().ScanAsync(Root.Id, CancellationToken.None);
        Db.ChangeTracker.Clear();
        return result;
    }

    /// <summary>Writes a completed SABnzbd job folder with one video file and returns its path.</summary>
    public string AddCompletedDownload(string jobName, string fileName)
    {
        var folder = Path.Combine(Downloads, jobName);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, fileName), [1, 2, 3]);
        return folder;
    }

    /// <summary>
    /// SABnzbd reports the job of the acquisition's latest attempt as completed; the monitor
    /// projection runs and returns the completed operation (the import is not started).
    /// </summary>
    public async Task<OperationSnapshot> CompleteLatestDownloadAsync(string storagePath, SabnzbdAcquisition? acquisition = null)
    {
        acquisition ??= (await Acquisitions.LoadAsync()).Acquisitions.Single();
        var operation = (await Operations.GetAsync(acquisition.LatestAttempt!.OperationId))!;
        Sabnzbd.History = new SabnzbdHistorySnapshot(
        [
            new SabnzbdHistoryJob(
                operation.ExternalId!,
                Path.GetFileName(storagePath),
                "Completed",
                "anime",
                storagePath,
                null,
                SabnzbdFailureKind.None,
                DateTimeOffset.UtcNow)
        ]);
        var result = await SabnzbdOperationProjector.ApplyAsync(
            Operations,
            await Operations.ListActiveExternalAsync(SabnzbdClient.ProviderId),
            new SabnzbdQueueSnapshot(false, null, null, []),
            Sabnzbd.History,
            DateTime.UtcNow,
            CancellationToken.None);
        return result.Completed.Single(item => item.Id == operation.Id);
    }

    /// <summary>What the SABnzbd monitor does for a completed anime job.</summary>
    public async Task<AnimeImportRecord?> ImportCompletedAsync(OperationSnapshot download, string storagePath)
    {
        await using var scope = services.CreateAsyncScope();
        var record = await scope.ServiceProvider.GetRequiredService<AnimeImportExecutor>()
            .ImportCompletedAsync(download, storagePath, CancellationToken.None);
        Db.ChangeTracker.Clear();
        return record;
    }

    public async Task<AnimeImportActionResult> ImportManuallyAsync(Guid recordId, string sourcePath, int season, int episode)
    {
        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<AnimeImportExecutor>()
            .ImportManuallyAsync(recordId, sourcePath, season, episode, CancellationToken.None);
        Db.ChangeTracker.Clear();
        return result;
    }

    public async Task<SabnzbdAcquisitionResult> StartAcquisitionAsync(
        IReadOnlyList<AnimeEpisodeKey> episodes,
        string releaseTitle)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SabnzbdAcquisitionService>().StartAsync(
            new SabnzbdAnimeAcquisitionRequest(
                AnimeKey,
                "Frieren",
                episodes,
                null,
                [new SabnzbdAnimeReleaseCandidate($"release:{releaseTitle}", releaseTitle, new Uri("https://indexer.example/a.nzb"))]),
            CancellationToken.None);
    }

    public async Task<IReadOnlyList<OperationLogEntry>> LogsAsync(string module) =>
        await Operations.ListLogsAsync(new OperationLogFilter(Module: module, Limit: 200));

    public async Task<AnimeMonitoringState> MonitoringStateAsync() => await Monitoring.LoadAsync();

    public async Task<MediaFile?> MediaFileAsync(int season, int episode)
    {
        Db.ChangeTracker.Clear();
        return await Db.MediaFiles
            .AsNoTracking()
            .Where(file => Db.Episodes.Any(item =>
                item.Id == file.EpisodeId &&
                item.AnimeId == AnimeId &&
                item.SeasonNumber == season &&
                item.Number == episode))
            .SingleOrDefaultAsync();
    }

    /// <summary>New service instances on the same data, database and library, as after a restart.</summary>
    public async Task RestartAsync()
    {
        await services.DisposeAsync();
        await Db.DisposeAsync();
        Db = new AppDbContext(Options);
        services = Build();
    }

    public static ProwlarrReleaseCandidate Release(string title, string guid, string protocol = "usenet") =>
        new(
            title,
            "Test indexer",
            1,
            protocol,
            1_200_000_000,
            null,
            null,
            DateTimeOffset.UtcNow,
            1,
            24,
            guid,
            null,
            AnimeReleaseParser.Parse(title),
            [],
            protocol == "usenet" ? new Uri($"https://indexer.example/{guid}.nzb?apikey=indexer-secret") : null,
            null);

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        await Db.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(TempRoot))
        {
            foreach (var file in Directory.EnumerateFiles(TempRoot, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(TempRoot, recursive: true);
        }
    }

    private ServiceProvider Build()
    {
        var collection = new ServiceCollection();
        collection.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        collection.AddSingleton(Db);
        collection.AddSingleton(Protection);
        collection.AddSingleton<IConfiguration>(SabnzbdTestSupport.Configuration());
        collection.AddSingleton<ISabnzbdClient>(Sabnzbd);
        collection.AddSingleton<IProwlarrClient>(Prowlarr);
        collection.AddSingleton<ISonarrObserverClient, UnusedSonarrObserverClient>();

        var acquisition = AcquisitionDirectory;
        var integrations = new DirectoryInfo(Path.Combine(DataRoot, "integrations"));
        collection.AddSingleton(new SabnzbdSettingsStore(Protection, acquisition));
        collection.AddSingleton(new SabnzbdAcquisitionStore(Protection, acquisition));
        collection.AddSingleton(new ProwlarrSettingsStore(Protection, acquisition));
        collection.AddSingleton(new IndexerStore(Protection, acquisition));
        collection.AddSingleton(new DownloadClientStore(Protection, acquisition));
        collection.AddSingleton(new AcquisitionHealthStore(acquisition));
        collection.AddSingleton<IReadOnlyDictionary<IndexerType, IIndexer>>(provider =>
            new Dictionary<IndexerType, IIndexer>
            {
                [IndexerType.Prowlarr] = new ProwlarrIndexer(provider.GetRequiredService<IProwlarrClient>())
            });
        collection.AddSingleton<IDownloadClient>(provider =>
            new SabnzbdDownloadClient(provider.GetRequiredService<ISabnzbdClient>()));
        collection.AddScoped<IndexerSearchCoordinator>();
        collection.AddScoped<DownloadClientSelector>();
        collection.AddScoped<DownloadClientSubmissionService>();
        collection.AddSingleton(new AnimeQualityProfileStore(acquisition));
        collection.AddSingleton(new AnimeMonitoringStore(DataRoot));
        collection.AddSingleton(new AnimeImportStore(acquisition));
        collection.AddSingleton(new AnimeNamingProfileStore(acquisition));
        collection.AddSingleton(new AcquisitionOwnershipStore(DataRoot));
        collection.AddSingleton(new SonarrConnectionStore(Protection));
        collection.AddSingleton<SonarrObservationService>();
        collection.AddSingleton(new AniListAccountStore(Protection, NullLogger<AniListAccountStore>.Instance, integrations));
        collection.AddSingleton(new MediaMappingReviewStore(NullLogger<MediaMappingReviewStore>.Instance, integrations));
        collection.AddSingleton(new ReadingSegmentMappingStore(NullLogger<ReadingSegmentMappingStore>.Instance, integrations));
        collection.AddSingleton(new AnimeImportSettingsStore(DataRoot));
        collection.AddSingleton(new AcquisitionPolicyStore(DataRoot));
        collection.AddSingleton(new AniListAutoMonitorSettingsStore(DataRoot));
        collection.AddSingleton<IHardLinkCreator>(HardLinkCreator);
        collection.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(() => AniListHandler));
        collection.AddScoped<AcquisitionHistoryService>();
        collection.AddScoped<AcquisitionBackupService>(_ => new AcquisitionBackupService(DataRoot));
        collection.AddScoped<AniListAutoMonitorService>();

        collection.AddScoped<AnimeMetadataService>();
        collection.AddScoped<SabnzbdConnectionResolver>();
        collection.AddScoped<SabnzbdDownloadService>();
        collection.AddScoped<SabnzbdAcquisitionService>();
        collection.AddScoped<ProwlarrAnimeSearchService>();
        collection.AddScoped<AnimeAcquisitionInventory>();
        collection.AddScoped<AnimeAcquisitionPipeline>();
        collection.AddScoped<AnimeImportExecutor>();
        collection.AddSingleton<AnimeAcquisitionScheduler>();
        collection.AddScoped<AcquisitionApiKeyService>();
        collection.AddScoped<AcquisitionApiService>();
        collection.AddHttpClient();

        var dictionary = Path.Combine(TempRoot, "dictionary");
        var mediaInventory = MediaInventoryTestSupport.Create(Options);
        collection.AddScoped(provider =>
        {
            var db = provider.GetRequiredService<AppDbContext>();
            var sonarrStore = provider.GetRequiredService<SonarrConnectionStore>();
            return new LibraryScanner(
                db,
                new SubtitleImportService(db, new VocabularyService(db, new JapaneseTermExtractor(new NoMorphology()), new JapaneseDictionary(dictionary))),
                new EmbeddedSubtitleExtractor(new MediaProcessRunner(NullLogger<MediaProcessRunner>.Instance), mediaInventory, NullLogger<EmbeddedSubtitleExtractor>.Instance),
                mediaInventory,
                new SonarrArtworkSyncService(
                    sonarrStore,
                    new SonarrArtworkImportService(db, new NoHttpClientFactory(), NullLogger<SonarrArtworkImportService>.Instance),
                    NullLogger<SonarrArtworkSyncService>.Instance),
                NullLogger<LibraryScanner>.Instance);
        });

        return collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class NoHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class SingleHandlerHttpClientFactory(Func<HttpMessageHandler> handlerFactory) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handlerFactory(), disposeHandler: false) { BaseAddress = new Uri("https://graphql.anilist.co/") };
    }

    // The default handler for tests that never connect AniList: any GraphQL call would be a bug.
    private sealed class NotConnectedAniListHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No AniList connection is configured for this test.");
    }

    private sealed class NoMorphology : IJapaneseMorphology
    {
        public IReadOnlyList<JapaneseMorphToken> Analyze(string text) => [];
    }

    private sealed class UnusedSonarrObserverClient : ISonarrObserverClient
    {
        public Task<IReadOnlyList<SonarrObservedSeries>> GetSeriesAsync(SonarrConnectionSettings settings, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<SonarrObservedEpisodeFile>> GetEpisodeFilesAsync(SonarrConnectionSettings settings, int seriesId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<SonarrObservedQueueItem>> GetQueueAsync(SonarrConnectionSettings settings, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<SonarrObservedHistoryEvent>> GetRecentHistoryAsync(SonarrConnectionSettings settings, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

/// <summary>Returns the configured releases for every query and counts the queries.</summary>
internal sealed class FakeProwlarrClient : IProwlarrClient
{
    public List<ProwlarrReleaseCandidate> Releases { get; } = [];
    public List<string> Queries { get; } = [];
    public List<ProwlarrConnection> Connections { get; } = [];

    public Task<ProwlarrConnectionTestResult> TestAsync(ProwlarrConnection connection, CancellationToken cancellationToken) =>
        Task.FromResult(new ProwlarrConnectionTestResult(true, "test"));

    public Task<IReadOnlyList<ProwlarrReleaseCandidate>> SearchAsync(
        ProwlarrConnection connection,
        ProwlarrSearchQuery search,
        CancellationToken cancellationToken)
    {
        Queries.Add(search.Query);
        Connections.Add(connection);
        return Task.FromResult<IReadOnlyList<ProwlarrReleaseCandidate>>([.. Releases]);
    }
}
