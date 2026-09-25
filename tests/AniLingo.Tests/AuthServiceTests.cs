using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.ComponentModel.DataAnnotations;
using AniLingo.Web.Pages.Account;

namespace AniLingo.Tests;

[TestClass]
public sealed class AuthServiceTests
{
    [TestMethod]
    public void RegistrationValidationUsesUserFacingPasswordMessages()
    {
        var shortPassword = new RegisterModel(null!)
        {
            UserName = "learner",
            Password = "short",
            ConfirmPassword = "short"
        };
        var shortResults = new List<ValidationResult>();

        Validator.TryValidateObject(
            shortPassword,
            new ValidationContext(shortPassword),
            shortResults,
            validateAllProperties: true);

        Assert.IsTrue(shortResults.Any(result =>
            result.ErrorMessage == "Password must be at least 12 characters long."));

        var mismatch = new RegisterModel(null!)
        {
            UserName = "learner",
            Password = "123456789012",
            ConfirmPassword = "123456789013"
        };
        var mismatchResults = new List<ValidationResult>();

        Validator.TryValidateObject(
            mismatch,
            new ValidationContext(mismatch),
            mismatchResults,
            validateAllProperties: true);

        Assert.IsTrue(mismatchResults.Any(result =>
            result.ErrorMessage == "Passwords do not match."));
    }

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

    [TestMethod]
    public async Task RegistrationRequestIsDisabledUntilOwnerApproval()
    {
        var databasePath = CreateDatabasePath();

        try
        {
            var options = CreateOptions(databasePath);
            await using var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var service = new OwnerAuthService(db, new PasswordHasher<OwnerAccount>());
            await service.CreateOwnerAsync(
                "owner",
                "a sufficiently long owner password");

            var request = await service.CreateRegistrationRequestAsync(
                "learner",
                "a sufficiently long user password");

            Assert.AreEqual(AccountRole.User, request.Role);
            Assert.IsFalse(request.IsEnabled);
            Assert.AreNotEqual(
                "a sufficiently long user password",
                request.PasswordHash);

            Assert.IsNull(await service.ValidateCredentialsAsync(
                "learner",
                "a sufficiently long user password"));

            await service.SetEnabledAsync(request.Id, true);

            Assert.IsNotNull(await service.ValidateCredentialsAsync(
                "learner",
                "a sufficiently long user password"));

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.CreateRegistrationRequestAsync(
                    "LEARNER",
                    "another sufficiently long password"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    public async Task ExistingDefaultLearningProfileMigratesToOwner()
    {
        var databasePath = CreateDatabasePath();

        try
        {
            var options = CreateOptions(databasePath);
            await using var db = new AppDbContext(options);

            var migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20260923071000_AddOwnerAccount");

            var termId = Guid.NewGuid();
            var userTermId = Guid.NewGuid();
            var now = DateTime.UtcNow.ToString("O");

            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO OwnerAccounts
                    (Id, UserName, NormalizedUserName, PasswordHash, CreatedAt)
                VALUES
                    ('owner', 'Julian', 'JULIAN', 'hash', {now});

                INSERT INTO Terms
                    (Id, Language, Canonical, Reading, Meaning)
                VALUES
                    ({termId.ToString()}, 'ja', '猫', 'ねこ', 'Katze');

                INSERT INTO UserTerms
                    (Id, ProfileId, TermId, State, IntervalDays, NextReviewAt, LearningStartedAt, QueuePosition, UpdatedAt)
                VALUES
                    ({userTermId.ToString()}, 'default', {termId.ToString()}, 1, 0, NULL, NULL, NULL, {now});

                INSERT INTO LearningPreferences
                    (ProfileId, DesiredRetention, ReviewBatchSize, NewWordsPerDay)
                VALUES
                    ('default', 0.91, 20, 5);
                """);

            await DatabaseMigrationBridge.UpgradeAsync(db);

            var migratedTerm = await db.UserTerms.AsNoTracking().SingleAsync();
            Assert.AreEqual(OwnerAccount.SingletonId, migratedTerm.ProfileId);

            var preferences = await db.LearningPreferences.AsNoTracking().SingleAsync();
            Assert.AreEqual(OwnerAccount.SingletonId, preferences.ProfileId);
            Assert.AreEqual(0.91, preferences.DesiredRetention, 0.0001);

            var owner = await db.OwnerAccounts.AsNoTracking().SingleAsync();
            Assert.AreEqual(AccountRole.Owner, owner.Role);
            Assert.IsTrue(owner.IsEnabled);
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
