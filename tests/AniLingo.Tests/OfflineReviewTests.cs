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

            await using (var setup = new AppDbContext(options))
            {
                await DatabaseMigrationBridge.UpgradeAsync(setup);
                var term = new Term { Language = "ja", Canonical = "猫" };
                setup.Terms.Add(term);
                setup.UserTerms.Add(new UserTerm
                {
                    ProfileId = "account-a",
                    TermId = term.Id,
                    State = UserTermState.Learning,
                    LearningStartedAt = DateTime.UtcNow.AddDays(-1),
                    NextReviewAt = DateTime.UtcNow.AddMinutes(-1)
                });
                await setup.SaveChangesAsync();
                termId = term.Id;
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
                    termId,
                    new DateTimeOffset(DateTime.SpecifyKind(firstAt, DateTimeKind.Utc)),
                    [],
                    ReviewRating.Again);
                var expectedFinal = scheduler.Schedule(
                    termId,
                    new DateTimeOffset(DateTime.SpecifyKind(secondAt, DateTimeKind.Utc)),
                    [new ReviewHistoryItem(
                        ReviewRating.Again,
                        new DateTimeOffset(DateTime.SpecifyKind(firstAt, DateTimeKind.Utc)))],
                    ReviewRating.Good);

                var reviews = await db.Reviews
                    .AsNoTracking()
                    .Where(x => x.ProfileId == "account-a")
                    .OrderBy(x => x.ReviewedAt)
                    .ToListAsync();

                Assert.AreEqual(2, reviews.Count);
                Assert.AreEqual(firstId, reviews[0].ClientEventId);
                Assert.AreEqual(expectedFirst.NextReviewAt.UtcDateTime, reviews[0].NextReviewAt);
                Assert.AreEqual(secondId, reviews[1].ClientEventId);
                Assert.AreEqual(expectedFinal.NextReviewAt.UtcDateTime, reviews[1].NextReviewAt);

                var userTerm = await db.UserTerms
                    .AsNoTracking()
                    .SingleAsync(x => x.ProfileId == "account-a" && x.TermId == termId);
                Assert.AreEqual(expectedFinal.NextReviewAt.UtcDateTime, userTerm.NextReviewAt);
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

            await using (var setup = new AppDbContext(options))
            {
                await DatabaseMigrationBridge.UpgradeAsync(setup);
                var own = new Term { Language = "ja", Canonical = "猫" };
                var foreign = new Term { Language = "ja", Canonical = "犬" };
                setup.Terms.AddRange(own, foreign);
                setup.UserTerms.AddRange(
                    new UserTerm
                    {
                        ProfileId = "account-a",
                        TermId = own.Id,
                        State = UserTermState.Learning
                    },
                    new UserTerm
                    {
                        ProfileId = "account-b",
                        TermId = foreign.Id,
                        State = UserTermState.Learning
                    });
                await setup.SaveChangesAsync();
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
                    now.AddMinutes(-1))
            ],
            now,
            CancellationToken.None);

            CollectionAssert.AreEquivalent(
                new[] { futureId, foreignId },
                result.Rejected.ToArray());
            Assert.AreEqual(0, result.Accepted.Count);
            Assert.AreEqual(0, await db.Reviews.CountAsync());
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
                setup.UserTerms.AddRange(
                    new UserTerm
                    {
                        ProfileId = "account-a",
                        TermId = a.Id,
                        State = UserTermState.Learning
                    },
                    new UserTerm
                    {
                        ProfileId = "account-b",
                        TermId = b.Id,
                        State = UserTermState.Learning
                    });
                await setup.SaveChangesAsync();
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
                await verify.Reviews.CountAsync(x => x.ClientEventId == eventId));
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
