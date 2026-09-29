using Jularr.Web.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Jularr.Tests.Infrastructure;

/// <summary>
/// Backs the whole test suite with real PostgreSQL (issue #570). The former SQLite tests each built
/// their own throwaway <c>.db</c> file; here every distinct SQLite-style "Data Source" a test builds
/// is mapped to its own PostgreSQL database, cloned from a once-migrated template. That reproduces
/// the old file-per-test isolation exactly — including tests that open two independent databases at
/// once — while a small LRU keeps the number of live databases (and disk use) bounded.
///
/// The base server comes from the <c>JULARR_TEST_DB</c> environment variable
/// (e.g. <c>Host=localhost;Port=5433;Username=jularr;Password=…</c>); a throwaway ephemeral
/// <c>postgres:16</c> container is the intended target. Never point this at production data.
/// </summary>
public static class TestPostgres
{
    private const string TemplateDatabase = "jularr_test_tpl";
    private const int MaxLiveDatabases = 32;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, string> KeyToDatabase = new(StringComparer.Ordinal);
    private static readonly LinkedList<string> Lru = new();
    private static bool _initialized;
    private static string _baseConnectionString = string.Empty;
    private static int _counter;

    private static string BaseConnectionString =>
        Environment.GetEnvironmentVariable("JULARR_TEST_DB")
        ?? "Host=localhost;Port=5433;Username=jularr;Password=devtest;Include Error Detail=true";

    /// <summary>
    /// Called by the <c>UseSqlite</c> test shim. Returns a PostgreSQL connection string for a database
    /// dedicated to the caller's logical database (its SQLite "Data Source" path); the same path
    /// always maps to the same database, distinct paths to distinct databases.
    /// </summary>
    public static string ResolveConnectionString(string? connectionString)
    {
        // A null/blank source (rare) behaves like a private in-memory database.
        connectionString ??= "Data Source=:memory:";

        // A caller reusing an already-resolved PostgreSQL connection string (e.g. from
        // Database.GetConnectionString()) keeps using that same database.
        if (connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        var key = KeyFor(connectionString);
        lock (Gate)
        {
            EnsureInitialized();

            if (KeyToDatabase.TryGetValue(key, out var existing))
            {
                Touch(key);
                return ConnectionFor(existing);
            }

            while (KeyToDatabase.Count >= MaxLiveDatabases && Lru.First is { } oldest)
            {
                Lru.RemoveFirst();
                if (KeyToDatabase.Remove(oldest.Value, out var evicted))
                {
                    DropDatabase(evicted);
                }
            }

            var database = $"jt_{Interlocked.Increment(ref _counter)}";
            CreateFromTemplate(database);
            KeyToDatabase[key] = database;
            Lru.AddLast(key);
            return ConnectionFor(database);
        }
    }

    private static string KeyFor(string connectionString)
    {
        var builder = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connectionString };
        if (builder.TryGetValue("Data Source", out var value) && value is string ds && !string.IsNullOrWhiteSpace(ds))
        {
            // SQLite ":memory:" is private per connection; give each use its own database.
            return ds.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
                ? "mem-" + Guid.NewGuid().ToString("N")
                : ds;
        }

        return connectionString;
    }

    private static void Touch(string key)
    {
        var node = Lru.Find(key);
        if (node is not null)
        {
            Lru.Remove(node);
            Lru.AddLast(node);
        }
    }

    private static string ConnectionFor(string database) =>
        new NpgsqlConnectionStringBuilder(BaseConnectionString) { Database = database }.ConnectionString;

    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        _baseConnectionString = BaseConnectionString;
        var maintenance = new NpgsqlConnectionStringBuilder(_baseConnectionString) { Database = "postgres" }
            .ConnectionString;

        // Drop any databases left by a previous run, then build a freshly migrated template.
        using (var admin = new NpgsqlConnection(maintenance))
        {
            admin.Open();
            foreach (var stale in StaleDatabases(admin))
            {
                Execute(admin, $"DROP DATABASE IF EXISTS \"{stale}\" WITH (FORCE);");
            }

            Execute(admin, $"DROP DATABASE IF EXISTS \"{TemplateDatabase}\" WITH (FORCE);");
            Execute(admin, $"CREATE DATABASE \"{TemplateDatabase}\";");
        }

        var templateConnection = new NpgsqlConnectionStringBuilder(_baseConnectionString) { Database = TemplateDatabase }
            .ConnectionString;
        using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                   .UseNpgsql(templateConnection).Options))
        {
            db.Database.Migrate();
        }

        NpgsqlConnection.ClearAllPools();
        _initialized = true;
    }

    private static void CreateFromTemplate(string database)
    {
        var maintenance = new NpgsqlConnectionStringBuilder(_baseConnectionString) { Database = "postgres" }
            .ConnectionString;
        using var admin = new NpgsqlConnection(maintenance);
        admin.Open();
        Execute(admin, $"CREATE DATABASE \"{database}\" TEMPLATE \"{TemplateDatabase}\";");
    }

    private static void DropDatabase(string database)
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(ConnectionFor(database)));
        var maintenance = new NpgsqlConnectionStringBuilder(_baseConnectionString) { Database = "postgres" }
            .ConnectionString;
        using var admin = new NpgsqlConnection(maintenance);
        admin.Open();
        Execute(admin, $"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE);");
    }

    private static IReadOnlyList<string> StaleDatabases(NpgsqlConnection admin)
    {
        using var command = admin.CreateCommand();
        command.CommandText = "SELECT datname FROM pg_database WHERE datname LIKE 'jt\\_%';";
        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static void Execute(NpgsqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
