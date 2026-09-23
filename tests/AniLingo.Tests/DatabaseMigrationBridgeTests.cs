using AniLingo.Web.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AniLingo.Tests;

[TestClass]
public sealed class DatabaseMigrationBridgeTests
{
    private const string PreAiMigration = "20260922184600_AddDailyNewWordQueue";
    private const string AiMigration = "20260922190000_AddAiSentenceExplanationCache";

    [TestMethod]
    public async Task AbandonedSqliteMigrationLockIsRecoveredBeforePendingMigration()
    {
        var databasePath = CreateDatabasePath();

        try
        {
            var options = CreateOptions(databasePath);

            await using (var setup = new AppDbContext(options))
            {
                var migrator = setup.GetService<IMigrator>();
                await migrator.MigrateAsync(PreAiMigration);

                await setup.Database.ExecuteSqlRawAsync(
                    """
                    CREATE TABLE IF NOT EXISTS "__EFMigrationsLock" (
                        "Id" INTEGER NOT NULL CONSTRAINT "PK___EFMigrationsLock" PRIMARY KEY,
                        "Timestamp" TEXT NOT NULL
                    );
                    """);

                await setup.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT OR REPLACE INTO "__EFMigrationsLock" ("Id", "Timestamp")
                    VALUES (1, {DateTimeOffset.UtcNow.AddMinutes(-10).ToString("O")});
                    """);
            }

            var logs = new List<string>();

            await using (var db = new AppDbContext(options))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await DatabaseMigrationBridge.UpgradeAsync(db, timeout.Token, logs.Add);

                var applied = await db.Database.GetAppliedMigrationsAsync(timeout.Token);
                CollectionAssert.Contains(applied.ToList(), AiMigration);
            }

            Assert.IsTrue(
                logs.Any(x => x.Contains("Recovered an abandoned", StringComparison.Ordinal)),
                "Expected abandoned migration lock recovery to be logged.");

            await using var verify = new SqliteConnection($"Data Source={databasePath}");
            await verify.OpenAsync();

            await using var tableCommand = verify.CreateCommand();
            tableCommand.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsLock';";
            var tableExists = Convert.ToInt32(await tableCommand.ExecuteScalarAsync()) > 0;

            if (tableExists)
            {
                await using var lockCommand = verify.CreateCommand();
                lockCommand.CommandText = """SELECT COUNT(*) FROM "__EFMigrationsLock";""";
                Assert.AreEqual(0L, Convert.ToInt64(await lockCommand.ExecuteScalarAsync()));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    public async Task RecentSqliteMigrationLockFailsFastInsteadOfWaitingIndefinitely()
    {
        var databasePath = CreateDatabasePath();

        try
        {
            var options = CreateOptions(databasePath);

            await using (var setup = new AppDbContext(options))
            {
                var migrator = setup.GetService<IMigrator>();
                await migrator.MigrateAsync(PreAiMigration);

                await setup.Database.ExecuteSqlRawAsync(
                    """
                    CREATE TABLE IF NOT EXISTS "__EFMigrationsLock" (
                        "Id" INTEGER NOT NULL CONSTRAINT "PK___EFMigrationsLock" PRIMARY KEY,
                        "Timestamp" TEXT NOT NULL
                    );
                    """);

                await setup.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT OR REPLACE INTO "__EFMigrationsLock" ("Id", "Timestamp")
                    VALUES (1, {DateTimeOffset.UtcNow.ToString("O")});
                    """);
            }

            await using var db = new AppDbContext(options);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => DatabaseMigrationBridge.UpgradeAsync(db, timeout.Token));

            StringAssert.Contains(exception.Message, "one application container");
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
            $"anilingo-migration-{Guid.NewGuid():N}.db");
}
