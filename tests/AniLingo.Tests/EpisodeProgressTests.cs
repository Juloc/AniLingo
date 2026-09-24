using System.Security.Claims;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Progress;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class EpisodeProgressTests
{
    [TestMethod]
    public async Task ProgressIsProfileScopedAndCompletionIsMonotonic()
    {
        var path = TempDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(path);
            var anime = new Anime { Key = "progress-anime", Title = "Progress Anime" };
            var episode = new Episode
            {
                AnimeId = anime.Id,
                SeasonNumber = 1,
                Number = 3,
                Title = "Episode 3"
            };
            db.Add(anime);
            db.Add(episode);
            await db.SaveChangesAsync();

            var readerA = new EpisodeProgressService(db, Account("reader-a"));
            var readerB = new EpisodeProgressService(db, Account("reader-b"));

            var a = await readerA.UpdateAsync(
                episode.Id,
                new EpisodeProgressUpdate(20_000, 100_000, false));
            var b = await readerB.UpdateAsync(
                episode.Id,
                new EpisodeProgressUpdate(60_000, 100_000, false));

            Assert.IsNotNull(a);
            Assert.IsNotNull(b);
            Assert.AreEqual(20, a.Percent);
            Assert.AreEqual(60, b.Percent);
            Assert.AreEqual(2, await db.EpisodeProgress.CountAsync());

            var completed = await readerA.UpdateAsync(
                episode.Id,
                new EpisodeProgressUpdate(96_000, 100_000, false));

            Assert.IsNotNull(completed);
            Assert.IsTrue(completed.IsCompleted);
            Assert.AreEqual(100, completed.Percent);
            Assert.AreEqual(100_000, completed.PositionMs);

            var laterPartial = await readerA.UpdateAsync(
                episode.Id,
                new EpisodeProgressUpdate(5_000, 100_000, false));

            Assert.IsNotNull(laterPartial);
            Assert.IsTrue(laterPartial.IsCompleted);
            Assert.AreEqual(100_000, laterPartial.PositionMs);

            var readerBAfter = await readerB.GetAsync(episode.Id);
            Assert.IsNotNull(readerBAfter);
            Assert.IsFalse(readerBAfter.IsCompleted);
            Assert.AreEqual(60_000, readerBAfter.PositionMs);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task ExistingEpisodeWithoutProgressReturnsEmptySnapshot()
    {
        var path = TempDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(path);
            var anime = new Anime { Key = "empty-progress", Title = "Empty" };
            var episode = new Episode
            {
                AnimeId = anime.Id,
                SeasonNumber = 1,
                Number = 1,
                Title = "One"
            };
            db.Add(anime);
            db.Add(episode);
            await db.SaveChangesAsync();

            var service = new EpisodeProgressService(db, Account("reader"));
            var progress = await service.GetAsync(episode.Id);

            Assert.IsNotNull(progress);
            Assert.AreEqual(0, progress.PositionMs);
            Assert.AreEqual(0, progress.Percent);
            Assert.IsNull(progress.UpdatedAt);

            Assert.IsNull(await service.GetAsync(Guid.NewGuid()));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static CurrentAccountContext Account(string profileId)
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

    private sealed class FixedHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private static string TempDatabasePath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"anilingo-episode-progress-{Guid.NewGuid():N}.db");

    private static async Task<AppDbContext> CreateDatabaseAsync(string path)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path};Foreign Keys=True")
            .Options;

        var db = new AppDbContext(options);
        await DatabaseMigrationBridge.UpgradeAsync(db);
        return db;
    }
}
