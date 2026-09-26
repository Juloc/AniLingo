using AniLingo.Web.Data;
using AniLingo.Web.Features.Kana;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Learning.Courses;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AniLingo.Tests;

/// <summary>
/// One-time conversion of the legacy Term/UserTerm/Review learning state into
/// directional Learning cards at the 20260926080500_RetireLegacyLearningState boundary.
/// </summary>
[TestClass]
public sealed class LearningStateMigrationTests
{
    private static readonly string CatId = Id("0001");
    private static readonly string DogId = Id("0002");
    private static readonly string KanaId = KanaCatalog.IdFor(KanaCatalog.All[0]).ToString().ToUpperInvariant();
    private static readonly string CatUserTermA = Id("1001");
    private static readonly string DogUserTermA = Id("1002");
    private static readonly string KanaUserTermA = Id("1003");
    private static readonly string CatUserTermB = Id("1004");
    private static readonly string EventA1 = Id("2001");
    private static readonly string EventA2 = Id("2002");

    [TestMethod]
    public async Task LegacyLearningStateIsConvertedOnceAndLegacyTablesAreDropped()
    {
        await using var fixture = await Fixture.CreateBeforeUniversalCoursesAsync();
        await fixture.SeedLegacyAsync(
            """
            INSERT INTO Terms (Id, Language, Canonical, Reading, Meaning) VALUES
                ($cat, 'ja', '猫', 'ねこ', 'Katze'),
                ($dog, 'ja', '犬', 'いぬ', NULL),
                ($kana, 'ja-kana', 'あ', 'a', 'Hiragana · Vokale');

            INSERT INTO UserTerms
                (Id, ProfileId, TermId, State, IntervalDays, NextReviewAt, LearningStartedAt, QueuePosition, UpdatedAt)
            VALUES
                ($catA, 'reader-a', $cat, 2, 7, '2026-09-28 10:00:00', '2026-09-20 10:00:00', 12, '2026-09-25 09:00:00'),
                ($dogA, 'reader-a', $dog, 1, 0, NULL, NULL, NULL, '2026-09-24 09:00:00'),
                ($kanaA, 'reader-a', $kana, 2, 1, '2026-09-26 08:00:00', '2026-09-22 08:00:00', 3, '2026-09-25 08:00:00'),
                ($catB, 'reader-b', $cat, 3, 0, NULL, NULL, NULL, '2026-09-23 09:00:00');

            INSERT INTO Reviews (Id, ProfileId, TermId, Rating, ClientEventId, ReviewedAt, NextReviewAt) VALUES
                (41, 'reader-a', $cat, 1, $eventA1, '2026-09-21 10:00:00', '2026-09-21 10:10:00'),
                (42, 'reader-a', $cat, 3, $eventA2, '2026-09-21 10:10:00', '2026-09-28 10:00:00'),
                (43, 'reader-a', $kana, 3, NULL, '2026-09-25 08:00:00', '2026-09-26 08:00:00'),
                (44, 'reader-b', $dog, 3, NULL, '2026-09-25 08:00:00', '2026-09-26 08:00:00');
            """);

        var logs = new List<string>();
        await DatabaseMigrationBridge.UpgradeAsync(fixture.Db, CancellationToken.None, logs.Add);

        Assert.IsTrue(logs.Any(x => x.Contains("Converted 4 legacy learning item(s) and 3 review(s)", StringComparison.Ordinal)));
        CollectionAssert.IsSubsetOf(
            new[]
            {
                DatabaseMigrationBridge.UniversalLearningCoursesMigration,
                DatabaseMigrationBridge.RetireLegacyLearningStateMigration
            },
            (await fixture.Db.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.AreEqual(0L, await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('UserTerms', 'Reviews');"));

        // Word card: every scheduling field is preserved, the card keeps the UserTerm ID.
        var cat = await fixture.Db.LearningCards.AsNoTracking().SingleAsync(x => x.Id == Guid.Parse(CatUserTermA));
        Assert.AreEqual("reader-a", cat.ProfileId);
        Assert.AreEqual(LearningCardMode.Recognition, cat.Mode);
        Assert.AreEqual(UserTermState.Learning, cat.State);
        Assert.AreEqual(7, cat.IntervalDays);
        Assert.AreEqual(new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc), cat.NextReviewAt);
        Assert.AreEqual(new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc), cat.LearningStartedAt);
        Assert.AreEqual(12L, cat.QueuePosition);
        Assert.AreEqual("ja", cat.PromptLanguage);
        Assert.AreEqual("de", cat.AnswerLanguage);

        var catUnit = await fixture.Db.LearningUnits.AsNoTracking().SingleAsync(x => x.Id == cat.UnitId);
        Assert.AreEqual(Guid.Parse(CatId), catUnit.TermId);
        Assert.AreEqual(LearningUnitKind.Word, catUnit.Kind);
        var catVariants = await fixture.Db.LearningVariants.AsNoTracking()
            .Where(x => x.UnitId == catUnit.Id)
            .ToDictionaryAsync(x => x.LanguageTag);
        Assert.AreEqual("猫", catVariants["ja"].Text);
        Assert.AreEqual("ねこ", catVariants["ja"].Reading);
        Assert.AreEqual("Katze", catVariants["de"].Text);

        // Items without a dictionary meaning are preserved as well.
        var dog = await fixture.Db.LearningCards.AsNoTracking().SingleAsync(x => x.Id == Guid.Parse(DogUserTermA));
        Assert.AreEqual(UserTermState.Known, dog.State);

        // Both profiles share the catalog unit but own separate courses and cards.
        var catB = await fixture.Db.LearningCards.AsNoTracking().SingleAsync(x => x.Id == Guid.Parse(CatUserTermB));
        Assert.AreEqual("reader-b", catB.ProfileId);
        Assert.AreEqual(UserTermState.Saved, catB.State);
        Assert.AreEqual(cat.UnitId, catB.UnitId);
        Assert.AreNotEqual(cat.CourseId, catB.CourseId);

        var coursesA = await fixture.Db.LearningCourses.AsNoTracking()
            .Where(x => x.ProfileId == "reader-a")
            .ToDictionaryAsync(x => x.TargetLanguage);
        Assert.AreEqual(2, coursesA.Count);
        Assert.IsTrue(coursesA["de"].IsPrimary);
        Assert.IsTrue(coursesA["de"].RecognitionEnabled);
        Assert.IsFalse(coursesA["de"].ProductionEnabled);
        Assert.AreEqual(KanaCatalog.CourseName, coursesA["ja-Latn"].Name);
        Assert.IsFalse(coursesA["ja-Latn"].IsPrimary);

        // Kana become Script units in the Kana course and leave the word catalog.
        var kana = await fixture.Db.LearningCards.AsNoTracking().SingleAsync(x => x.Id == Guid.Parse(KanaUserTermA));
        Assert.AreEqual(coursesA["ja-Latn"].Id, kana.CourseId);
        Assert.AreEqual(Guid.Parse(KanaId), kana.UnitId);
        Assert.AreEqual(3L, kana.QueuePosition);
        var kanaUnit = await fixture.Db.LearningUnits.AsNoTracking().SingleAsync(x => x.Id == kana.UnitId);
        Assert.AreEqual(LearningUnitKind.Script, kanaUnit.Kind);
        Assert.IsNull(kanaUnit.TermId);
        CollectionAssert.AreEquivalent(
            new[] { "ja:あ", "ja-Latn:a" },
            await fixture.Db.LearningVariants.AsNoTracking()
                .Where(x => x.UnitId == kanaUnit.Id)
                .Select(x => x.LanguageTag + ":" + x.Text)
                .ToArrayAsync());
        Assert.AreEqual(0, await fixture.Db.Terms.CountAsync(x => x.Language == KanaCatalog.IdNamespace));

        // Review history keeps IDs, ratings, times and offline event IDs; the
        // orphan review without a learning item is not carried over.
        var reviews = await fixture.Db.LearningCardReviews.AsNoTracking()
            .OrderBy(x => x.Id)
            .ToListAsync();
        CollectionAssert.AreEqual(new long[] { 41, 42, 43 }, reviews.Select(x => x.Id).ToArray());
        Assert.AreEqual(cat.Id, reviews[0].CardId);
        Assert.AreEqual(ReviewRating.Again, reviews[0].Rating);
        Assert.AreEqual(Guid.Parse(EventA1), reviews[0].ClientEventId);
        Assert.AreEqual(Guid.Parse(EventA2), reviews[1].ClientEventId);
        Assert.AreEqual(new DateTime(2026, 9, 21, 10, 10, 0, DateTimeKind.Utc), reviews[1].ReviewedAt);
        Assert.AreEqual(kana.Id, reviews[2].CardId);
        Assert.IsTrue(reviews.All(x => x.ProfileId == "reader-a"));

        // Offline events that were synced before the upgrade stay idempotent.
        var service = new LearningService(fixture.Db, new FsrsReviewScheduler(), TestAccounts.Context("reader-a"));
        var replay = await service.SyncOfflineReviewsAsync(
            [new OfflineReviewEvent(Guid.Parse(EventA2), Guid.Parse(CatId), ReviewRating.Good, new DateTime(2026, 9, 21, 10, 10, 0, DateTimeKind.Utc))],
            DateTime.UtcNow,
            CancellationToken.None);
        CollectionAssert.AreEqual(new[] { Guid.Parse(EventA2) }, replay.AlreadyApplied.ToArray());
        Assert.AreEqual(0, replay.Accepted.Count);

        // The term read model and the scheduler use the converted cards directly.
        var catState = await LearningQueries.TermStates(fixture.Db, "reader-a")
            .SingleAsync(x => x.TermId == Guid.Parse(CatId));
        Assert.AreEqual(cat.Id, catState.CardId);

        // A later start is a no-op: nothing is copied twice.
        logs.Clear();
        await DatabaseMigrationBridge.UpgradeAsync(fixture.Db, CancellationToken.None, logs.Add);
        Assert.IsFalse(logs.Any(x => x.Contains("Converted", StringComparison.Ordinal)));
        Assert.AreEqual(4, await fixture.Db.LearningCards.CountAsync());
        Assert.AreEqual(3, await fixture.Db.LearningCardReviews.CountAsync());
    }

    [TestMethod]
    public async Task RetiringLegacyTablesWithoutConversionIsRefused()
    {
        await using var fixture = await Fixture.CreateBeforeUniversalCoursesAsync();
        await fixture.SeedLegacyAsync(
            """
            INSERT INTO Terms (Id, Language, Canonical, Reading, Meaning) VALUES
                ($cat, 'ja', '猫', 'ねこ', 'Katze');
            INSERT INTO UserTerms
                (Id, ProfileId, TermId, State, IntervalDays, NextReviewAt, LearningStartedAt, QueuePosition, UpdatedAt)
            VALUES
                ($catA, 'reader-a', $cat, 2, 7, NULL, NULL, NULL, '2026-09-25 09:00:00');
            """);

        // Applying migrations without the bridge must not silently drop progress.
        await Assert.ThrowsExactlyAsync<SqliteException>(
            () => fixture.Db.GetService<IMigrator>().MigrateAsync());

        Assert.AreEqual(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM UserTerms;"));
        CollectionAssert.DoesNotContain(
            (await fixture.Db.Database.GetAppliedMigrationsAsync()).ToArray(),
            DatabaseMigrationBridge.RetireLegacyLearningStateMigration);

        // The bridge still converts the data afterwards.
        await DatabaseMigrationBridge.UpgradeAsync(fixture.Db);
        Assert.AreEqual(
            UserTermState.Learning,
            (await fixture.Db.LearningCards.AsNoTracking().SingleAsync()).State);
    }

    [TestMethod]
    public async Task PreAccountDefaultProfileMovesToOwnerAndOwnerDataWins()
    {
        await using var fixture = await Fixture.CreateBeforeUniversalCoursesAsync();
        await fixture.SeedLegacyAsync(
            """
            INSERT INTO OwnerAccounts (Id, UserName, NormalizedUserName, PasswordHash, CreatedAt)
            VALUES ('owner', 'Owner', 'OWNER', 'hash', '2026-09-20 10:00:00');

            INSERT INTO Terms (Id, Language, Canonical, Reading, Meaning) VALUES
                ($cat, 'ja', '猫', 'ねこ', 'Katze'),
                ($dog, 'ja', '犬', 'いぬ', 'Hund');

            INSERT INTO UserTerms
                (Id, ProfileId, TermId, State, IntervalDays, NextReviewAt, LearningStartedAt, QueuePosition, UpdatedAt)
            VALUES
                ($catA, 'owner', $cat, 1, 0, NULL, NULL, NULL, '2026-09-25 09:00:00'),
                ($catB, 'default', $cat, 2, 3, NULL, NULL, NULL, '2026-09-24 09:00:00'),
                ($dogA, 'default', $dog, 2, 5, NULL, NULL, NULL, '2026-09-24 09:00:00');

            INSERT INTO Reviews (Id, ProfileId, TermId, Rating, ClientEventId, ReviewedAt, NextReviewAt) VALUES
                (51, 'default', $dog, 3, $eventA1, '2026-09-21 10:00:00', '2026-09-26 10:00:00');
            """);

        await DatabaseMigrationBridge.UpgradeAsync(fixture.Db);

        Assert.AreEqual(0, await fixture.Db.LearningCards.CountAsync(x => x.ProfileId == "default"));
        Assert.AreEqual(0, await fixture.Db.LearningCourses.CountAsync(x => x.ProfileId == "default"));
        Assert.AreEqual(0, await fixture.Db.LearningCardReviews.CountAsync(x => x.ProfileId == "default"));

        var owner = await fixture.Db.LearningCards.AsNoTracking()
            .Where(x => x.ProfileId == "owner")
            .ToDictionaryAsync(x => x.Id);
        Assert.AreEqual(2, owner.Count);
        Assert.AreEqual(UserTermState.Known, owner[Guid.Parse(CatUserTermA)].State);
        Assert.AreEqual(5, owner[Guid.Parse(DogUserTermA)].IntervalDays);
        Assert.AreEqual(1, await fixture.Db.LearningCourses.CountAsync(x => x.ProfileId == "owner"));

        var review = await fixture.Db.LearningCardReviews.AsNoTracking().SingleAsync();
        Assert.AreEqual("owner", review.ProfileId);
        Assert.AreEqual(Guid.Parse(DogUserTermA), review.CardId);
    }

    private static string Id(string suffix) =>
        $"00000000-0000-0000-0000-00000000{suffix}";

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string directory, AppDbContext db)
        {
            Directory = directory;
            Db = db;
        }

        public string Directory { get; }
        public AppDbContext Db { get; }

        public static async Task<Fixture> CreateBeforeUniversalCoursesAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"anilingo-learning-migration-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "anilingo.db")};Foreign Keys=True")
                .Options;
            var fixture = new Fixture(directory, new AppDbContext(options));

