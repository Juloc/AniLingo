using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AniLingo.Tests;

[TestClass]
public sealed class LearningConfigurationTests
{
    private const string MigrationId = "20260925122000_AddLearningV2Settings";

    [TestMethod]
    public async Task NewProfileDefaultsToOff()
    {
        await using var fixture = await Fixture.CreateAsync();

        var mode = await fixture.Store.GetProfileModeAsync(
            "new-user",
            CancellationToken.None);

        var resolved = await fixture.Store.ResolveAsync(
            "new-user",
            new LearningScopeContext(LearningMediaType.Anime),
            CancellationToken.None);

        Assert.AreEqual(LearningMode.Off, mode);
        Assert.IsFalse(resolved.HasAnyVisibleLearning);
        Assert.IsFalse(resolved.IsEnabled(LearningCapability.Reviews));
    }

    [TestMethod]
    public async Task ExistingLearnerIsSeededAsStudyDuringMigration()
    {
        await using var fixture = await Fixture.CreateBeforeLearningV2Async();

        var term = new Term
        {
            Id = Guid.NewGuid(),
            Language = "ja",
            Canonical = "食べる",
            Reading = "たべる",
            Meaning = "to eat"
        };
        fixture.Db.Terms.Add(term);
        fixture.Db.UserTerms.Add(new UserTerm
        {
            ProfileId = "legacy-user",
            TermId = term.Id,
            State = UserTermState.Learning,
            UpdatedAt = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        await fixture.Db.GetService<IMigrator>().MigrateAsync();

        var mode = await fixture.Store.GetProfileModeAsync(
            "legacy-user",
            CancellationToken.None);

        Assert.AreEqual(LearningMode.Study, mode);
    }

    [TestMethod]
    public async Task NearestScopeModeWins()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string profile = "reader";

        await fixture.Store.SetModeAsync(
            profile,
            LearningScopeRef.Profile,
            LearningMode.Off,
            CancellationToken.None);
        await fixture.Store.SetModeAsync(
            profile,
            LearningScopeRef.ForMedia(LearningMediaType.Novel),
            LearningMode.LanguageTools,
            CancellationToken.None);
        await fixture.Store.SetModeAsync(
            profile,
            LearningScopeRef.ForWork(LearningMediaType.Novel, "work-1"),
            LearningMode.Study,
            CancellationToken.None);
        await fixture.Store.SetModeAsync(
            profile,
            LearningScopeRef.ForContent(LearningMediaType.Novel, "chapter-7"),
            LearningMode.Off,
            CancellationToken.None);

        var media = await fixture.Store.ResolveAsync(
            profile,
            new LearningScopeContext(LearningMediaType.Novel),
            CancellationToken.None);
        var work = await fixture.Store.ResolveAsync(
            profile,
            new LearningScopeContext(
                LearningMediaType.Novel,
                WorkKey: "work-1"),
            CancellationToken.None);
        var content = await fixture.Store.ResolveAsync(
            profile,
            new LearningScopeContext(
                LearningMediaType.Novel,
                WorkKey: "work-1",
                ContentKey: "chapter-7"),
            CancellationToken.None);

        Assert.AreEqual(LearningMode.LanguageTools, media.Mode);
        Assert.IsTrue(media.IsEnabled(LearningCapability.LanguageLookup));
        Assert.IsFalse(media.IsEnabled(LearningCapability.Reviews));

        Assert.AreEqual(LearningMode.Study, work.Mode);
        Assert.IsTrue(work.IsEnabled(LearningCapability.Reviews));

        Assert.AreEqual(LearningMode.Off, content.Mode);
        Assert.IsFalse(content.IsEnabled(LearningCapability.LanguageLookup));
    }

    [TestMethod]
    public async Task CapabilityOverridesApplyFromBroadToSpecific()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string profile = "reader";

        await fixture.Store.SetModeAsync(
            profile,
            LearningScopeRef.Profile,
            LearningMode.Study,
            CancellationToken.None);

        // Study deliberately keeps the Home widget quiet unless requested.
        var defaults = await fixture.Store.ResolveAsync(
            profile,
            new LearningScopeContext(LearningMediaType.Anime),
            CancellationToken.None);
        Assert.IsFalse(defaults.IsEnabled(LearningCapability.HomeWidget));

        await fixture.Store.SetCapabilityOverrideAsync(
            profile,
            LearningScopeRef.Profile,
            LearningCapability.HomeWidget,
            true,
            CancellationToken.None);
        await fixture.Store.SetCapabilityOverrideAsync(
            profile,
            LearningScopeRef.ForMedia(LearningMediaType.Anime),
            LearningCapability.HomeWidget,
            false,
            CancellationToken.None);
        await fixture.Store.SetCapabilityOverrideAsync(
            profile,
            LearningScopeRef.ForWork(LearningMediaType.Anime, "anime-1"),
            LearningCapability.HomeWidget,
            true,
            CancellationToken.None);

        var anime = await fixture.Store.ResolveAsync(
            profile,
            new LearningScopeContext(LearningMediaType.Anime),
            CancellationToken.None);
        var work = await fixture.Store.ResolveAsync(
            profile,
            new LearningScopeContext(
                LearningMediaType.Anime,
                WorkKey: "anime-1"),
            CancellationToken.None);
        var book = await fixture.Store.ResolveAsync(
            profile,
            new LearningScopeContext(LearningMediaType.Book),
            CancellationToken.None);

        Assert.IsFalse(anime.IsEnabled(LearningCapability.HomeWidget));
        Assert.IsTrue(work.IsEnabled(LearningCapability.HomeWidget));
        Assert.IsTrue(book.IsEnabled(LearningCapability.HomeWidget));
    }

