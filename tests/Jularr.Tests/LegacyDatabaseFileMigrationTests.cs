using Jularr.Web.Data;

namespace Jularr.Tests;

[TestClass]
public sealed class LegacyDatabaseFileMigrationTests
{
    [TestMethod]
    public void MovesLegacyDatabaseAndCompanionsToConfiguredName()
    {
        using var directory = new TempDirectory();
        directory.Write("anilingo.db", "db");
        directory.Write("anilingo.db-wal", "wal");
        directory.Write("anilingo.db-shm", "shm");

        var migrated = LegacyDatabaseFileMigration.Run(directory.ConnectionString("jularr.db"), _ => { });

        Assert.AreEqual(directory.PathOf("jularr.db"), migrated);
        Assert.AreEqual("db", directory.Read("jularr.db"));
        Assert.AreEqual("wal", directory.Read("jularr.db-wal"));
        Assert.AreEqual("shm", directory.Read("jularr.db-shm"));
        Assert.IsFalse(File.Exists(directory.PathOf("anilingo.db")));
        Assert.IsFalse(File.Exists(directory.PathOf("anilingo.db-wal")));
    }

    [TestMethod]
    public void FinishesAMoveThatStoppedAfterTheCompanions()
    {
        using var directory = new TempDirectory();
        directory.Write("anilingo.db", "db");
        directory.Write("jularr.db-wal", "wal");

        LegacyDatabaseFileMigration.Run(directory.ConnectionString("jularr.db"), _ => { });

        Assert.AreEqual("db", directory.Read("jularr.db"));
        Assert.AreEqual("wal", directory.Read("jularr.db-wal"));
    }

    [TestMethod]
    public void LeavesBothFilesAloneWhenTheNewDatabaseAlreadyExists()
    {
        using var directory = new TempDirectory();
        directory.Write("anilingo.db", "old");
        directory.Write("jularr.db", "new");
        var messages = new List<string>();

        Assert.IsNull(LegacyDatabaseFileMigration.Run(directory.ConnectionString("jularr.db"), messages.Add));

        Assert.AreEqual("old", directory.Read("anilingo.db"));
        Assert.AreEqual("new", directory.Read("jularr.db"));
        Assert.AreEqual(1, messages.Count);
    }

    [TestMethod]
    public void DoesNothingForFreshInstallsOrAnExplicitLegacyPath()
    {
        using var directory = new TempDirectory();
        Assert.IsNull(LegacyDatabaseFileMigration.Run(directory.ConnectionString("jularr.db"), _ => { }));

        directory.Write("anilingo.db", "db");
        Assert.IsNull(LegacyDatabaseFileMigration.Run(directory.ConnectionString("anilingo.db"), _ => { }));
        Assert.AreEqual("db", directory.Read("anilingo.db"));
        Assert.IsNull(LegacyDatabaseFileMigration.Run("Data Source=:memory:", _ => { }));
    }

    private sealed class TempDirectory : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("jularr-db-rename-").FullName;

        public string PathOf(string name) => Path.Combine(_root, name);
        public string ConnectionString(string name) => $"Data Source={PathOf(name)};Foreign Keys=True";
        public void Write(string name, string content) => File.WriteAllText(PathOf(name), content);
        public string Read(string name) => File.ReadAllText(PathOf(name));
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
