using Microsoft.Data.Sqlite;

namespace Jularr.Web.Data;

/// <summary>
/// One-time upgrade step for the AniLingo → Jularr rename (#333): installations created before the
/// rename keep their SQLite database as <c>anilingo.db</c> next to the configured <c>jularr.db</c>.
/// Before EF Core opens the database, the legacy file and its WAL/SHM companions are moved to the
/// configured name, so the data volume keeps working unchanged.
/// </summary>
public static class LegacyDatabaseFileMigration
{
    public const string LegacyFileName = "anilingo.db";

    // Companions first and the database last: if the process stops mid-way, the next start still
    // sees the legacy database and finishes the move, and committed WAL pages are never separated
    // from their database.
    private static readonly string[] MoveOrder = ["-wal", "-shm", ""];

    /// <returns>The migrated database path, or <c>null</c> when nothing had to be moved.</returns>
    public static string? Run(string connectionString, Action<string> log)
    {
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (string.IsNullOrWhiteSpace(dataSource)
            || dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var target = Path.GetFullPath(dataSource);
        if (Path.GetFileName(target).Equals(LegacyFileName, StringComparison.OrdinalIgnoreCase))
        {
            // Explicitly configured to the legacy name: nothing to rename.
            return null;
        }

        var legacy = Path.Combine(Path.GetDirectoryName(target)!, LegacyFileName);
        if (!File.Exists(legacy))
        {
            return null;
        }

        if (File.Exists(target))
        {
            log($"Both '{legacy}' and '{target}' exist; using '{target}' and leaving the legacy database untouched.");
            return null;
        }

        foreach (var suffix in MoveOrder)
        {
            if (File.Exists(legacy + suffix))
            {
                File.Move(legacy + suffix, target + suffix);
            }
        }

        log($"Renamed legacy database '{legacy}' to '{target}'.");
        return target;
    }
}