    [TestMethod]
    public async Task LowerScopeCanEnableStudyWhileProfileIsOff()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string profile = "reader";

        await fixture.Store.SetModeAsync(
            profile,
            LearningScopeRef.Profile,
            LearningMode.Off,
            CancellationToken.None);
        await fixture.Store.SetModeAsync(
            profile,
            LearningScopeRef.ForWork(LearningMediaType.Book, "book-42"),
            LearningMode.Study,
            CancellationToken.None);

        var defaultBook = await fixture.Store.ResolveAsync(
            profile,
            new LearningScopeContext(LearningMediaType.Book),
            CancellationToken.None);
        var selectedBook = await fixture.Store.ResolveAsync(
            profile,
            new LearningScopeContext(
                LearningMediaType.Book,
                WorkKey: "book-42"),
            CancellationToken.None);

        Assert.AreEqual(LearningMode.Off, defaultBook.Mode);
        Assert.AreEqual(LearningMode.Study, selectedBook.Mode);
        Assert.IsTrue(selectedBook.IsEnabled(LearningCapability.Reviews));
        Assert.IsTrue(await fixture.Store.HasAnyLearningEnabledAsync(
            profile,
            CancellationToken.None));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            string directory,
            AppDbContext db)
        {
            Directory = directory;
            Db = db;
            Store = new LearningConfigurationStore(db);
        }

        public string Directory { get; }
        public AppDbContext Db { get; }
        public LearningConfigurationStore Store { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = Create();
            await DatabaseMigrationBridge.UpgradeAsync(fixture.Db);
            return fixture;
        }

        public static async Task<Fixture> CreateBeforeLearningV2Async()
        {
            var fixture = Create();
            var migrations = fixture.Db.Database
                .GetMigrations()
                .ToArray();
            var migrationIndex = Array.IndexOf(migrations, MigrationId);
            Assert.IsTrue(
                migrationIndex > 0,
                $"Expected {MigrationId} after at least one existing migration.");

            await fixture.Db.GetService<IMigrator>()
                .MigrateAsync(migrations[migrationIndex - 1]);

            return fixture;
        }

        private static Fixture Create()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"anilingo-learning-v2-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(
                    $"Data Source={Path.Combine(directory, "anilingo.db")};Foreign Keys=True")
                .Options;

            return new Fixture(
                directory,
                new AppDbContext(options));
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            SqliteConnection.ClearAllPools();

            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }
}
