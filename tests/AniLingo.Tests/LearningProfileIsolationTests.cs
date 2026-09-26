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
public sealed class LearningProfileIsolationTests
{
    [TestMethod]
    public async Task LearningStateAndPreferencesAreIsolatedByAuthenticatedAccount()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-profile-isolation-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
                .Options;

            Guid termId;
            await using (var setup = new AppDbContext(options))
            {
                await DatabaseMigrationBridge.UpgradeAsync(setup);
                var term = new Term { Language = "ja", Canonical = "猫" };
                setup.Terms.Add(term);
                await setup.SaveChangesAsync();
                termId = term.Id;
            }

            await using (var dbA = new AppDbContext(options))
            {
                var serviceA = new LearningService(
                    dbA,
                    new FsrsReviewScheduler(),
                    Context("account-a"));
                await serviceA.SetStateAsync(
                    termId,
                    UserTermState.Known,
                    CancellationToken.None);
                await serviceA.SavePreferencesAsync(
                    0.94,
                    12,
                    7,
                    CancellationToken.None);
            }

            await using (var dbB = new AppDbContext(options))
            {
                var serviceB = new LearningService(
                    dbB,
                    new FsrsReviewScheduler(),
                    Context("account-b"));
                await serviceB.SetStateAsync(
                    termId,
                    UserTermState.Learning,
                    CancellationToken.None);
                await serviceB.SavePreferencesAsync(
                    0.88,
                    20,
                    3,
                    CancellationToken.None);
            }

            await using var verify = new AppDbContext(options);
            var states = await verify.LearningCards
                .AsNoTracking()
                .OrderBy(x => x.ProfileId)
                .Select(x => new { x.ProfileId, x.State, x.CourseId })
                .ToListAsync();

            Assert.AreEqual(2, states.Count);
            Assert.AreEqual("account-a", states[0].ProfileId);
            Assert.AreEqual(UserTermState.Known, states[0].State);
            Assert.AreEqual("account-b", states[1].ProfileId);
            Assert.AreEqual(UserTermState.Learning, states[1].State);
            Assert.AreNotEqual(states[0].CourseId, states[1].CourseId);

            var courseOwners = await verify.LearningCourses
                .AsNoTracking()
                .OrderBy(x => x.ProfileId)
                .Select(x => x.ProfileId)
                .ToListAsync();
            CollectionAssert.AreEqual(new[] { "account-a", "account-b" }, courseOwners);

            // Each account sees only its own term state.
            var stateA = await LearningQueries.TermStates(verify, "account-a").SingleAsync();
            var stateB = await LearningQueries.TermStates(verify, "account-b").SingleAsync();
            Assert.AreEqual(UserTermState.Known, stateA.State);
            Assert.AreEqual(UserTermState.Learning, stateB.State);

            var preferences = await verify.LearningPreferences
                .AsNoTracking()
                .OrderBy(x => x.ProfileId)
                .ToListAsync();

            Assert.AreEqual(2, preferences.Count);
            Assert.AreEqual(0.94, preferences[0].DesiredRetention, 0.0001);
            Assert.AreEqual(0.88, preferences[1].DesiredRetention, 0.0001);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    private static CurrentAccountContext Context(string id)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, id),
            new Claim(ClaimTypes.Name, id),
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
