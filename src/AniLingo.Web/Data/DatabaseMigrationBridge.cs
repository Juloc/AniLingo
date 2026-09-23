using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Data;

public static class DatabaseMigrationBridge
{
    public const string Epoch2BaselineMigration = "20260922153000_InitialEpoch2Schema";
    private const string ProductVersion = "10.0.12";
    private const string MigrationLockTable = "__EFMigrationsLock";
    private static readonly TimeSpan StaleMigrationLockAge = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MigrationTimeout = TimeSpan.FromMinutes(2);

    private static readonly string[] Epoch2Tables =
    [
        "Anime",
        "Episodes",
        "EpisodeTerms",
        "LibraryRoots",
        "MediaFiles",
        "Reviews",
        "SubtitleCues",
        "SubtitleTracks",
        "Terms",
        "UserTerms"
    ];

    public static async Task UpgradeAsync(
        AppDbContext db,
        CancellationToken cancellationToken = default,
        Action<string>? log = null)
    {
        log?.Invoke("Inspecting SQLite migration state.");

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var existingTables = await ReadTablesAsync(connection, cancellationToken);

            if (existingTables.Contains(MigrationLockTable, StringComparer.Ordinal))
            {
                await RecoverAbandonedMigrationLockAsync(
                    db,
                    connection,
                    log,
                    cancellationToken);
            }

            var hasHistory = existingTables.Contains("__EFMigrationsHistory", StringComparer.Ordinal);

            if (!hasHistory && Epoch2Tables.All(
                    table => existingTables.Contains(table, StringComparer.Ordinal)))
            {
                log?.Invoke("Bridging the pre-migration AniLingo database into the EF migration baseline.");

                await db.Database.ExecuteSqlRawAsync(
                    """
                    CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                        "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                        "ProductVersion" TEXT NOT NULL
                    );
                    """,
                    cancellationToken);

                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                    VALUES ({Epoch2BaselineMigration}, {ProductVersion});
                    """,
                    cancellationToken);
            }
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }

        log?.Invoke("Applying pending EF Core migrations.");

        using var migrationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        migrationCts.CancelAfter(MigrationTimeout);

        try
        {
            await db.Database.MigrateAsync(migrationCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"SQLite migration did not finish within {MigrationTimeout.TotalSeconds:0} seconds. " +
                "Check Docker logs for another AniLingo instance or a database lock.");
        }

        await MigrateLegacyLearningProfileAsync(db, cancellationToken, log);

        log?.Invoke("Database migrations are complete.");
    }

    private static async Task MigrateLegacyLearningProfileAsync(
        AppDbContext db,
        CancellationToken cancellationToken,
        Action<string>? log)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var userTermColumns = await ReadColumnsAsync(connection, "UserTerms", cancellationToken);
            var reviewColumns = await ReadColumnsAsync(connection, "Reviews", cancellationToken);
            var preferenceColumns = await ReadColumnsAsync(connection, "LearningPreferences", cancellationToken);
            var ownerColumns = await ReadColumnsAsync(connection, "OwnerAccounts", cancellationToken);

            if (!ownerColumns.Contains("Id"))
            {
                return;
            }

            var ownerExists = await ScalarLongAsync(
                connection,
                "SELECT COUNT(*) FROM OwnerAccounts WHERE Id = 'owner';",
                cancellationToken) > 0;

            if (!ownerExists)
            {
                return;
            }

            var migrated = 0;

            if (userTermColumns.Contains("ProfileId") && userTermColumns.Contains("TermId"))
            {
                migrated += await ExecuteAsync(
                    connection,
                    """
                    DELETE FROM UserTerms
                    WHERE ProfileId = 'default'
                      AND EXISTS (
                        SELECT 1
                        FROM UserTerms AS existing
                        WHERE existing.ProfileId = 'owner'
                          AND existing.TermId = UserTerms.TermId
                      );

                    UPDATE UserTerms
                    SET ProfileId = 'owner'
                    WHERE ProfileId = 'default';
                    """,
                    cancellationToken);
            }

            if (reviewColumns.Contains("ProfileId"))
            {
                migrated += await ExecuteAsync(
                    connection,
                    """
                    UPDATE Reviews
                    SET ProfileId = 'owner'
                    WHERE ProfileId = 'default';
                    """,
                    cancellationToken);
            }

            if (preferenceColumns.Contains("ProfileId"))
            {
                migrated += await ExecuteAsync(
                    connection,
                    """
                    DELETE FROM LearningPreferences
                    WHERE ProfileId = 'default'
                      AND EXISTS (
                        SELECT 1
                        FROM LearningPreferences AS existing
                        WHERE existing.ProfileId = 'owner'
                      );

                    UPDATE LearningPreferences
                    SET ProfileId = 'owner'
                    WHERE ProfileId = 'default';
                    """,
                    cancellationToken);
            }

            if (migrated > 0)
            {
                log?.Invoke("Migrated legacy learning profile data to the owner account.");
            }
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<HashSet<string>> ReadColumnsAsync(
        System.Data.Common.DbConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table.Replace("\"", "\"\"")}\");";

        var names = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(1))
            {
                names.Add(reader.GetString(1));
            }
        }

        return names;
    }

    private static async Task<long> ScalarLongAsync(
        System.Data.Common.DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task<int> ExecuteAsync(
        System.Data.Common.DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RecoverAbandonedMigrationLockAsync(
        AppDbContext db,
        System.Data.Common.DbConnection connection,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT "Timestamp"
            FROM "__EFMigrationsLock"
            WHERE "Id" = 1
            LIMIT 1;
            """;

        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null or DBNull)
        {
            return;
        }

        var rawTimestamp = Convert.ToString(value, CultureInfo.InvariantCulture);
        var timestampValid = DateTimeOffset.TryParse(
            rawTimestamp,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out var lockTimestamp);

        if (timestampValid)
        {
            var age = DateTimeOffset.UtcNow - lockTimestamp.ToUniversalTime();
            if (age < StaleMigrationLockAge)
            {
                var remaining = StaleMigrationLockAge - age;
                throw new InvalidOperationException(
                    "A recent SQLite migration lock exists. AniLingo supports one application container per /data volume. " +
                    $"Another instance may still be migrating; retry in about {Math.Ceiling(remaining.TotalSeconds)} seconds.");
            }
        }

        var removed = await db.Database.ExecuteSqlRawAsync(
            """DELETE FROM "__EFMigrationsLock";""",
            cancellationToken);

        if (removed > 0)
        {
            var ageText = timestampValid
                ? $"{Math.Max(0, (DateTimeOffset.UtcNow - lockTimestamp.ToUniversalTime()).TotalSeconds):0}s old"
                : "with an unreadable timestamp";

            log?.Invoke($"Recovered an abandoned EF Core SQLite migration lock ({ageText}).");
        }
    }

    private static async Task<HashSet<string>> ReadTablesAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table';";

        var names = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                names.Add(reader.GetString(0));
            }
        }

        return names;
    }
}
