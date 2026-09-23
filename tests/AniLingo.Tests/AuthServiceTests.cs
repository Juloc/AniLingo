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

    private static DbContextOptions<AppDbContext> CreateOptions(string databasePath) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
            .Options;

    private static string CreateDatabasePath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"anilingo-auth-{Guid.NewGuid():N}.db");
}
