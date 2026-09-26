using System.Security.Claims;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class OfflineReviewTests
{
    [TestMethod]
    public async Task ReorderedOfflineEventsApplyOnceAndReplayChronologically()
    {
        var databasePath = TempPath();

        try
        {
            var options = Options(databasePath);
            Guid termId;
            Guid cardId;

            await using (var setup = new AppDbContext(options))
            {
                await DatabaseMigrationBridge.UpgradeAsync(setup);
                var term = new Term { Language = "ja", Canonical = "猫" };
                setup.Terms.Add(term);
                var card = await LearningTestData.SeedTermCardAsync(
                    setup,
                    "account-a",
                    term,
                    UserTermState.Learning,
                    nextReviewAt: DateTime.UtcNow.AddMinutes(-1),
                    learningStartedAt: DateTime.UtcNow.AddDays(-1));
                termId = term.Id;
                cardId = card.Id;
            }

            var firstAt = DateTime.UtcNow.AddMinutes(-20);
            var secondAt = firstAt.AddMinutes(10);
            var firstId = Guid.NewGuid();
            var secondId = Guid.NewGuid();

            await using (var db = new AppDbContext(options))
            {
                var scheduler = new FsrsReviewScheduler();
                var service = new LearningService(db, scheduler, Context("account-a"));

                var result = await service.SyncOfflineReviewsAsync(
                [
                    new OfflineReviewEvent(secondId, termId, ReviewRating.Good, secondAt),
                    new OfflineReviewEvent(firstId, termId, ReviewRating.Again, firstAt)
                ],
                DateTime.UtcNow,
                CancellationToken.None);

                CollectionAssert.AreEquivalent(
                    new[] { firstId, secondId },
                    result.Accepted.ToArray());
                Assert.AreEqual(0, result.AlreadyApplied.Count);
                Assert.AreEqual(0, result.Rejected.Count);

                var repeat = await service.SyncOfflineReviewsAsync(
                [
                    new OfflineReviewEvent(firstId, termId, ReviewRating.Again, firstAt),
                    new OfflineReviewEvent(secondId, termId, ReviewRating.Good, secondAt)
                ],
                DateTime.UtcNow,
                CancellationToken.None);

                Assert.AreEqual(0, repeat.Accepted.Count);
                CollectionAssert.AreEquivalent(
                    new[] { firstId, secondId },
                    repeat.AlreadyApplied.ToArray());

                var expectedFirst = scheduler.Schedule(
                    cardId,
                    new DateTimeOffset(DateTime.SpecifyKind(firstAt, DateTimeKind.Utc)),
                    [],
                    ReviewRating.Again);
                var expectedFinal = scheduler.Schedule(
                    cardId,
                    new DateTimeOffset(DateTime.SpecifyKind(secondAt, DateTimeKind.Utc)),
                    [new ReviewHistoryItem(
                        ReviewRating.Again,
                        new DateTimeOffset(DateTime.SpecifyKind(firstAt, DateTimeKind.Utc)))],
                    ReviewRating.Good);

                var reviews = await db.LearningCardReviews
                    .AsNoTracking()
                    .Where(x => x.ProfileId == "account-a")
                    .OrderBy(x => x.ReviewedAt)
                    .ToListAsync();

                Assert.AreEqual(2, reviews.Count);
                Assert.IsTrue(reviews.All(x => x.CardId == cardId));
                Assert.AreEqual(firstId, reviews[0].ClientEventId);
                Assert.AreEqual(expectedFirst.NextReviewAt.UtcDateTime, reviews[0].NextReviewAt);
                Assert.AreEqual(secondId, reviews[1].ClientEventId);
                Assert.AreEqual(expectedFinal.NextReviewAt.UtcDateTime, reviews[1].NextReviewAt);

                var card = await db.LearningCards
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == cardId);
                Assert.AreEqual(expectedFinal.NextReviewAt.UtcDateTime, card.NextReviewAt);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    public async Task FutureAndForeignProfileEventsAreRejected()
    {
        var databasePath = TempPath();

        try
        {
            var options = Options(databasePath);
            Guid ownTermId;
            Guid foreignTermId;
            Guid foreignCardId;

            await using (var setup = new AppDbContext(options))
            {
                await DatabaseMigrationBridge.UpgradeAsync(setup);
                var own = new Term { Language = "ja", Canonical = "猫" };
                var foreign = new Term { Language = "ja", Canonical = "犬" };
                setup.Terms.AddRange(own, foreign);
                await LearningTestData.SeedTermCardAsync(
                    setup, "account-a", own, UserTermState.Learning);
                foreignCardId = (await LearningTestData.SeedTermCardAsync(
                    setup, "account-b", foreign, UserTermState.Learning)).Id;
                ownTermId = own.Id;
                foreignTermId = foreign.Id;
            }

            await using var db = new AppDbContext(options);
            var service = new LearningService(
                db,
                new FsrsReviewScheduler(),
                Context("account-a"));
            var futureId = Guid.NewGuid();
            var foreignId = Guid.NewGuid();
            var foreignCardEventId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            var result = await service.SyncOfflineReviewsAsync(
            [
                new OfflineReviewEvent(
                    futureId,
                    ownTermId,
                    ReviewRating.Good,
                    now.AddMinutes(1)),
                new OfflineReviewEvent(
                    foreignId,
                    foreignTermId,
                    ReviewRating.Good,
                    now.AddMinutes(-1)),
                new OfflineReviewEvent(
                    foreignCardEventId,
                    null,
                    ReviewRating.Good,
                    now.AddMinutes(-1),
                    foreignCardId)
            ],
            now,
            CancellationToken.None);

            CollectionAssert.AreEquivalent(
                new[] { futureId, foreignId, foreignCardEventId },
                result.Rejected.ToArray());
            Assert.AreEqual(0, result.Accepted.Count);
            Assert.AreEqual(0, await db.LearningCardReviews.CountAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    public async Task SameClientEventIdIsIsolatedPerAccount()
    {
        var databasePath = TempPath();

        try
        {
            var options = Options(databasePath);
            Guid termA;
            Guid termB;

            await using (var setup = new AppDbContext(options))
            {
                await DatabaseMigrationBridge.UpgradeAsync(setup);
                var a = new Term { Language = "ja", Canonical = "猫" };
                var b = new Term { Language = "ja", Canonical = "犬" };
                setup.Terms.AddRange(a, b);
                await LearningTestData.SeedTermCardAsync(
                    setup, "account-a", a, UserTermState.Learning);
                await LearningTestData.SeedTermCardAsync(
                    setup, "account-b", b, UserTermState.Learning);
                termA = a.Id;
                termB = b.Id;
            }

            var eventId = Guid.NewGuid();
            var reviewedAt = DateTime.UtcNow.AddMinutes(-1);

            await using (var dbA = new AppDbContext(options))
            {
                var serviceA = new LearningService(
                    dbA,
                    new FsrsReviewScheduler(),
                    Context("account-a"));
                var resultA = await serviceA.SyncOfflineReviewsAsync(
                    [new OfflineReviewEvent(
                        eventId,
                        termA,
                        ReviewRating.Good,
                        reviewedAt)],
                    DateTime.UtcNow,
                    CancellationToken.None);
                Assert.AreEqual(1, resultA.Accepted.Count);
            }

            await using (var dbB = new AppDbContext(options))
            {
                var serviceB = new LearningService(
                    dbB,
                    new FsrsReviewScheduler(),
                    Context("account-b"));
                var resultB = await serviceB.SyncOfflineReviewsAsync(
                    [new OfflineReviewEvent(
                        eventId,
                        termB,
                        ReviewRating.Good,
                        reviewedAt)],
                    DateTime.UtcNow,
                    CancellationToken.None);
                Assert.AreEqual(1, resultB.Accepted.Count);
            }

            await using var verify = new AppDbContext(options);
            Assert.AreEqual(
                2,
                await verify.LearningCardReviews.CountAsync(x => x.ClientEventId == eventId));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    private static DbContextOptions<AppDbContext> Options(string path) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path};Foreign Keys=True")
            .Options;

    private static string TempPath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"anilingo-offline-review-{Guid.NewGuid():N}.db");

    private static CurrentAccountContext Context(string profileId)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, profileId),
            new Claim(ClaimTypes.Name, profileId),
            new Claim(ClaimTypes.Role, AccountRoles.User)
        ],
        CookieAuthenticationDefaults.AuthenticationScheme);

        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        return new CurrentAccountContext(accessor);
    }
}
