using System.Security.Claims;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Pages;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class HomePageQueryTests
{
    [TestMethod]
    public async Task HomeLoadsEpisodeWithoutVocabularyRows()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-home-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
                .Options;

            await using var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var anime = new Anime
            {
                Key = "test",
                Title = "Test"
            };
            var episode = new Episode
            {
                AnimeId = anime.Id,
                SeasonNumber = 1,
                Number = 1,
                Title = "Episode 1",
                DiscoveredAt = DateTime.UtcNow
            };

            db.AddRange(anime, episode);
            await db.SaveChangesAsync();

            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "owner")],
                        "test"))
            };
            var currentAccount = new CurrentAccountContext(
                new HttpContextAccessor { HttpContext = httpContext });
            var model = new IndexModel(db, currentAccount);

            await model.OnGetAsync(CancellationToken.None);

            Assert.AreEqual(1, model.RecentEpisodes.Count);
            Assert.AreEqual(episode.Id, model.RecentEpisodes[0].Id);
            Assert.AreEqual(0, model.RecentEpisodes[0].TotalOccurrences);
            Assert.AreEqual(0, model.RecentEpisodes[0].PreparedOccurrences);
            Assert.AreEqual(0, model.RecentEpisodes[0].PreparationPercent);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }
}
