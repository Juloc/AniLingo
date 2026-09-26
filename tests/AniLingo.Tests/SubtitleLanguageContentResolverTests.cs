using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning.Courses;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

// Subtitle acquisition (SubtitleImportService, SubtitleSidecarLocator,
// EmbeddedSubtitleExtractor) resolves the content language it targets through
// LearningContentLanguageResolver - the one place that decides "what language
// is this household acquiring subtitles for" when profiles/courses disagree.
[TestClass]
public sealed class SubtitleLanguageContentResolverTests
{
    [TestMethod]
    public async Task DefaultsToJapaneseWithNoLearningCourses()
    {
        await using var fixture = await Fixture.CreateAsync();

        var language = await fixture.Resolver.ResolveTargetLanguageAsync(CancellationToken.None);

        Assert.AreEqual("ja", language);
    }

    [TestMethod]
    public async Task UsesTheSingleEnabledPrimaryCourseSourceLanguage()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddCourseAsync("reader", "de", "en", enabled: true, primary: true);

        var language = await fixture.Resolver.ResolveTargetLanguageAsync(CancellationToken.None);

        Assert.AreEqual("de", language);
    }

    [TestMethod]
    public async Task IgnoresDisabledAndNonPrimaryCourses()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddCourseAsync("reader", "de", "en", enabled: false, primary: true);
        await fixture.AddCourseAsync("reader", "id", "en", enabled: true, primary: false);

        var language = await fixture.Resolver.ResolveTargetLanguageAsync(CancellationToken.None);

        Assert.AreEqual("ja", language);
    }

    [TestMethod]
    public async Task ConflictingProfilesResolveToTheMostCommonSourceLanguage()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddCourseAsync("reader-one", "de", "en", enabled: true, primary: true);
        await fixture.AddCourseAsync("reader-two", "de", "id", enabled: true, primary: true);
        await fixture.AddCourseAsync("reader-three", "ro", "en", enabled: true, primary: true);

        var language = await fixture.Resolver.ResolveTargetLanguageAsync(CancellationToken.None);

        Assert.AreEqual("de", language);
    }

    [TestMethod]
    public async Task TiedLanguagesResolveToTheOldestConfiguredCourse()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddCourseAsync(
            "reader-one",
            "ro",
            "en",
            enabled: true,
            primary: true,
            createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await fixture.AddCourseAsync(
            "reader-two",
            "de",
            "en",
            enabled: true,
            primary: true,
            createdAt: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var language = await fixture.Resolver.ResolveTargetLanguageAsync(CancellationToken.None);

        Assert.AreEqual("ro", language);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string directory, AppDbContext db)
        {
            Directory = directory;
            Db = db;
            Resolver = new LearningContentLanguageResolver(db);
        }

        public string Directory { get; }
        public AppDbContext Db { get; }
        public LearningContentLanguageResolver Resolver { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"anilingo-content-language-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(
                    $"Data Source={Path.Combine(directory, "anilingo.db")};Foreign Keys=True")
                .Options;

            var fixture = new Fixture(directory, new AppDbContext(options));
            await DatabaseMigrationBridge.UpgradeAsync(fixture.Db);
            return fixture;
        }

        public async Task AddCourseAsync(
            string profileId,
            string sourceLanguage,
            string targetLanguage,
            bool enabled,
            bool primary,
            DateTime? createdAt = null)
        {
            var now = createdAt ?? DateTime.UtcNow;
            Db.LearningCourses.Add(new LearningCourse
            {
                ProfileId = profileId,
                Name = $"{sourceLanguage} → {targetLanguage}",
                SourceLanguage = sourceLanguage,
                TargetLanguage = targetLanguage,
                IsEnabled = enabled,
                IsPrimary = primary,
                CreatedAt = now,
                UpdatedAt = now
            });
            await Db.SaveChangesAsync(CancellationToken.None);
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
