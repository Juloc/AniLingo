using System.Data;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Data;

public static class DatabaseMigrationBridge
{
    public const string Epoch2BaselineMigration = "20260922153000_InitialEpoch2Schema";
    private const string ProductVersion = "10.0.12";

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
        CancellationToken cancellationToken = default)
    {
        await using var connection = db.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var existingTables = await ReadTablesAsync(connection, cancellationToken);
        var hasHistory = existingTables.Contains("__EFMigrationsHistory", StringComparer.Ordinal);

        if (!hasHistory && Epoch2Tables.All(
                table => existingTables.Contains(table, StringComparer.Ordinal)))
        {
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

        await db.Database.MigrateAsync(cancellationToken);
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
