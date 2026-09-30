using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jularr.Tests.Infrastructure;

[TestClass]
public static class TestPostgresCleanup
{
    /// <summary>Drops this run's template and databases once the whole assembly has finished (#616).</summary>
    [AssemblyCleanup]
    public static void DropRunDatabases() => TestPostgres.DropRunDatabases();
}
