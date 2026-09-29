using Jularr.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Tests;

/// <summary>
/// Shared setup for the universal media core tests (#592): an isolated PostgreSQL-backed
/// <see cref="AppDbContext"/> per test (the <c>UseSqlite</c> shim maps to a dedicated test database),
/// migrated to the current schema. Mirrors the established fixture pattern used across the suite.
/// </summary>
internal static class MediaCoreTestSupport
{
    public static async Task<AppDbContext> CreateDbAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source=media-core-{Guid.NewGuid():N}.db;Foreign Keys=True")
            .Options);
        await DatabaseMigrationBridge.UpgradeAsync(db);
        return db;
    }
}
