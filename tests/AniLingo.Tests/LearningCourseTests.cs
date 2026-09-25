using System.Data.Common;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Learning.Courses;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AniLingo.Tests;

[TestClass]
public sealed class LearningCourseTests
{
    private const string MigrationId =
        "20260925203500_AddUniversalLearningCourses";

    [TestMethod]
    public async Task ArbitraryLanguagePairsAreIndependentCourses()
    {
        await using var fixture = await Fixture.CreateAsync();

        var indonesianToGerman = await fixture.Store.CreateAsync(
            "reader",
            "id",
            "de",
            "Bahasa Indonesia → Deutsch",
            new LearningCourseOptions(
                RecognitionEnabled: true,
                ProductionEnabled: true,
                ListeningEnabled: true,
                WritingEnabled: false,
                SentencePracticeEnabled: true),
            CancellationToken.None);

        var germanToIndonesian = await fixture.Store.CreateAsync(
            "reader",
            "de",
            "id",
            "Deutsch → Bahasa Indonesia",
            new LearningCourseOptions(
                RecognitionEnabled: true,
                ProductionEnabled: true,
                ListeningEnabled: false,
                WritingEnabled: true,
                SentencePracticeEnabled: true),
            CancellationToken.None);

        Assert.AreEqual("id", indonesianToGerman.SourceLanguage);
        Assert.AreEqual("de", indonesianToGerman.TargetLanguage);
        Assert.IsTrue(indonesianToGerman.Options.ListeningEnabled);
        Assert.IsFalse(indonesianToGerman.Options.WritingEnabled);

        Assert.AreEqual("de", germanToIndonesian.SourceLanguage);
        Assert.AreEqual("id", germanToIndonesian.TargetLanguage);
        Assert.IsFalse(germanToIndonesian.Options.ListeningEnabled);
        Assert.IsTrue(germanToIndonesian.Options.WritingEnabled);

        var courses = await fixture.Store.ListAsync(
            "reader",
            CancellationToken.None);

        Assert.AreEqual(2, courses.Count);
    }

