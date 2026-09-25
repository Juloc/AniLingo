using AniLingo.Web.Data;
using AniLingo.Web.Features.Operations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AniLingo.Tests;

[TestClass]
public sealed class OperationRunnerTests
{
    [TestMethod]
    public async Task RunAsyncRecordsProgressAndSuccess()
    {
        var root = TempDirectory();
        var database = Path.Combine(root, "anilingo.db");

        try
        {
            await using var db = await CreateDatabaseAsync(database);
            var services = new ServiceCollection()
                .AddSingleton(db)
                .BuildServiceProvider();
            var runner = new OperationRunner(db, services);

            var result = await runner.RunAsync(
                new OperationDescriptor(
                    "test-success",
                    "Tests",
                    "Successful operation",
                    ProfileId: "test-profile",
                    Retryable: false),
                async (operation, token) =>
                {
                    await operation.ReportAsync(
                        50,
                        "Half way.",
                        cancellationToken: token);
                    return 42;
                },
                "Completed test operation.",
                CancellationToken.None);

            Assert.AreEqual(42, result);

            var rows = await new OperationStore(db).ListAsync(
                new OperationListFilter(Search: "Successful operation"),
                CancellationToken.None);
            var operation = rows.Single();

            Assert.AreEqual(OperationStatus.Succeeded, operation.Status);
            Assert.AreEqual(100, operation.ProgressPercent);
            Assert.AreEqual("Completed test operation.", operation.Message);
            Assert.AreEqual("test-profile", operation.ProfileId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(root);
        }
    }

    [TestMethod]
    public async Task RunAsyncRecordsFailureAndRethrows()
    {
        var root = TempDirectory();
        var database = Path.Combine(root, "anilingo.db");

        try
        {
            await using var db = await CreateDatabaseAsync(database);
            var services = new ServiceCollection()
                .AddSingleton(db)
                .BuildServiceProvider();
            var runner = new OperationRunner(db, services);

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => runner.RunAsync(
                    new OperationDescriptor(
                        "test-failure",
                        "Tests",
                        "Failing operation",
                        Retryable: false),
                    (_, _) => throw new InvalidOperationException("Expected failure."),
                    cancellationToken: CancellationToken.None));

            var rows = await new OperationStore(db).ListAsync(
                new OperationListFilter(Search: "Failing operation"),
                CancellationToken.None);
            var operation = rows.Single();

            Assert.AreEqual(OperationStatus.Failed, operation.Status);
            StringAssert.Contains(operation.Error, "Expected failure.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(root);
        }
    }

    private static async Task<AppDbContext> CreateDatabaseAsync(string path)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path};Foreign Keys=True")
            .Options;
        var db = new AppDbContext(options);
        await DatabaseMigrationBridge.UpgradeAsync(db);
        return db;
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-operation-runner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