            var migrations = fixture.Db.Database.GetMigrations().ToArray();
            var index = Array.IndexOf(migrations, DatabaseMigrationBridge.UniversalLearningCoursesMigration);
            Assert.IsTrue(index > 0);
            await fixture.Db.GetService<IMigrator>().MigrateAsync(migrations[index - 1]);
            return fixture;
        }

        public async Task SeedLegacyAsync(string sql)
        {
            var connection = (SqliteConnection)Db.Database.GetDbConnection();
            await connection.OpenAsync();
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("$cat", CatId);
                command.Parameters.AddWithValue("$dog", DogId);
                command.Parameters.AddWithValue("$kana", KanaId);
                command.Parameters.AddWithValue("$catA", CatUserTermA);
                command.Parameters.AddWithValue("$dogA", DogUserTermA);
                command.Parameters.AddWithValue("$kanaA", KanaUserTermA);
                command.Parameters.AddWithValue("$catB", CatUserTermB);
                command.Parameters.AddWithValue("$eventA1", EventA1);
                command.Parameters.AddWithValue("$eventA2", EventA2);
                await command.ExecuteNonQueryAsync();
            }
            finally
            {
                await connection.CloseAsync();
            }
        }

        public async Task<long> ScalarAsync(string sql)
        {
            var connection = Db.Database.GetDbConnection();
            await connection.OpenAsync();
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                return Convert.ToInt64(await command.ExecuteScalarAsync());
            }
            finally
            {
                await connection.CloseAsync();
            }
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
