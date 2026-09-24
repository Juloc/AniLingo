using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Progress;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class UserManagementTests
{
    [TestMethod]
    public async Task UserCanBeRenamedWithoutChangingProfileIdentity()
    {
        var path = CreateDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(path);
            var auth = CreateAuth(db);
            await auth.CreateOwnerAsync(
                "owner",
                "a sufficiently long owner password");
            var user = await auth.CreateUserAsync(
                "learner",
                "a sufficiently long user password");

            var originalId = user.Id;
            await auth.RenameAsync(user.Id, "Renamed Learner");

            var stored = await auth.GetAsync(user.Id);
            Assert.IsNotNull(stored);
            Assert.AreEqual(originalId, stored.Id);
            Assert.AreEqual("Renamed Learner", stored.UserName);

            Assert.IsNull(await auth.ValidateCredentialsAsync(
                "learner",
                "a sufficiently long user password"));
            Assert.IsNotNull(await auth.ValidateCredentialsAsync(
                "renamed learner",
                "a sufficiently long user password"));

            await auth.CreateUserAsync(
                "other",
                "another sufficiently long user password");

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => auth.RenameAsync(user.Id, "OTHER"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task SessionRevocationRejectsExistingPrincipals()
    {
        var path = CreateDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(path);
            var auth = CreateAuth(db);
            await auth.CreateOwnerAsync(
                "owner",
                "a sufficiently long owner password");
            var user = await auth.CreateUserAsync(
                "learner",
                "a sufficiently long user password");

            var firstPrincipal = OwnerAuthService.CreatePrincipal(user);
            var firstVersion = OwnerAuthService.GetSessionVersion(firstPrincipal);
            Assert.IsNotNull(firstVersion);
            Assert.IsNotNull(await auth.GetEnabledAccountAsync(
                user.Id,
                firstVersion));

            await auth.InvalidateSessionsAsync(user.Id);

            Assert.IsNull(await auth.GetEnabledAccountAsync(
                user.Id,
                firstVersion));

            var refreshed = await auth.GetEnabledAccountAsync(user.Id);
            Assert.IsNotNull(refreshed);
            Assert.IsGreaterThan(firstVersion.Value, refreshed.SessionVersion);

            var refreshedPrincipal = OwnerAuthService.CreatePrincipal(refreshed);
            var refreshedVersion = OwnerAuthService.GetSessionVersion(refreshedPrincipal);
            Assert.AreEqual(refreshed.SessionVersion, refreshedVersion);

            await auth.ResetPasswordAsync(
                user.Id,
                "a completely different long password");

            Assert.IsNull(await auth.GetEnabledAccountAsync(
                user.Id,
                refreshedVersion));

            var afterPasswordReset = await auth.GetEnabledAccountAsync(user.Id);
            Assert.IsNotNull(afterPasswordReset);

            await auth.SetEnabledAsync(user.Id, false);
            Assert.IsNull(await auth.GetEnabledAccountAsync(
                user.Id,
                afterPasswordReset.SessionVersion));

            await auth.SetEnabledAsync(user.Id, true);
            Assert.IsNull(await auth.GetEnabledAccountAsync(
                user.Id,
                afterPasswordReset.SessionVersion));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task DeleteUserRemovesProfileDataAndProtectsOwner()
    {
        var path = CreateDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(path);
            var auth = CreateAuth(db);
            var owner = await auth.CreateOwnerAsync(
                "owner",
                "a sufficiently long owner password");
            var user = await auth.CreateUserAsync(
                "learner",
                "a sufficiently long user password");

            var term = new Term
            {
                Canonical = "猫",
                Language = "ja"
            };
            var anime = new Anime
            {
                Key = "user-delete-test",
                Title = "Delete Test"
            };
            var episode = new Episode
            {
                AnimeId = anime.Id,
                SeasonNumber = 1,
                Number = 1,
                Title = "Episode 1"
            };

            db.AddRange(term, anime, episode);
            db.UserTerms.Add(new UserTerm
            {
                ProfileId = user.Id,
                TermId = term.Id,
                State = UserTermState.Learning
            });
            db.LearningPreferences.Add(new LearningPreferences
            {
                ProfileId = user.Id
            });
            db.EpisodeProgress.Add(new EpisodeProgress
            {
                ProfileId = user.Id,
                EpisodeId = episode.Id,
                PositionMs = 30_000,
                DurationMs = 100_000
            });
            await db.SaveChangesAsync();

            await auth.DeleteUserAsync(user.Id);

            Assert.IsNull(await auth.GetAsync(user.Id));
            Assert.IsNotNull(await auth.GetAsync(owner.Id));
            Assert.AreEqual(0, await db.UserTerms.CountAsync(x => x.ProfileId == user.Id));
            Assert.AreEqual(0, await db.LearningPreferences.CountAsync(x => x.ProfileId == user.Id));
            Assert.AreEqual(0, await db.EpisodeProgress.CountAsync(x => x.ProfileId == user.Id));
            Assert.AreEqual(1, await db.Terms.CountAsync(x => x.Id == term.Id));
            Assert.AreEqual(1, await db.Anime.CountAsync(x => x.Id == anime.Id));

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => auth.DeleteUserAsync(owner.Id));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static OwnerAuthService CreateAuth(AppDbContext db) =>
        new(db, new PasswordHasher<OwnerAccount>());

    private static string CreateDatabasePath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"anilingo-user-management-{Guid.NewGuid():N}.db");

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
