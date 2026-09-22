using AniLingo.Web.Data;
using AniLingo.Web.Features.Ai;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Playback;
using AniLingo.Web.Features.Subtitles;
using AniLingo.Web.Features.Vocabulary;
using AniLingo.Web.Infrastructure;
using AniLingo.Web.Infrastructure.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.Configure<MediaOptions>(builder.Configuration.GetSection(MediaOptions.SectionName));

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
builder.Services.AddScoped<PlaybackRemuxService>();
builder.Services.AddScoped<PlaybackService>();

builder.Services.AddSingleton<CodexCliProvider>();
builder.Services.AddSingleton<IAiProvider>(services => services.GetRequiredService<CodexCliProvider>());

builder.Services.AddSingleton<BackgroundJobQueue>();
builder.Services.AddHostedService<BackgroundJobWorker>();

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

    await db.Database.EnsureCreatedAsync();
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
