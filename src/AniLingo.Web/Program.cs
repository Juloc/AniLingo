using AniLingo.Web.Data;
using AniLingo.Web.Features.Ai;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Metadata;
using AniLingo.Web.Features.Playback;
using AniLingo.Web.Features.Subtitles;
using AniLingo.Web.Features.Tracking;
using AniLingo.Web.Features.Vocabulary;
using AniLingo.Web.Infrastructure;
using AniLingo.Web.Infrastructure.Ai;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

await InitializeDatabaseAsync(app.Services);

app.Run();

static async Task InitializeDatabaseAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    await DatabaseMigrationBridge.UpgradeAsync(db);
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

    db.LibraryRoots.Add(new LibraryRoot
    {
        Name = "Anime",
        Path = Path.GetFullPath(media.BootstrapRoot)
    });
    await db.SaveChangesAsync();
}
