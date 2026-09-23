using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class AuthServiceTests
{
    [TestMethod]
    public async Task OwnerAccountIsCreatedHashedAndUsedForLogin()
    {
        var databasePath = CreateDatabasePath();

        try
        {
            var options = CreateOptions(databasePath);
            await using var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var service = new OwnerAuthService(db, new PasswordHasher<OwnerAccount>());

            Assert.IsFalse(await service.HasOwnerAsync());

            var owner = await service.CreateOwnerAsync("Julian", "correct horse battery staple");

            Assert.IsTrue(await service.HasOwnerAsync());
            Assert.AreEqual("Julian", owner.UserName);
            Assert.AreNotEqual("correct horse battery staple", owner.PasswordHash);

            var stored = await db.OwnerAccounts.AsNoTracking().SingleAsync();
            Assert.AreEqual("JULIAN", stored.NormalizedUserName);
            Assert.AreNotEqual("correct horse battery staple", stored.PasswordHash);

            var valid = await service.ValidateCredentialsAsync(
                "julian",
                "correct horse battery staple");
            Assert.IsNotNull(valid);

            var invalid = await service.ValidateCredentialsAsync(
                "julian",
                "wrong password");
            Assert.IsNull(invalid);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    public async Task SecondOwnerCannotBeCreated()
    {
        var databasePath = CreateDatabasePath();

        try
        {
            var options = CreateOptions(databasePath);
            await using var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var service = new OwnerAuthService(db, new PasswordHasher<OwnerAccount>());
            await service.CreateOwnerAsync("owner", "a sufficiently long password");

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.CreateOwnerAsync("other", "another sufficiently long password"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    public async Task LocalUserCanBeCreatedDisabledAndPasswordReset()
    {
        var databasePath = CreateDatabasePath();

        try
        {
            var options = CreateOptions(databasePath);
            await using var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var service = new OwnerAuthService(db, new PasswordHasher<OwnerAccount>());
            var owner = await service.CreateOwnerAsync(
                "owner",
                "a sufficiently long owner password");
            var user = await service.CreateUserAsync(
                "learner",
                "a sufficiently long user password");

            Assert.AreEqual(AccountRole.Owner, owner.Role);
            Assert.AreEqual(AccountRole.User, user.Role);
            Assert.IsTrue(user.IsEnabled);

            var login = await service.ValidateCredentialsAsync(
                "learner",
                "a sufficiently long user password");
            Assert.IsNotNull(login);
            Assert.IsTrue(OwnerAuthService.CreatePrincipal(login).IsInRole(AccountRoles.User));

            await service.SetEnabledAsync(user.Id, false);
            Assert.IsNull(await service.ValidateCredentialsAsync(
                "learner",
                "a sufficiently long user password"));

            await service.SetEnabledAsync(user.Id, true);
            await service.ResetPasswordAsync(
                user.Id,
                "a completely different long password");

            Assert.IsNull(await service.ValidateCredentialsAsync(
                "learner",
                "a sufficiently long user password"));
            Assert.IsNotNull(await service.ValidateCredentialsAsync(
                "learner",
                "a completely different long password"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    public async Task OwnerCannotBeDisabledAndUserNamesAreUnique()
    {
        var databasePath = CreateDatabasePath();

        try
        {
            var options = CreateOptions(databasePath);
            await using var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var service = new OwnerAuthService(db, new PasswordHasher<OwnerAccount>());
            var owner = await service.CreateOwnerAsync(
                "Julian",
                "a sufficiently long owner password");
            await service.CreateUserAsync(
                "Learner",
                "a sufficiently long user password");

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.SetEnabledAsync(owner.Id, false));

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.CreateUserAsync(
                    "learner",
                    "another sufficiently long password"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    private static DbContextOptions<AppDbContext> CreateOptions(string databasePath) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
            .Options;

    private static string CreateDatabasePath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"anilingo-auth-{Guid.NewGuid():N}.db");
}
