using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Api;
using Jularr.Web.Features.Acquisition.AniListAutoMonitor;
using Jularr.Web.Features.Acquisition.Backup;
using Jularr.Web.Features.Acquisition.DownloadClients;
using Jularr.Web.Features.Acquisition.Health;
using Jularr.Web.Features.Acquisition.History;
using Jularr.Web.Features.Acquisition.Import;
using Jularr.Web.Features.Acquisition.Indexers;
using Jularr.Web.Features.Acquisition.Monitoring;
using Jularr.Web.Features.Acquisition.Pipeline;
using Jularr.Web.Features.Acquisition.Policy;
using Jularr.Web.Features.Acquisition.Prowlarr;
using Jularr.Web.Features.Acquisition.Quality;
using Jularr.Web.Features.Acquisition.Sabnzbd;
using Jularr.Web.Features.Admin;
using Jularr.Web.Features.Ai;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.ClientApi;
using Jularr.Web.Features.Learning;
using Jularr.Web.Features.Learning.LanguageAssistance;
using Jularr.Web.Features.Library;
using Jularr.Web.Features.Media.Optimization;
using Jularr.Web.Features.MediaMapping;
using Jularr.Web.Features.MediaSegments;
using Jularr.Web.Features.Metadata;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.OfflineLibrary;
using Jularr.Web.Features.Operations;
using Jularr.Web.Features.Playback;
using Jularr.Web.Features.PlaybackSessions;
using Jularr.Web.Features.Progress;
using Jularr.Web.Features.ReaderThemes;
using Jularr.Web.Features.Sonarr;
using Jularr.Web.Features.Statistics;
using Jularr.Web.Features.Storage;
using Jularr.Web.Features.StoryContext;
using Jularr.Web.Features.Subtitles;
using Jularr.Web.Features.Tracking;
using Jularr.Web.Features.Vocabulary;
using Jularr.Web.Infrastructure;
using Jularr.Web.Infrastructure.Ai;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;

Console.WriteLine($"[Jularr] {DateTimeOffset.UtcNow:O} Process starting.");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddSignalR();
builder.Services.Configure<MediaOptions>(builder.Configuration.GetSection(MediaOptions.SectionName));

var dataProtectionDirectory = new DirectoryInfo("/data/keys");
Directory.CreateDirectory(dataProtectionDirectory.FullName);
builder.Services.AddDataProtection()
    // Stays "AniLingo" after the Jularr rebrand: the application name isolates the Data Protection
    // key ring, and changing it would invalidate every sign-in cookie and every secret already
    // protected with it (download-client passwords, indexer/Prowlarr/SABnzbd/AI API keys, AniList tokens).
    .SetApplicationName("AniLingo")
    .PersistKeysToFileSystem(dataProtectionDirectory);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<OperationProfileContext>();
