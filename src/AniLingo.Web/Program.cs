using AniLingo.Web.Data;
using AniLingo.Web.Features.Ai;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Metadata;
using AniLingo.Web.Features.Playback;
using AniLingo.Web.Features.Sonarr;
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

Console.WriteLine($"[AniLingo] {DateTimeOffset.UtcNow:O} Process starting.");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.Configure<MediaOptions>(builder.Configuration.GetSection(MediaOptions.SectionName));

var dataProtectionDirectory = new DirectoryInfo("/data/keys");
Directory.CreateDirectory(dataProtectionDirectory.FullName);
builder.Services.AddDataProtection()
    .SetApplicationName("AniLingo")
    .PersistKeysToFileSystem(dataProtectionDirectory);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));

builder.Services.AddScoped<OwnerAuthService>();
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
builder.Services.AddScoped<SubtitleImportService>();
builder.Services.AddSingleton<EmbeddedSubtitleExtractor>();
builder.Services.AddScoped<VocabularyService>();
builder.Services.AddSingleton<IJapaneseMorphology, MeCabJapaneseMorphology>();
builder.Services.AddSingleton<JapaneseTermExtractor>();
builder.Services.AddSingleton<JapaneseDictionary>();
builder.Services.AddSingleton<IReviewScheduler, FsrsReviewScheduler>();
builder.Services.AddScoped<LearningService>();
builder.Services.AddScoped<EpisodePreparationService>();
builder.Services.AddSingleton<PlaybackCueProjector>();
builder.Services.AddSingleton<PlaybackMediaProbe>();
builder.Services.AddSingleton<PlaybackPreparationTracker>();
builder.Services.AddScoped<PlaybackPreparationService>();
builder.Services.AddScoped<PlaybackService>();

builder.Services.AddHttpClient<AniListMetadataProvider>(client =>
{
    client.BaseAddress = new Uri("https://graphql.anilist.co/");
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddScoped<IAnimeMetadataProvider>(
    services => services.GetRequiredService<AniListMetadataProvider>());
builder.Services.AddScoped<AnimeMetadataService>();

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
builder.Services.AddSingleton<IAiProvider>(services => services.GetRequiredService<CodexCliProvider>());
builder.Services.AddSingleton<IAiSentenceExplainer>(services => services.GetRequiredService<CodexCliProvider>());
builder.Services.AddScoped<AiSentenceExplanationService>();

builder.Services.AddSingleton<BackgroundJobQueue>();
builder.Services.AddHostedService<BackgroundJobWorker>();
builder.Services.AddSingleton<PlaybackJobQueue>();
builder.Services.AddHostedService<PlaybackJobWorker>();

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
