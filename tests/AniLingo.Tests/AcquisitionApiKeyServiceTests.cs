using AniLingo.Web.Data;
using AniLingo.Web.Features.Acquisition.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class AcquisitionApiKeyServiceTests
{
    [TestMethod]
    public async Task CreateReturnsPlaintextOnceAndStoresOnlyAHash()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using var db = new AppDbContext(CreateOptions(databasePath));
            await DatabaseMigrationBridge.UpgradeAsync(db);
            var service = new AcquisitionApiKeyService(db);

            var (key, rawKey) = await service.CreateAsync("Automation script", CancellationToken.None);

            Assert.IsTrue(rawKey.StartsWith(AcquisitionApiKeyService.KeyPrefixTag, StringComparison.Ordinal));
            Assert.AreEqual("Automation script", key.Name);
            Assert.IsTrue(key.IsActive);
            Assert.AreNotEqual(rawKey, key.KeyHash, "Only a hash is kept on the entity, never the raw key.");
            Assert.IsFalse(key.KeyHash.Contains(rawKey, StringComparison.Ordinal));

            var stored = await db.AcquisitionApiKeys.AsNoTracking().SingleAsync();
            Assert.AreNotEqual(rawKey, stored.KeyHash);
            Assert.IsFalse(stored.KeyHash.Contains(rawKey, StringComparison.Ordinal), "The raw key must never be recoverable from persisted state.");
            Assert.AreEqual(key.KeyPrefix, stored.KeyPrefix);
            Assert.IsTrue(rawKey.StartsWith(stored.KeyPrefix, StringComparison.Ordinal));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    public async Task ValidateAcceptsAFreshKeyAndUpdatesLastUsed()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using var db = new AppDbContext(CreateOptions(databasePath));
            await DatabaseMigrationBridge.UpgradeAsync(db);
            var service = new AcquisitionApiKeyService(db);
            var (key, rawKey) = await service.CreateAsync("Script", CancellationToken.None);
            Assert.IsNull(key.LastUsedAtUtc);

            var validated = await service.ValidateAsync(rawKey, CancellationToken.None);

            Assert.IsNotNull(validated);
            Assert.AreEqual(key.Id, validated!.Id);
            Assert.IsNotNull(validated.LastUsedAtUtc);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    public async Task ValidateRejectsARevokedKey()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using var db = new AppDbContext(CreateOptions(databasePath));
            await DatabaseMigrationBridge.UpgradeAsync(db);
            var service = new AcquisitionApiKeyService(db);
            var (key, rawKey) = await service.CreateAsync("Script", CancellationToken.None);

            var revoked = await service.RevokeAsync(key.Id, CancellationToken.None);
            Assert.IsTrue(revoked);

            var validated = await service.ValidateAsync(rawKey, CancellationToken.None);
            Assert.IsNull(validated, "A revoked key must never authenticate again.");

            var revokedAgain = await service.RevokeAsync(key.Id, CancellationToken.None);
            Assert.IsFalse(revokedAgain, "Revoking an already-revoked key is reported as a no-op.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("not-a-key")]
    [DataRow("alk_completely-made-up-and-never-issued")]
    public async Task ValidateRejectsMissingMalformedOrUnknownKeys(string? candidate)
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using var db = new AppDbContext(CreateOptions(databasePath));
            await DatabaseMigrationBridge.UpgradeAsync(db);
            var service = new AcquisitionApiKeyService(db);
            await service.CreateAsync("Unrelated key", CancellationToken.None);

            var validated = await service.ValidateAsync(candidate, CancellationToken.None);

            Assert.IsNull(validated);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    public async Task RevokeUnknownKeyReturnsFalse()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using var db = new AppDbContext(CreateOptions(databasePath));
            await DatabaseMigrationBridge.UpgradeAsync(db);
            var service = new AcquisitionApiKeyService(db);

            Assert.IsFalse(await service.RevokeAsync(Guid.NewGuid(), CancellationToken.None));
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
            $"anilingo-acquisition-api-key-{Guid.NewGuid():N}.db");
}