    [TestMethod]
    public async Task SameDirectionPairCannotBeDuplicatedPerProfile()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.Store.CreateAsync(
            "reader",
            "ro",
            "de",
            null,
            null,
            CancellationToken.None);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => fixture.Store.CreateAsync(
                "reader",
                "ro",
                "de",
                "Second Romanian course",
                null,
                CancellationToken.None));
    }

    [TestMethod]
    public void LanguageTagsAreNormalizedAsBcp47Cultures()
    {
        Assert.AreEqual("de-DE", LearningLanguageTag.Normalize("de_DE"));
        Assert.AreEqual("id", LearningLanguageTag.Normalize("id"));
        Assert.AreEqual("ja", LearningLanguageTag.Normalize("ja"));

        Assert.ThrowsExactly<ArgumentException>(
            () => LearningLanguageTag.Normalize("de--DE"));
    }

    [TestMethod]
    public async Task CourseCreatesIndependentDirectionalCards()
    {
        await using var fixture = await Fixture.CreateAsync();

        var course = await fixture.Store.CreateAsync(
            "reader",
            "de",
            "id",
            "Deutsch ↔ Bahasa Indonesia",
            new LearningCourseOptions(
                RecognitionEnabled: true,
                ProductionEnabled: true,
                ListeningEnabled: true,
                WritingEnabled: true,
                SentencePracticeEnabled: true),
            CancellationToken.None);

        var unit = await fixture.Store.CreateUnitAsync(
            LearningUnitKind.Word,
            [
                new LearningVariantInput("de", "essen"),
                new LearningVariantInput("id", "makan")
            ],
            CancellationToken.None);

        var cards = await fixture.Store.EnsureCourseCardsAsync(
            "reader",
            course.Id,
            unit.Unit.Id,
            LearningCardState.Saved,
            CancellationToken.None);

        Assert.AreEqual(4, cards.Count);

        var recognition = cards.Single(x => x.Mode == LearningCardMode.Recognition);
        Assert.AreEqual("de", recognition.PromptLanguage);
        Assert.AreEqual("id", recognition.AnswerLanguage);

        var production = cards.Single(x => x.Mode == LearningCardMode.Production);
        Assert.AreEqual("id", production.PromptLanguage);
        Assert.AreEqual("de", production.AnswerLanguage);

        var listening = cards.Single(x => x.Mode == LearningCardMode.Listening);
        Assert.AreEqual("de", listening.PromptLanguage);
        Assert.AreEqual("id", listening.AnswerLanguage);

        var writing = cards.Single(x => x.Mode == LearningCardMode.Writing);
        Assert.AreEqual("id", writing.PromptLanguage);
        Assert.AreEqual("de", writing.AnswerLanguage);

        Assert.IsTrue(cards.All(x => x.State == LearningCardState.Saved));

        var variants = await fixture.Store.ListVariantsAsync(
            unit.Unit.Id,
            CancellationToken.None);
        CollectionAssert.AreEquivalent(
            new[] { "de", "id" },
            variants.Select(x => x.LanguageTag).ToArray());

        // Re-running card creation is idempotent and does not duplicate cards.
        var second = await fixture.Store.EnsureCourseCardsAsync(
            "reader",
            course.Id,
            unit.Unit.Id,
            LearningCardState.Saved,
            CancellationToken.None);
        Assert.AreEqual(4, second.Count);
    }

    [TestMethod]
    public async Task CardsRequireBothCourseLanguageVariants()
    {
        await using var fixture = await Fixture.CreateAsync();

        var course = await fixture.Store.CreateAsync(
            "reader",
            "ro",
            "de",
            null,
            null,
            CancellationToken.None);

        var unit = await fixture.Store.CreateUnitAsync(
            LearningUnitKind.Word,
            [new LearningVariantInput("ro", "carte")],
            CancellationToken.None);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => fixture.Store.EnsureCourseCardsAsync(
                "reader",
                course.Id,
                unit.Unit.Id,
                LearningCardState.Saved,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ContextAnchorsAreMediaAgnostic()
    {
        await using var fixture = await Fixture.CreateAsync();

        var unit = await fixture.Store.CreateUnitAsync(
            LearningUnitKind.Word,
            [
                new LearningVariantInput("ja", "食べる", "たべる"),
                new LearningVariantInput("de", "essen")
            ],
            CancellationToken.None);

        await fixture.Store.AddContextAsync(
            unit.Unit.Id,
            new LearningContextInput(
                "anime",
                "episode:123",
                "timestamp:754000",
                "ja",
                "パンを食べる。"),
            CancellationToken.None);

        await fixture.Store.AddContextAsync(
            unit.Unit.Id,
            new LearningContextInput(
                "book",
                "book:42",
                "chapter:3#paragraph:8",
                "ja",
                "朝ご飯を食べる。"),
            CancellationToken.None);

        await fixture.Store.AddContextAsync(
            unit.Unit.Id,
            new LearningContextInput(
                "manga",
                "chapter:9",
                "page:17#region:2",
                "ja",
                "食べる？"),
            CancellationToken.None);

        var contexts = await fixture.Store.ListContextsAsync(
            unit.Unit.Id,
            CancellationToken.None);

        Assert.AreEqual(3, contexts.Count);
        CollectionAssert.AreEquivalent(
            new[] { "anime", "book", "manga" },
            contexts.Select(x => x.SourceType).ToArray());
        Assert.IsTrue(contexts.All(x => x.LanguageTag == "ja"));
    }

    [TestMethod]
    public void JapaneseToolkitIsSpecializedWithoutBlockingOtherLanguages()
    {
        var registry = new LearningLanguageToolkitRegistry();

        var japanese = registry.Get("ja");
        var indonesian = registry.Get("id");
        var romanian = registry.Get("ro");

        Assert.IsTrue(japanese.Supports(
            LearningLanguageCapability.ScriptTrainer));
        Assert.IsTrue(japanese.Supports(
            LearningLanguageCapability.Tokenization));

        Assert.IsFalse(indonesian.Supports(
            LearningLanguageCapability.ScriptTrainer));
        Assert.IsTrue(indonesian.Supports(
            LearningLanguageCapability.TextToSpeech));

        Assert.AreEqual("ro", romanian.LanguageTag);
        Assert.IsFalse(romanian.Supports(
            LearningLanguageCapability.Readings));
    }

    [TestMethod]
    public async Task LegacyTermReviewAndScheduleArePreservedInRecognitionCard()
    {
        await using var fixture = await Fixture.CreateBeforeUniversalCoursesAsync();

        var term = new Term
        {
            Id = Guid.NewGuid(),
            Language = "ja",
            Canonical = "食べる",
            Reading = "たべる",
            Meaning = "essen"
        };
        fixture.Db.Terms.Add(term);

        var userTerm = new UserTerm
        {
            Id = Guid.NewGuid(),
            ProfileId = "legacy-reader",
            TermId = term.Id,
            State = UserTermState.Learning,
            IntervalDays = 7,
            NextReviewAt = new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc),
            LearningStartedAt = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc),
            QueuePosition = 12,
            UpdatedAt = new DateTime(2026, 9, 25, 9, 0, 0, DateTimeKind.Utc)
        };
        fixture.Db.UserTerms.Add(userTerm);

        fixture.Db.Reviews.Add(new Review
        {
            ProfileId = "legacy-reader",
            TermId = term.Id,
            Rating = ReviewRating.Good,
            ClientEventId = Guid.NewGuid(),
            ReviewedAt = new DateTime(2026, 9, 25, 8, 0, 0, DateTimeKind.Utc),
            NextReviewAt = new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc)
        });

        await fixture.Db.SaveChangesAsync();

        await DatabaseMigrationBridge.UpgradeAsync(fixture.Db);

        var courses = await fixture.Store.ListAsync(
            "legacy-reader",
            CancellationToken.None);
        var course = AssertSingle(courses);

        Assert.AreEqual("ja", course.SourceLanguage);
        Assert.AreEqual("de", course.TargetLanguage);
        Assert.IsTrue(course.Options.RecognitionEnabled);
        Assert.IsFalse(course.Options.ProductionEnabled);

        var cards = await fixture.Store.ListCardsAsync(
            "legacy-reader",
            course.Id,
            CancellationToken.None);
        var card = AssertSingle(cards);

        Assert.AreEqual("ja", card.PromptLanguage);
        Assert.AreEqual("de", card.AnswerLanguage);
        Assert.AreEqual(LearningCardMode.Recognition, card.Mode);
        Assert.AreEqual(LearningCardState.Learning, card.State);
        Assert.AreEqual(7, card.IntervalDays);
        Assert.IsNotNull(card.LegacyUserTermId);
        Assert.AreEqual(userTerm.Id, Guid.Parse(card.LegacyUserTermId));
        Assert.IsNotNull(card.NextReviewAt);

        var reviewCount = await ScalarLongAsync(
            fixture.Db.Database.GetDbConnection(),
            """
            SELECT COUNT(*)
            FROM "LearningCardReviews"
            WHERE "CardId" = $cardId
              AND "Rating" = 3;
            """,
            ("$cardId", card.Id));

        Assert.AreEqual(1L, reviewCount);

        var sourceVariantCount = await ScalarLongAsync(
            fixture.Db.Database.GetDbConnection(),
            """
            SELECT COUNT(*)
            FROM "LearningVariants"
            WHERE "UnitId" = $unitId
              AND "LanguageTag" = 'ja'
              AND "Text" = '食べる';
            """,
            ("$unitId", card.UnitId));

        var targetVariantCount = await ScalarLongAsync(
            fixture.Db.Database.GetDbConnection(),
            """
            SELECT COUNT(*)
            FROM "LearningVariants"
            WHERE "UnitId" = $unitId
              AND "LanguageTag" = 'de'
              AND "Text" = 'essen';
            """,
            ("$unitId", card.UnitId));

        Assert.AreEqual(1L, sourceVariantCount);
        Assert.AreEqual(1L, targetVariantCount);
    }

    private static T AssertSingle<T>(IReadOnlyList<T> items)
    {
        Assert.AreEqual(1, items.Count);
        return items[0];
    }

    private static async Task<long> ScalarLongAsync(
        DbConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var item in parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = item.Name;
                parameter.Value = item.Value;
                command.Parameters.Add(parameter);
            }

            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            string directory,
            AppDbContext db)
        {
            Directory = directory;
            Db = db;
            Store = new LearningCourseStore(db);
        }

        public string Directory { get; }
        public AppDbContext Db { get; }
        public LearningCourseStore Store { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = Create();
            await DatabaseMigrationBridge.UpgradeAsync(fixture.Db);
            return fixture;
        }

        public static async Task<Fixture> CreateBeforeUniversalCoursesAsync()
        {
            var fixture = Create();
            var migrations = fixture.Db.Database.GetMigrations().ToArray();
            var index = Array.IndexOf(migrations, MigrationId);

            Assert.IsTrue(index > 0, $"Expected migration {MigrationId}.");

            await fixture.Db.GetService<IMigrator>()
                .MigrateAsync(migrations[index - 1]);

            return fixture;
        }

        private static Fixture Create()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"anilingo-learning-course-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(
                    $"Data Source={Path.Combine(directory, "anilingo.db")};Foreign Keys=True")
                .Options;

            return new Fixture(directory, new AppDbContext(options));
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
