using Jularr.Tests.Infrastructure;

// The PostgreSQL cutover (#570) removed the SQLite EF provider, so the EF Core UseSqlite extension
// no longer exists. The test suite was 100% SQLite (UseSqlite(...) at ~130 call sites). Rather than
// edit every call site, this shim lives in the SAME namespace the tests already import
// (Microsoft.EntityFrameworkCore), so the existing UseSqlite calls compile and transparently target
// the shared test PostgreSQL database (see TestPostgres). New tests should use UseNpgsql directly.
//
// Microsoft.Data.Sqlite itself is still referenced (the one-time importer reads a legacy SQLite
// file), so SqliteConnection / SqliteConnection.ClearAllPools() resolve to the real type and need
// no shim.

namespace Microsoft.EntityFrameworkCore
{
    /// <summary>Redirects the tests' <c>UseSqlite(...)</c> calls to the shared test PostgreSQL database.</summary>
    public static class SqliteTestShimExtensions
    {
        public static DbContextOptionsBuilder<TContext> UseSqlite<TContext>(
            this DbContextOptionsBuilder<TContext> builder,
            string? connectionString)
            where TContext : DbContext =>
            builder.UseNpgsql(TestPostgres.ResolveConnectionString(connectionString));

        public static DbContextOptionsBuilder UseSqlite(
            this DbContextOptionsBuilder builder,
            string? connectionString) =>
            builder.UseNpgsql(TestPostgres.ResolveConnectionString(connectionString));
    }
}