builder.Services.AddScoped<CurrentAccountContext>();
builder.Services.AddScoped<OwnerAuthService>();
builder.Services.AddScoped<AdminUserProgressService>();
builder.Services.AddScoped<OperationRunner>();
builder.Services.AddSingleton<IPasswordHasher<OwnerAccount>, PasswordHasher<OwnerAccount>>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "Jularr.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = async context =>
        {
            var accountId = OwnerAuthService.GetAccountId(context.Principal!);
            if (string.IsNullOrWhiteSpace(accountId))
            {
                context.RejectPrincipal();
                return;
            }

            var cookieSessionVersion = OwnerAuthService.GetSessionVersion(
                context.Principal!);
            var auth = context.HttpContext.RequestServices
                .GetRequiredService<OwnerAuthService>();
            var account = await auth.GetEnabledAccountAsync(
                accountId,
                cookieSessionVersion,
                context.HttpContext.RequestAborted);

            if (account is null)
            {
                context.RejectPrincipal();
                return;
            }

            var currentName = context.Principal?.Identity?.Name;
            var currentRole = context.Principal?.FindFirstValue(ClaimTypes.Role);
            var expectedRole = account.Role.ToString();

            if (!string.Equals(currentName, account.UserName, StringComparison.Ordinal)
                || !string.Equals(currentRole, expectedRole, StringComparison.Ordinal)
                || cookieSessionVersion != account.SessionVersion)
            {
                context.ReplacePrincipal(OwnerAuthService.CreatePrincipal(account));
                context.ShouldRenew = true;
            }
        };
        options.Events.OnRedirectToLogin = async context =>
        {
            if (ClientApiRoutes.IsClientApi(context.Request.Path) ||
                AcquisitionApiRoutes.IsAcquisitionApi(context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(
                    new ClientErrorResponse(
                        "authentication_required",
                        "Authentication is required for this Jularr client API endpoint."),
                    context.HttpContext.RequestAborted);
                return;
            }

            context.Response.Redirect(context.RedirectUri);
        };
        options.Events.OnRedirectToAccessDenied = async context =>
        {
            if (ClientApiRoutes.IsClientApi(context.Request.Path) ||
                AcquisitionApiRoutes.IsAcquisitionApi(context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(
                    new ClientErrorResponse(
                        "access_denied",
                        "The authenticated account is not allowed to use this endpoint."),
                    context.HttpContext.RequestAborted);
                return;
            }

            context.Response.Redirect(context.RedirectUri);
        };
    })
    .AddScheme<AuthenticationSchemeOptions, AcquisitionApiKeyAuthenticationHandler>(
        AcquisitionApiKeyAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    options.AddPolicy("wake", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 6,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    // Scoped by API key id when the request authenticated with one, otherwise by IP (a cookie
    // owner session sharing the browser's normal traffic does not need its own partition).
    options.AddPolicy("acquisitionApi", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("fc00::/7"));
});

builder.Services.AddSingleton<MediaProcessRunner>();
builder.Services.AddScoped<LibraryScanner>();
builder.Services.AddSingleton<IMediaProbeRunner, FfprobeMediaProbeRunner>();
builder.Services.AddSingleton<MediaInventoryService>();
builder.Services.AddSingleton<IMediaContainerRemuxer, FfmpegMediaContainerRemuxer>();
builder.Services.AddSingleton(_ => new MediaOptimizationJournal("/data/media-optimization"));
builder.Services.AddSingleton<MediaOptimizationQueue>();
builder.Services.AddScoped<MediaContainerOptimizer>();
builder.Services.AddScoped<IMediaFileReplacementParticipant, AcquisitionMediaReplacementParticipant>();
builder.Services.AddHostedService<MediaOptimizationRecoveryService>();
builder.Services.AddSingleton<LibraryScanCoordinator>();
builder.Services.AddHostedService<LibraryStartupScanService>();
builder.Services.AddHostedService<LibraryWatchService>();
builder.Services.AddSingleton<StorageAvailabilityCoordinator>();
builder.Services.AddSingleton<IWakeOnLanPacketSender, UdpWakeOnLanPacketSender>();
builder.Services.AddSingleton(new StorageWakeOptions());
builder.Services.AddSingleton<StorageWakeCoordinator>();
builder.Services.AddScoped<StorageIntegrityService>();
builder.Services.AddScoped<LibraryRootAvailabilityService>();
builder.Services.AddScoped<MediaAvailabilityService>();
builder.Services.AddScoped<WakeOnLanService>();
builder.Services.AddScoped<SubtitleImportService>();
builder.Services.AddSingleton<EmbeddedSubtitleExtractor>();
builder.Services.AddScoped<VocabularyService>();
builder.Services.AddSingleton<IJapaneseMorphology, MeCabJapaneseMorphology>();
builder.Services.AddSingleton<JapaneseTermExtractor>();
builder.Services.AddSingleton<JapaneseDictionary>();
builder.Services.AddSingleton<IReviewScheduler, FsrsReviewScheduler>();
builder.Services.AddScoped<LearningService>();
builder.Services.AddSingleton<LanguageTextAnalyzer>();
builder.Services.AddScoped<LanguageInspectorService>();
builder.Services.AddScoped<EpisodePreparationService>();
builder.Services.AddScoped<LearningStatisticsService>();
builder.Services.AddSingleton<PlaybackCueProjector>();
builder.Services.AddSingleton<PlaybackPreparationTracker>();
builder.Services.AddScoped<PlaybackPreparationService>();
builder.Services.AddScoped<PlaybackService>();
builder.Services.Configure<MediaSegmentOptions>(builder.Configuration.GetSection(MediaSegmentOptions.SectionName));
// Single canonical opt-in: cross-episode audio fingerprint detection is CPU heavy (it decodes and
// hashes several minutes of audio per episode), so it stays off unless explicitly enabled.
var fingerprintDetectionEnabled = builder.Configuration
    .GetSection(MediaSegmentOptions.SectionName)
    .GetValue<bool>(nameof(MediaSegmentOptions.FingerprintDetectionEnabled));
