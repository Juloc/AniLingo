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
            Assert.AreEqual(10, defaults.NewWordsPerDay);

            await service.SavePreferencesAsync(0.94, 12, 7, CancellationToken.None);
            var saved = await service.GetPreferencesAsync(CancellationToken.None);

            Assert.AreEqual(0.94, saved.DesiredRetention, 0.0001);
            Assert.AreEqual(12, saved.ReviewBatchSize);
            Assert.AreEqual(7, saved.NewWordsPerDay);

            var row = await db.LearningPreferences.AsNoTracking().SingleAsync();
            Assert.AreEqual(LearningProfile.DefaultId, row.ProfileId);
            Assert.AreEqual(0.94, row.DesiredRetention, 0.0001);
            Assert.AreEqual(12, row.ReviewBatchSize);
            Assert.AreEqual(7, row.NewWordsPerDay);
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
                    LearningStartedAt = now.AddDays(-1),
                    NextReviewAt = now.AddMinutes(-index - 1),
                    UpdatedAt = now.AddMinutes(-10)
                });
            }

            await db.SaveChangesAsync();

            var service = new LearningService(db, new FsrsReviewScheduler());
            await service.SavePreferencesAsync(0.90, 5, 10, CancellationToken.None);

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
    public async Task QueuedTermsRespectDailyLimitAndStableOrder()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-learning-queue-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
                .Options;

            await using var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var terms = Enumerable.Range(0, 5)
                .Select(index => new Term
                {
                    Language = "ja",
                    Canonical = $"新語{index}"
                })
                .ToArray();

            db.Terms.AddRange(terms);
            await db.SaveChangesAsync();

            var requestedOrder = new[]
            {
                terms[3].Id,
                terms[1].Id,
                terms[4].Id,
                terms[0].Id,
                terms[2].Id
            };

            var service = new LearningService(db, new FsrsReviewScheduler());
            await service.SavePreferencesAsync(0.90, 10, 2, CancellationToken.None);
            await service.AddToLearningAsync(requestedOrder, CancellationToken.None);

            var queuedBefore = await db.UserTerms
                .AsNoTracking()
                .OrderBy(x => x.QueuePosition)
                .ToListAsync();

            Assert.AreEqual(5, queuedBefore.Count);
            Assert.IsTrue(queuedBefore.All(x => x.NextReviewAt is null));
            CollectionAssert.AreEqual(
                requestedOrder,
                queuedBefore.Select(x => x.TermId).ToArray());

            var firstLoad = await service.GetDueAsync(CancellationToken.None);
            CollectionAssert.AreEqual(
                requestedOrder.Take(2).ToArray(),
                firstLoad.Select(x => x.TermId).ToArray());

            var startedAfterFirstLoad = await db.UserTerms
                .AsNoTracking()
                .CountAsync(x => x.LearningStartedAt != null);

            Assert.AreEqual(2, startedAfterFirstLoad);

            var secondLoad = await service.GetDueAsync(CancellationToken.None);
            CollectionAssert.AreEqual(
                requestedOrder.Take(2).ToArray(),
                secondLoad.Select(x => x.TermId).ToArray());

            var startedAfterSecondLoad = await db.UserTerms
                .AsNoTracking()
                .CountAsync(x => x.LearningStartedAt != null);

            Assert.AreEqual(2, startedAfterSecondLoad);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    public async Task DueReviewsUseBatchBeforeNewWordsAreActivated()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-learning-review-first-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
                .Options;

            await using var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var now = DateTime.UtcNow;
            var reviewTerms = Enumerable.Range(0, 3)
                .Select(index => new Term
                {
                    Language = "ja",
                    Canonical = $"復習{index}"
                })
                .ToArray();
            var newTerms = Enumerable.Range(0, 4)
                .Select(index => new Term
                {
                    Language = "ja",
                    Canonical = $"待機{index}"
                })
                .ToArray();

            db.Terms.AddRange(reviewTerms);
            db.Terms.AddRange(newTerms);

            foreach (var term in reviewTerms)
            {
                db.UserTerms.Add(new UserTerm
                {
                    TermId = term.Id,
                    State = UserTermState.Learning,
                    LearningStartedAt = now.AddDays(-2),
                    NextReviewAt = now.AddMinutes(-5),
                    UpdatedAt = now.AddDays(-1)
                });
            }

            await db.SaveChangesAsync();

            var service = new LearningService(db, new FsrsReviewScheduler());
            await service.SavePreferencesAsync(0.90, 3, 10, CancellationToken.None);
            await service.AddToLearningAsync(
                newTerms.Select(x => x.Id).ToArray(),
                CancellationToken.None);

            var due = await service.GetDueAsync(CancellationToken.None);

            Assert.AreEqual(3, due.Count);
            CollectionAssert.AreEquivalent(
                reviewTerms.Select(x => x.Id).ToArray(),
                due.Select(x => x.TermId).ToArray());

            var newTermIds = newTerms.Select(term => term.Id).ToArray();
            var startedNew = await db.UserTerms
                .AsNoTracking()
                .CountAsync(x =>
                    newTermIds.Contains(x.TermId)
                    && x.LearningStartedAt != null);

            Assert.AreEqual(0, startedNew);
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
                () => service.SavePreferencesAsync(0.79, 50, 10, CancellationToken.None));

            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
                () => service.SavePreferencesAsync(0.90, 201, 10, CancellationToken.None));

            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
                () => service.SavePreferencesAsync(0.90, 50, 101, CancellationToken.None));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }
}
