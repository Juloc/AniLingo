using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class LearningPreferencesTests
{
    [TestMethod]
    public async Task DefaultsApplyUntilPreferencesAreSaved()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-learning-preferences-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
                .Options;

            await using var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var service = new LearningService(db, new FsrsReviewScheduler());
            var defaults = await service.GetPreferencesAsync(CancellationToken.None);

            Assert.AreEqual(0.90, defaults.DesiredRetention, 0.0001);
            Assert.AreEqual(50, defaults.ReviewBatchSize);

            await service.SavePreferencesAsync(0.94, 12, CancellationToken.None);
            var saved = await service.GetPreferencesAsync(CancellationToken.None);

            Assert.AreEqual(0.94, saved.DesiredRetention, 0.0001);
            Assert.AreEqual(12, saved.ReviewBatchSize);

            var row = await db.LearningPreferences.AsNoTracking().SingleAsync();
            Assert.AreEqual(LearningProfile.DefaultId, row.ProfileId);
            Assert.AreEqual(0.94, row.DesiredRetention, 0.0001);
            Assert.AreEqual(12, row.ReviewBatchSize);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    public async Task ReviewBatchSizeBoundsDueQuery()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-learning-batch-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
                .Options;

            await using var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var now = DateTime.UtcNow;

            for (var index = 0; index < 8; index++)
            {
                var term = new Term
                {
                    Language = "ja",
                    Canonical = $"語{index}"
                };

                db.Terms.Add(term);
                db.UserTerms.Add(new UserTerm
                {
                    TermId = term.Id,
                    State = UserTermState.Learning,
                    NextReviewAt = now.AddMinutes(-index - 1),
                    UpdatedAt = now.AddMinutes(-10)
                });
            }

            await db.SaveChangesAsync();

            var service = new LearningService(db, new FsrsReviewScheduler());
            await service.SavePreferencesAsync(0.90, 5, CancellationToken.None);

            var due = await service.GetDueAsync(CancellationToken.None);

            Assert.AreEqual(5, due.Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    public async Task SavingRejectsOutOfRangeValues()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-learning-validation-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
                .Options;

            await using var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var service = new LearningService(db, new FsrsReviewScheduler());

            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
                () => service.SavePreferencesAsync(0.79, 50, CancellationToken.None));

            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
                () => service.SavePreferencesAsync(0.90, 201, CancellationToken.None));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }
}
