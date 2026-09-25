using AniLingo.Web.Data;
using AniLingo.Web.Features.Admin;
using AniLingo.Web.Features.Ai;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.ClientApi;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Metadata;
using AniLingo.Web.Features.Novels;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Features.Playback;
using AniLingo.Web.Features.PlaybackSessions;
using AniLingo.Web.Features.Progress;
using AniLingo.Web.Features.ReaderThemes;
using AniLingo.Web.Features.Sonarr;
using AniLingo.Web.Features.Statistics;
using AniLingo.Web.Features.Storage;
using AniLingo.Web.Features.Subtitles;
using AniLingo.Web.Features.Tracking;
using AniLingo.Web.Features.Vocabulary;
using AniLingo.Web.Infrastructure;
using AniLingo.Web.Infrastructure.Ai;
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

Console.WriteLine($"[AniLingo] {DateTimeOffset.UtcNow:O} Process starting.");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddSignalR();
builder.Services.Configure<MediaOptions>(builder.Configuration.GetSection(MediaOptions.SectionName));

var dataProtectionDirectory = new DirectoryInfo("/data/keys");
Directory.CreateDirectory(dataProtectionDirectory.FullName);
builder.Services.AddDataProtection()
    .SetApplicationName("AniLingo")
    .PersistKeysToFileSystem(dataProtectionDirectory);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentAccountContext>();
builder.Services.AddScoped<OwnerAuthService>();
builder.Services.AddScoped<AdminUserProgressService>();
builder.Services.AddScoped<OperationRunner>();
builder.Services.AddSingleton<IPasswordHasher<OwnerAccount>, PasswordHasher<OwnerAccount>>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "AniLingo.Auth";
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
            if (ClientApiRoutes.IsClientApi(context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(
                    new ClientErrorResponse(
                        "authentication_required",
                        "Authentication is required for this AniLingo client API endpoint."),
                    context.HttpContext.RequestAborted);
                return;
            }

            context.Response.Redirect(context.RedirectUri);
        };
        options.Events.OnRedirectToAccessDenied = async context =>
        {
            if (ClientApiRoutes.IsClientApi(context.Request.Path))
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
    });
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
builder.Services.AddHostedService<LibraryStartupScanService>();
builder.Services.AddSingleton<StorageAvailabilityCoordinator>();
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
builder.Services.AddScoped<EpisodePreparationService>();
builder.Services.AddScoped<LearningStatisticsService>();
builder.Services.AddSingleton<PlaybackCueProjector>();
builder.Services.AddSingleton<PlaybackMediaProbe>();
builder.Services.AddSingleton<PlaybackPreparationTracker>();
builder.Services.AddScoped<PlaybackPreparationService>();
builder.Services.AddScoped<PlaybackService>();
builder.Services.AddScoped<EpisodeProgressService>();
builder.Services.AddScoped<ClientApiService>();
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

builder.Services.AddHttpClient<NcodeNovelSourceProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("AniLingo/0.1 (+https://github.com/Juloc/AniLingo)");
});
builder.Services.AddScoped<INovelSourceProvider>(
    services => services.GetRequiredService<NcodeNovelSourceProvider>());
builder.Services.AddScoped<NovelService>();

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
    client.DefaultRequestHeaders.UserAgent.ParseAdd("AniLingo/0.1 (+https://github.com/Juloc/AniLingo)");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});

builder.Services.AddHttpClient<SabnzbdOperationsClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHostedService<SabnzbdOperationMonitorService>();

builder.Services.AddSingleton<SonarrConnectionStore>();
builder.Services.AddScoped<SonarrArtworkImportService>();
builder.Services.AddScoped<SonarrArtworkSyncService>();

builder.Services.AddSingleton<AniListAccountStore>();
builder.Services.AddHttpClient<AniListAccountService>(client =>
{
    client.BaseAddress = new Uri("https://graphql.anilist.co/");
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});

builder.Services.AddSingleton<CodexCliProvider>();
builder.Services.AddSingleton<AiProfileSettingsStore>();
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
app.MapHub<PlaybackSessionHub>(PlaybackSessionHub.Route)
    .AllowAnonymous();
app.MapReaderThemeCatalog();
app.MapRazorPages();

try
{
    Console.WriteLine($"[AniLingo] {DateTimeOffset.UtcNow:O} Initializing persistent database.");
    await InitializeDatabaseAsync(
        app.Services,
        message => Console.WriteLine($"[AniLingo] {DateTimeOffset.UtcNow:O} {message}"));
    Console.WriteLine($"[AniLingo] {DateTimeOffset.UtcNow:O} Database ready. Starting web server.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[AniLingo] {DateTimeOffset.UtcNow:O} Startup database initialization failed.");
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
