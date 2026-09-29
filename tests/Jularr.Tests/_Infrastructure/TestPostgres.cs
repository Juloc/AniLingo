using Jularr.Web.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Jularr.Tests.Infrastructure;

/// <summary>
/// Backs the whole test suite with a single real PostgreSQL database (issue #570). The former
/// SQLite tests each built their own throwaway <c>.db</c> file; here every distinct SQLite-style
/// connection string a test builds is mapped to one shared PostgreSQL database that is truncated
/// back to empty whenever a test moves on to a new logical database. MSTest runs tests serially,
/// so one shared database with a reset between logical databases gives the same isolation the
/// per-file model gave, without creating (and leaking) thousands of databases on limited disk.
///
/// The base server comes from the <c>JULARR_TEST_DB</c> environment variable
/// (e.g. <c>Host=localhost;Port=5433;Username=jularr;Password=…</c>); a throwaway ephemeral
/// <c>postgres:16</c> container is the intended target. Never point this at production data.
/// </summary>
public static class TestPostgres
{
    private const string DatabaseName = "jularr_test";
    private static readonly object Gate = new();
    private static bool _initialized;
    private static string _sharedConnectionString = string.Empty;
    private static string? _currentKey;
    private static string[]? _dataTables;

    private static string BaseConnectionString =>
        Environment.GetEnvironmentVariable("JULARR_TEST_DB")
        ?? "Host=localhost;Port=5433;Username=jularr;Password=devtest;Include Error Detail=true";

    /// <summary>Connection string to the shared, migrated test database.</summary>
    public static string SharedConnectionString
    {
        get
        {
            EnsureInitialized();
            return _sharedConnectionString;
        }
    }

    /// <summary>
    /// Called by the <c>UseSqlite</c> test shim. Returns the shared PostgreSQL connection string,
    /// resetting the database to empty when the caller has switched to a different logical
    /// database (a different SQLite "Data Source"), which mirrors starting a fresh <c>.db</c> file.
    /// </summary>
    public static string ResolveConnectionString(string sqliteConnectionString)
    {
        EnsureInitialized();
        var key = KeyFor(sqliteConnectionString);
        lock (Gate)
        {
            // A caller that passes the shared PostgreSQL string itself (e.g. reusing
            // Database.GetConnectionString()) means "keep using the current database".
            if (key is not null && key != _currentKey)
            {
                ResetData();
                _currentKey = key;
            }
        }

        return _sharedConnectionString;
    }

    private static string? KeyFor(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)
            || connectionString.Contains(DatabaseName, StringComparison.Ordinal))
        {
            // Reusing the shared database explicitly: do not reset.
            return null;
        }

        // SQLite style: "Data Source=<path>;...". Key on the data-source path so that several
        // contexts opened against the same path within one test share state (as the file did).
        var builder = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connectionString };
        if (builder.TryGetValue("Data Source", out var dataSource) && dataSource is string ds && !string.IsNullOrWhiteSpace(ds))
        {
            return ds.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
                ? Guid.NewGuid().ToString("N")
                : ds;
        }

        return connectionString;
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            var maintenance = new NpgsqlConnectionStringBuilder(BaseConnectionString) { Database = "postgres" }
                .ConnectionString;
            using (var admin = new NpgsqlConnection(maintenance))
            {
                admin.Open();
                Execute(admin, $"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE);");
                Execute(admin, $"CREATE DATABASE \"{DatabaseName}\";");
            }

            _sharedConnectionString = new NpgsqlConnectionStringBuilder(BaseConnectionString)
            {
                Database = DatabaseName
            }.ConnectionString;

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_sharedConnectionString)
                .Options;
            using (var db = new AppDbContext(options))
            {
                db.Database.Migrate();
            }

            _dataTables = LoadDataTables();
            _initialized = true;
        }
    }

    private static void ResetData()
    {
        if (_dataTables is null || _dataTables.Length == 0)
        {
            return;
        }

        using var conn = new NpgsqlConnection(_sharedConnectionString);
        conn.Open();
        var list = string.Join(", ", _dataTables.Select(t => $"\"{t}\""));
        Execute(conn, $"TRUNCATE TABLE {list} RESTART IDENTITY CASCADE;");
    }

    private static string[] LoadDataTables()
    {
        using var conn = new NpgsqlConnection(_sharedConnectionString);
        conn.Open();
        using var command = conn.CreateCommand();
        command.CommandText =
            "SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory';";
        var tables = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables.ToArray();
    }

    private static void Execute(NpgsqlConnection conn, string sql)
    {
        using var command = conn.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