builder.Services.AddSingleton<IAudioWindowDecoder, FfmpegAudioWindowDecoder>();
if (fingerprintDetectionEnabled)
{
    builder.Services.AddSingleton<IMediaSegmentDetector, AudioFingerprintMediaSegmentDetector>();
}
else
{
    builder.Services.AddSingleton<IMediaSegmentDetector, NoOpMediaSegmentDetector>();
}

builder.Services.AddSingleton<TrickplayGenerator>();
builder.Services.AddSingleton<SeasonSegmentDetectionQueue>();
builder.Services.AddScoped<MediaSegmentService>();
builder.Services.AddScoped<MediaSegmentSidecarImporter>();
builder.Services.AddScoped<EpisodeProgressService>();
builder.Services.AddScoped<ClientApiService>();
builder.Services.AddScoped<ClientApiOfflineService>();
builder.Services.AddScoped<OfflineProgressReconciler>();
builder.Services.AddScoped<OfflineLibraryQueries>();
builder.Services.AddScoped<OfflineLibraryProgressReconciler>();
builder.Services.AddScoped<OfflineLibraryBookmarkReconciler>();
builder.Services.AddScoped<ClientApiOfflineLibraryService>();
builder.Services.AddScoped<Jularr.Web.Features.Speech.TtsPreferencesService>();
builder.Services.AddSingleton(_ => new Jularr.Web.Features.Speech.SpeechModelManifestStore("/data"));
builder.Services.AddSingleton<ReaderThemeCatalog>();

