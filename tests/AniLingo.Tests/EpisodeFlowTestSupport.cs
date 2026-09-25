using System.Security.Claims;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Progress;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

internal sealed class EpisodeFlowFixture : IAsyncDisposable
{
    private readonly string path;
    private LibraryRoot? root;

    private EpisodeFlowFixture(string path, AppDbContext db)
    {
        this.path = path;
        Db = db;
    }

    public AppDbContext Db { get; private set; }

    public static async Task<EpisodeFlowFixture> CreateAsync(bool migrate = true)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-episode-flow-{Guid.NewGuid():N}.db");
        var fixture = new EpisodeFlowFixture(path, CreateContext(path));

        if (migrate)
        {
            await DatabaseMigrationBridge.UpgradeAsync(fixture.Db);
        }

        return fixture;
    }

    public async Task ReopenAsync()
    {
        await Db.DisposeAsync();
        Db = CreateContext(path);
    }

    public EpisodeProgressService Service(string profileId) =>
        new(Db, Account(profileId));

    public async Task<Anime> AddAnimeAsync(string key)
    {
        var anime = new Anime { Key = key, Title = key };
        Db.Add(anime);
        await Db.SaveChangesAsync();
        return anime;
    }

    public async Task<Episode> AddEpisodeAsync(
        Anime anime,
        int seasonNumber,
        int number,
        bool withMedia = true,
        Guid? id = null)
    {
        var episode = new Episode
        {
            Id = id ?? Guid.NewGuid(),
            AnimeId = anime.Id,
            SeasonNumber = seasonNumber,
            Number = number,
            Title = $"Episode {number}"
        };
        Db.Add(episode);

        if (withMedia)
        {
            if (root is null)
            {
                root = new LibraryRoot { Name = "Test", Path = Path.GetTempPath() };
                Db.Add(root);
            }

            Db.Add(new MediaFile
            {
                LibraryRootId = root.Id,
                EpisodeId = episode.Id,
                Path = Path.Combine(
                    Path.GetTempPath(),
                    $"{anime.Key}-s{seasonNumber:00}e{number:00}-{Guid.NewGuid():N}.mkv"),
                SizeBytes = 1,
                LastWriteTimeUtc = DateTime.UtcNow
            });
        }

        await Db.SaveChangesAsync();
        return episode;
    }

    public async Task SetUpdatedAtAsync(
        string profileId,
        Guid episodeId,
        DateTime updatedAt)
    {
        var progress = await Db.EpisodeProgress.SingleAsync(
            x => x.ProfileId == profileId && x.EpisodeId == episodeId);
        progress.UpdatedAt = updatedAt;
        await Db.SaveChangesAsync();
    }

    public static CurrentAccountContext Account(string profileId)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, profileId)],
                    "test"))
        };

        return new CurrentAccountContext(
            new FixedHttpContextAccessor { HttpContext = httpContext });
    }

    // HttpContextAccessor stores its context in a shared AsyncLocal, which
    // would let the last created profile win for every service in a test.
    private sealed class FixedHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(path);
    }

    private static AppDbContext CreateContext(string path) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path};Foreign Keys=True")
            .Options);
}
