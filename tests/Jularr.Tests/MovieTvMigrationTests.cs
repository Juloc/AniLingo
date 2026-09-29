using Jularr.Web.Features.Acquisition.Access;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Jularr.Tests;

/// <summary>
/// The MovieTvFoundation migration: the new per-type tables exist, the retired UserAddMode column is gone,
/// and the acquisition tables now accept the movie/tv kinds (#593/#594). The test database is PostgreSQL
/// (the canonical provider), so these assertions cover the migration's provider-specific schema changes.
/// </summary>
[TestClass]
public sealed class MovieTvMigrationTests
{
    [TestMethod]
    public async Task NewMediaTablesExist()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        Assert.AreEqual(0, await db.Movies.CountAsync());
        Assert.AreEqual(0, await db.TvSeries.CountAsync());
    }

    [TestMethod]
    public async Task UserAddModeColumnIsDropped()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'AcquisitionAccessPolicies' AND column_name = 'UserAddMode';";
            Assert.AreEqual(0L, (long)(await command.ExecuteScalarAsync())!);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    [TestMethod]
    public async Task AccessPoliciesAcceptMovieAndTvKinds()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        var store = new AcquisitionAccessStore(db);

        await store.SavePolicyAsync(new AcquisitionAccessPolicy(MediaAcquisitionKind.Movie, ManualAddMode.Users), CancellationToken.None);
        await store.SavePolicyAsync(new AcquisitionAccessPolicy(MediaAcquisitionKind.Tv, ManualAddMode.Users), CancellationToken.None);

        Assert.AreEqual(ManualAddMode.Users, (await store.GetPolicyAsync(MediaAcquisitionKind.Movie, CancellationToken.None)).Manual);
        Assert.AreEqual(ManualAddMode.Users, (await store.GetPolicyAsync(MediaAcquisitionKind.Tv, CancellationToken.None)).Manual);
    }
}