builder.Services.AddHttpClient<AniListMetadataProvider>(client =>
{
    client.BaseAddress = new Uri("https://graphql.anilist.co/");
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddScoped<IAnimeMetadataProvider>(
    services => services.GetRequiredService<AniListMetadataProvider>());
builder.Services.AddScoped<AnimeMetadataService>();
builder.Services.AddScoped<AnimeRepairService>();

builder.Services.AddHttpClient<NcodeNovelSourceProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Jularr/0.1 (+https://github.com/Juloc/Jularr)");
});
builder.Services.AddScoped<INovelSourceProvider>(
    services => services.GetRequiredService<NcodeNovelSourceProvider>());
builder.Services.AddScoped<NovelImportService>();
builder.Services.AddSingleton<NovelVolumeAssetStore>();
builder.Services.AddScoped<NovelEpubImportService>();
builder.Services.AddScoped<NovelCatalogQueries>();
builder.Services.AddScoped<NovelProgressService>();
builder.Services.AddScoped<NovelAnnotationService>();
builder.Services.AddScoped<NovelJobs>();

builder.Services.AddHttpClient<NovelAniListProvider>(client =>
{
    client.BaseAddress = new Uri("https://graphql.anilist.co/");
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddScoped<INovelMetadataProvider>(
    services => services.GetRequiredService<NovelAniListProvider>());
builder.Services.AddScoped<NovelMetadataService>();
builder.Services.AddScoped<NovelTranslationService>();
builder.Services.AddScoped<NovelMappingService>();

builder.Services.AddHttpClient<BookCatalogService>(client =>
{
    client.BaseAddress = new Uri("https://gutendex.com/");
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Jularr/0.1 (+https://github.com/Juloc/Jularr)");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});

// Legacy single-connection stores: read once by the one-time settings migration below, then
// unused. Kept registered only so that migration can resolve them.
builder.Services.AddSingleton<SabnzbdSettingsStore>();
builder.Services.AddSingleton<SabnzbdConnectionResolver>();
builder.Services.AddSingleton<SabnzbdAcquisitionStore>();
builder.Services.AddHttpClient<ISabnzbdClient, SabnzbdClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddSingleton<ProwlarrSettingsStore>();
builder.Services.AddHttpClient<IProwlarrClient, ProwlarrClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});

// Indexers: Prowlarr and direct Newznab connections share the one canonical list. Jularr is
// usenet-only; torrent indexers (Torznab) are intentionally unsupported.
builder.Services.AddSingleton<IndexerStore>();
builder.Services.AddHttpClient<NewznabIndexer>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddSingleton<IReadOnlyDictionary<IndexerType, IIndexer>>(services =>
    new Dictionary<IndexerType, IIndexer>
    {
        [IndexerType.Prowlarr] = new ProwlarrIndexer(services.GetRequiredService<IProwlarrClient>()),
        [IndexerType.Newznab] = services.GetRequiredService<NewznabIndexer>()
    });
builder.Services.AddScoped<IndexerSearchCoordinator>();
builder.Services.AddScoped<Jularr.Web.Features.Acquisition.Access.AcquisitionAccessStore>();
builder.Services.AddScoped<Jularr.Web.Features.Acquisition.Access.AcquisitionRequestService>();
builder.Services.AddScoped<Jularr.Web.Features.Acquisition.Access.IAcquisitionRequestExecutor, Jularr.Web.Features.Books.BookAcquisitionExecutor>();
builder.Services.AddScoped<Jularr.Web.Features.Acquisition.Access.IAcquisitionRequestExecutor, Jularr.Web.Features.Acquisition.Access.AnimeAcquisitionRequestExecutor>();

// Download clients: SABnzbd connections share the one canonical list (several can fail over to
// each other). Jularr is usenet-only; torrent clients (qBittorrent) are intentionally
// unsupported. The pipeline and Books submission only depend on IDownloadClient, never on
// SabnzbdDownloadClient directly.
builder.Services.AddSingleton<DownloadClientStore>();
builder.Services.AddSingleton<IDownloadClient>(services =>
    new SabnzbdDownloadClient(services.GetRequiredService<ISabnzbdClient>()));
builder.Services.AddScoped<DownloadClientSelector>();
builder.Services.AddScoped<DownloadClientSubmissionService>();

builder.Services.AddSingleton<AcquisitionHealthStore>();
builder.Services.AddHostedService<AcquisitionHealthCheckService>();

builder.Services.AddScoped<SabnzbdDownloadService>();
builder.Services.AddScoped<SabnzbdAcquisitionService>();
builder.Services.AddHostedService<SabnzbdOperationMonitorService>();
builder.Services.AddHostedService<Jularr.Web.Features.Books.BookRequestSearchService>();

builder.Services.AddSingleton<AnimeQualityProfileStore>();
builder.Services.AddSingleton(_ => new AnimeMonitoringStore("/data"));
builder.Services.AddSingleton<AnimeImportStore>();
builder.Services.AddSingleton(_ => new AnimeImportSettingsStore("/data"));
builder.Services.AddSingleton<IHardLinkCreator, FileSystemHardLinkCreator>();
builder.Services.AddSingleton(_ => new AcquisitionPolicyStore("/data"));
builder.Services.AddSingleton(_ => new AniListAutoMonitorSettingsStore("/data"));
builder.Services.AddScoped<AniListAutoMonitorService>();
builder.Services.AddScoped<AcquisitionHistoryService>();
builder.Services.AddScoped<AcquisitionBackupService>();
builder.Services.AddScoped<AnimeAcquisitionInventory>();
builder.Services.AddScoped<AnimeAcquisitionPipeline>();
builder.Services.AddScoped<AnimeImportExecutor>();
builder.Services.AddSingleton<AnimeAcquisitionScheduler>();
builder.Services.AddHostedService(services => services.GetRequiredService<AnimeAcquisitionScheduler>());
Jularr.Web.Features.Calendar.ReleaseCalendarRegistration.AddReleaseCalendar(builder.Services);

builder.Services.AddScoped<AcquisitionApiKeyService>();
builder.Services.AddScoped<AcquisitionApiService>();

builder.Services.AddSingleton<SonarrConnectionStore>();
builder.Services.AddScoped<SonarrArtworkImportService>();
builder.Services.AddScoped<SonarrArtworkSyncService>();
builder.Services.AddSingleton(_ =>
    new Jularr.Web.Features.Acquisition.Ownership.AcquisitionOwnershipStore("/data"));
builder.Services.AddSingleton<ISonarrObserverClient, SonarrObserverClient>();
builder.Services.AddSingleton<ISonarrSeriesMonitoringClient, SonarrSeriesMonitoringClient>();
builder.Services.AddSingleton<SonarrObservationService>();
builder.Services.AddSingleton<SonarrMigrationService>();
builder.Services.AddSingleton<Jularr.Web.Features.Acquisition.Naming.AnimeNamingProfileStore>();
builder.Services.AddSingleton<Jularr.Web.Features.Acquisition.Naming.AnimeRenameFileSystem>();
builder.Services.AddScoped<Jularr.Web.Features.Acquisition.Naming.AnimeRenameService>();

builder.Services.AddSingleton<MediaMappingReviewStore>();
builder.Services.AddSingleton<ReadingSegmentMappingStore>();
builder.Services.AddSingleton<AniListAccountStore>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AniListRateLimitGate>();
builder.Services.AddTransient<AniListRateLimitHandler>();
builder.Services.AddHttpClient<AniListAccountService>(client =>
{
    client.BaseAddress = new Uri("https://graphql.anilist.co/");
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
}).AddHttpMessageHandler<AniListRateLimitHandler>();
builder.Services.AddSingleton<AniListSyncStateStore>();
builder.Services.AddScoped<AniListSyncService>();
builder.Services.AddHostedService<AniListSyncBackgroundService>();

builder.Services.AddSingleton<ICodexAppServerLauncher, CodexAppServerProcessLauncher>();
builder.Services.AddSingleton<CodexAppServerClient>();
builder.Services.AddSingleton<CodexAppServerGateway>();
builder.Services.AddSingleton<CodexCliProvider>();
builder.Services.AddSingleton<AiProfileSettingsStore>();
builder.Services.AddSingleton<AiUsagePersistenceQueue>();
builder.Services.AddSingleton<IAiUsageSink>(services => services.GetRequiredService<AiUsagePersistenceQueue>());
builder.Services.AddHostedService<AiUsagePersistenceWorker>();
builder.Services.AddSingleton<AiUsageTracker>();
builder.Services.AddSingleton<AiActivityTracker>();
builder.Services.AddSingleton<AiActivityRunner>();
builder.Services.AddScoped<AiUsageStore>();
builder.Services.AddScoped<IAiModelCatalogStore, AiModelCatalogStore>();
builder.Services.AddScoped<AiModelCatalogService>();
builder.Services.AddHttpClient("ai-openai-compatible", client =>
{
    client.Timeout = TimeSpan.FromMinutes(4);
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddScoped<ProfileAiProviderRouter>();
builder.Services.AddScoped<IAiProvider>(services => services.GetRequiredService<ProfileAiProviderRouter>());
builder.Services.AddScoped<IAiSentenceExplainer>(services => services.GetRequiredService<ProfileAiProviderRouter>());
builder.Services.AddScoped<INovelTranslator>(services => services.GetRequiredService<ProfileAiProviderRouter>());
builder.Services.AddScoped<IBookTranslator>(services => services.GetRequiredService<ProfileAiProviderRouter>());
builder.Services.AddScoped<INovelMappingSuggester>(services => services.GetRequiredService<ProfileAiProviderRouter>());
builder.Services.AddScoped<IStoryContextExtractor>(services => services.GetRequiredService<ProfileAiProviderRouter>());
builder.Services.AddSingleton(services => StoryContextStore.FromConfiguration(services.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<StoryContextSnapshotCache>();
builder.Services.AddScoped<StoryContextService>();
builder.Services.AddScoped<AiSentenceExplanationService>();

builder.Services.AddSingleton<BackgroundJobQueue>();
builder.Services.AddHostedService<BackgroundJobWorker>();
builder.Services.AddSingleton<PlaybackJobQueue>();
builder.Services.AddHostedService<PlaybackJobWorker>();
builder.Services.AddSingleton<PlaybackSessionStore>();
builder.Services.AddSingleton<PlaybackSessionConnectionRegistry>();
builder.Services.AddSingleton<PlaybackSessionCoordinator>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseForwardedHeaders();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapClientApiV1();
app.MapAcquisitionApiV1();
app.MapClientApiOfflineV1();
app.MapClientApiOfflineLibraryV1();
app.MapHub<PlaybackSessionHub>(PlaybackSessionHub.Route)
    .AllowAnonymous();
app.MapReaderThemeCatalog();
app.MapLanguageInspector();
app.MapAiActivity();
app.MapRazorPages();

try
{
    Console.WriteLine($"[Jularr] {DateTimeOffset.UtcNow:O} Initializing persistent database.");
    LegacyDatabaseFileMigration.Run(
        connectionString,
        message => Console.WriteLine($"[Jularr] {DateTimeOffset.UtcNow:O} {message}"));
    await InitializeDatabaseAsync(
        app.Services,
        message => Console.WriteLine($"[Jularr] {DateTimeOffset.UtcNow:O} {message}"));
    await SabnzbdSettingsMigration.RunAtStartupAsync(
        app.Services,
        message => Console.WriteLine($"[Jularr] {DateTimeOffset.UtcNow:O} {message}"));
    await IndexerSettingsMigration.RunAtStartupAsync(
        app.Services,
        message => Console.WriteLine($"[Jularr] {DateTimeOffset.UtcNow:O} {message}"));
    await DownloadClientSettingsMigration.RunAtStartupAsync(
        app.Services,
        message => Console.WriteLine($"[Jularr] {DateTimeOffset.UtcNow:O} {message}"));
    var migratedBibles = await BookTranslationMemoryStore
        .FromConfiguration(app.Configuration)
        .MigrateLegacyAsync(CancellationToken.None);
    if (migratedBibles > 0)
    {
        Console.WriteLine($"[Jularr] {DateTimeOffset.UtcNow:O} Moved {migratedBibles} translation bible(s) into the shared story context.");
    }
    Console.WriteLine($"[Jularr] {DateTimeOffset.UtcNow:O} Database ready. Starting web server.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[Jularr] {DateTimeOffset.UtcNow:O} Startup database initialization failed.");
    Console.Error.WriteLine(ex);
    throw;
}

await app.RunAsync();

static async Task InitializeDatabaseAsync(
    IServiceProvider services,
    Action<string> log)
{
    await using var scope = services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    await DatabaseMigrationBridge.UpgradeAsync(db, log: log);

    log("Enabling SQLite WAL journal mode.");
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");

    if (await db.LibraryRoots.AnyAsync())
    {
        return;
    }

    var media = scope.ServiceProvider.GetRequiredService<IOptions<MediaOptions>>().Value;
    if (string.IsNullOrWhiteSpace(media.BootstrapRoot))
    {
        return;
    }

    log($"Creating bootstrap library root {media.BootstrapRoot}.");

    db.LibraryRoots.Add(new LibraryRoot
    {
        Name = "Anime",
        Path = Path.GetFullPath(media.BootstrapRoot)
    });
    await db.SaveChangesAsync();
}
