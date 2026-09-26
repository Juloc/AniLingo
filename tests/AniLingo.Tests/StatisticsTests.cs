using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Statistics;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class StatisticsTests
{
    [TestMethod]
    public async Task LoadAsyncReturnsProfileScopedProgressAndOccurrenceCoverage()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-statistics-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
                .Options;

            await using var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var anime = new Anime { Key = "test", Title = "Test" };
            var episode = new Episode
            {
                AnimeId = anime.Id,
                SeasonNumber = 1,
                Number = 1,
                Title = "Episode 1"
            };
            var known = new Term { Language = "ja", Canonical = "猫" };
            var learning = new Term { Language = "ja", Canonical = "走る" };
            var untouched = new Term { Language = "ja", Canonical = "空" };

            db.AddRange(anime, episode, known, learning, untouched);
            db.EpisodeTerms.AddRange(
                new EpisodeTerm
                {
                    EpisodeId = episode.Id,
                    TermId = known.Id,
                    Occurrences = 5,
                    FirstCueStartMs = 1_000
                },
                new EpisodeTerm
                {
                    EpisodeId = episode.Id,
                    TermId = learning.Id,
                    Occurrences = 3,
                    FirstCueStartMs = 2_000
                },
                new EpisodeTerm
                {
                    EpisodeId = episode.Id,
                    TermId = untouched.Id,
                    Occurrences = 2,
                    FirstCueStartMs = 3_000
                });

            var now = new DateTime(2026, 9, 23, 9, 30, 0, DateTimeKind.Utc);
            const string profile = "profile-a";

            await LearningTestData.SeedTermCardAsync(
                db,
                profile,
                known,
                UserTermState.Known,
                updatedAt: now.AddDays(-3));
            var learningCard = await LearningTestData.SeedTermCardAsync(
                db,
                profile,
                learning,
                UserTermState.Learning,
                nextReviewAt: now.AddMinutes(-5),
                learningStartedAt: now.AddDays(-2),
                updatedAt: now.AddDays(-1));
            var otherCard = await LearningTestData.SeedTermCardAsync(
                db,
                "other-profile",
                untouched,
                UserTermState.Known,
                updatedAt: now.AddDays(-1));

            db.LearningCardReviews.AddRange(
                LearningTestData.Review(profile, learningCard.Id, now.AddHours(-2)),
                LearningTestData.Review(profile, learningCard.Id, now.AddDays(-2)),
                LearningTestData.Review(profile, learningCard.Id, now.AddDays(-10)),
                LearningTestData.Review("other-profile", otherCard.Id, now.AddHours(-1)));

            await db.SaveChangesAsync();

            var result = await new LearningStatisticsService(db)
                .LoadAsync(profile, now, CancellationToken.None);

            Assert.AreEqual(1, result.KnownTerms);
            Assert.AreEqual(1, result.LearningTerms);
            Assert.AreEqual(1, result.DueReviews);
            Assert.AreEqual(3, result.TotalReviews);
            Assert.AreEqual(1, result.ReviewsLast24Hours);
            Assert.AreEqual(2, result.ReviewsLast7Days);
            Assert.AreEqual(8, result.PreparedOccurrences);
            Assert.AreEqual(10, result.TotalOccurrences);
            Assert.AreEqual(80, result.PreparedCoveragePercent);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    public async Task EmptyProfileReturnsZeroUserMetricsWithoutChangingLibraryTotal()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-statistics-empty-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
                .Options;

            await using var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var anime = new Anime { Key = "test", Title = "Test" };
            var episode = new Episode
            {
                AnimeId = anime.Id,
                SeasonNumber = 1,
                Number = 1,
                Title = "Episode"
            };
            var term = new Term { Language = "ja", Canonical = "猫" };

            db.AddRange(anime, episode, term);
            db.EpisodeTerms.Add(new EpisodeTerm
            {
                EpisodeId = episode.Id,
                TermId = term.Id,
                Occurrences = 4,
                FirstCueStartMs = 1_000
            });
            await db.SaveChangesAsync();

            var result = await new LearningStatisticsService(db)
                .LoadAsync(
                    "missing-profile",
                    DateTime.UtcNow,
                    CancellationToken.None);

            Assert.AreEqual(0, result.KnownTerms);
            Assert.AreEqual(0, result.LearningTerms);
            Assert.AreEqual(0, result.DueReviews);
            Assert.AreEqual(0, result.TotalReviews);
            Assert.AreEqual(0, result.PreparedOccurrences);
            Assert.AreEqual(4, result.TotalOccurrences);
            Assert.AreEqual(0, result.PreparedCoveragePercent);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }
}
