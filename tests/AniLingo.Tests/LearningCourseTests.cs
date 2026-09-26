using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Learning.Courses;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class LearningCourseTests
{
    [TestMethod]
    public async Task ArbitraryLanguagePairsAreIndependentCourses()
    {
        await using var fixture = await Fixture.CreateAsync();

        (string Source, string Target)[] pairs =
        [
            ("ja", "de"),
            ("de", "ja"),
            ("ja", "id"),
            ("ro", "de"),
            ("de", "id"),
            ("id", "de")
        ];

        foreach (var (source, target) in pairs)
        {
            await fixture.Store.CreateAsync(
                "reader",
                source,
                target,
                null,
                new LearningCourseOptions(ProductionEnabled: true),
                CancellationToken.None);
        }

        var courses = await fixture.Store.ListAsync("reader", CancellationToken.None);

        Assert.AreEqual(pairs.Length, courses.Count);
        foreach (var (source, target) in pairs)
        {
            var course = courses.Single(x => x.SourceLanguage == source && x.TargetLanguage == target);
            Assert.AreEqual($"{source} → {target}", course.Name);
            Assert.IsTrue(course.Options.ProductionEnabled);
        }

        // The first course per source language receives catalog words.
        Assert.IsTrue(courses.Single(x => x.SourceLanguage == "ja" && x.TargetLanguage == "de").IsPrimary);
        Assert.IsFalse(courses.Single(x => x.SourceLanguage == "ja" && x.TargetLanguage == "id").IsPrimary);
        Assert.IsTrue(courses.Single(x => x.SourceLanguage == "de" && x.TargetLanguage == "ja").IsPrimary);
        Assert.IsFalse(courses.Single(x => x.SourceLanguage == "de" && x.TargetLanguage == "id").IsPrimary);
        Assert.IsTrue(courses.Single(x => x.SourceLanguage == "ro").IsPrimary);
        Assert.IsTrue(courses.Single(x => x.SourceLanguage == "id").IsPrimary);

        Assert.AreEqual(0, (await fixture.Store.ListAsync("other", CancellationToken.None)).Count);
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

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => fixture.Store.CreateAsync(
                "reader",
                "ro",
                "RO",
                null,
                null,
                CancellationToken.None));

        // Another profile may use the same pair.
        await fixture.Store.CreateAsync(
            "other",
            "ro",
            "de",
            null,
            null,
            CancellationToken.None);
    }

    [TestMethod]
    public void LanguageTagsAreNormalizedAsBcp47Cultures()
    {
        Assert.AreEqual("de-DE", LearningLanguageTag.Normalize("de_DE"));
        Assert.AreEqual("id", LearningLanguageTag.Normalize("id"));
        Assert.AreEqual("ja", LearningLanguageTag.Normalize("ja"));
        Assert.AreEqual("ja-Latn", LearningLanguageTag.Normalize("ja-latn"));

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
            CancellationToken.None);

        Assert.AreEqual(4, cards.Count);
        Assert.AreEqual(4, cards.Select(x => x.Id).Distinct().Count());

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

        Assert.IsTrue(cards.All(x => x.State == UserTermState.Saved));

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
            CancellationToken.None);
        CollectionAssert.AreEquivalent(
            cards.Select(x => x.Id).ToArray(),
            second.Select(x => x.Id).ToArray());
    }

    [TestMethod]
    public async Task CardsNeedSourceVariantAndPromptingModesNeedTargetVariant()
    {
        await using var fixture = await Fixture.CreateAsync();

        var course = await fixture.Store.CreateAsync(
            "reader",
            "ro",
            "de",
            null,
            new LearningCourseOptions(ProductionEnabled: true, WritingEnabled: true),
            CancellationToken.None);

        var romanianOnly = await fixture.Store.CreateUnitAsync(
            LearningUnitKind.Word,
            [new LearningVariantInput("ro", "carte")],
            CancellationToken.None);

        var cards = await fixture.Store.EnsureCourseCardsAsync(
            "reader",
            course.Id,
            romanianOnly.Unit.Id,
            CancellationToken.None);

        // Recognition works without a translation yet; production/writing need
        // a German prompt and are not created.
        Assert.AreEqual(LearningCardMode.Recognition, cards.Single().Mode);

        var germanOnly = await fixture.Store.CreateUnitAsync(
            LearningUnitKind.Word,
            [new LearningVariantInput("de", "Buch")],
            CancellationToken.None);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => fixture.Store.EnsureCourseCardsAsync(
                "reader",
                course.Id,
                germanOnly.Unit.Id,
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
    public async Task CatalogWordsJoinThePrimaryCourseOfTheirLanguage()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.Service;

        var cat = await fixture.AddTermAsync("猫", "ねこ", "Katze");
        await service.SetStateAsync(cat.Id, UserTermState.Learning, CancellationToken.None);

        // A profile without courses gets a ja → de recognition course.
        var courses = await fixture.Store.ListAsync(LearningProfile.DefaultId, CancellationToken.None);
        var japaneseGerman = courses.Single();
        Assert.AreEqual("ja", japaneseGerman.SourceLanguage);
        Assert.AreEqual("de", japaneseGerman.TargetLanguage);
        Assert.IsTrue(japaneseGerman.IsPrimary);

        var catUnit = await fixture.Db.LearningUnits.SingleAsync(x => x.TermId == cat.Id);
        var variants = await fixture.Store.ListVariantsAsync(catUnit.Id, CancellationToken.None);
        Assert.AreEqual("ねこ", variants.Single(x => x.LanguageTag == "ja").Reading);
        Assert.AreEqual("Katze", variants.Single(x => x.LanguageTag == "de").Text);

        var japaneseIndonesian = await fixture.Store.CreateAsync(
            LearningProfile.DefaultId,
            "ja",
            "id",
            null,
            null,
            CancellationToken.None);
        Assert.IsFalse(japaneseIndonesian.IsPrimary);

        await fixture.Store.SetPrimaryAsync(
            LearningProfile.DefaultId,
            japaneseIndonesian.Id,
            CancellationToken.None);

        var dog = await fixture.AddTermAsync("犬", "いぬ", "Hund");
        await service.SetStateAsync(dog.Id, UserTermState.Known, CancellationToken.None);

        var dogCard = await (
                from card in fixture.Db.LearningCards
                join unit in fixture.Db.LearningUnits on card.UnitId equals unit.Id
                where unit.TermId == dog.Id
                select card)
            .SingleAsync();
        Assert.AreEqual(japaneseIndonesian.Id, dogCard.CourseId);
        Assert.AreEqual("id", dogCard.AnswerLanguage);

        courses = await fixture.Store.ListAsync(LearningProfile.DefaultId, CancellationToken.None);
        Assert.AreEqual(1, courses.Count(x => x.SourceLanguage == "ja" && x.IsPrimary));

        // The term read model follows the primary course.
        var dogState = await LearningQueries.TermStates(fixture.Db, LearningProfile.DefaultId)
            .SingleAsync(x => x.TermId == dog.Id);
        Assert.AreEqual(UserTermState.Known, dogState.State);
    }

    [TestMethod]
    public async Task EnablingAModeBackfillsCardsAndDisablingItPausesThem()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.Service;
        await service.SavePreferencesAsync(0.90, 50, 100, CancellationToken.None);

        var term = await fixture.AddTermAsync("本", "ほん", "Buch");
        await service.SetStateAsync(term.Id, UserTermState.Learning, CancellationToken.None);

        var course = (await fixture.Store.ListAsync(LearningProfile.DefaultId, CancellationToken.None)).Single();
        Assert.IsFalse(course.Options.ProductionEnabled);
        Assert.AreEqual(1, await fixture.Db.LearningCards.CountAsync());

        await fixture.Store.UpdateAsync(
            LearningProfile.DefaultId,
            course.Id,
            null,
            isEnabled: true,
            course.Options with { ProductionEnabled = true },
            CancellationToken.None);

        var production = await fixture.Db.LearningCards
            .AsNoTracking()
            .SingleAsync(x => x.Mode == LearningCardMode.Production);
        Assert.AreEqual(UserTermState.Learning, production.State);
        Assert.AreEqual("de", production.PromptLanguage);
        Assert.AreEqual("ja", production.AnswerLanguage);
        Assert.IsNotNull(production.QueuePosition);

        var due = await service.GetDueAsync(CancellationToken.None);
        CollectionAssert.AreEquivalent(
            new[] { LearningCardMode.Recognition, LearningCardMode.Production },
            due.Select(x => x.Mode).ToArray());
        var productionItem = due.Single(x => x.Mode == LearningCardMode.Production);
        Assert.AreEqual("Buch", productionItem.Prompt);
        Assert.AreEqual("本", productionItem.Answer);
        Assert.AreEqual("ほん", productionItem.AnswerReading);

        await service.ReviewAsync(productionItem.CardId, ReviewRating.Again, CancellationToken.None);

        await fixture.Store.UpdateAsync(
            LearningProfile.DefaultId,
            course.Id,
            null,
            isEnabled: true,
            course.Options with { ProductionEnabled = false },
            CancellationToken.None);

        var afterDisable = await service.GetDueAsync(CancellationToken.None);
        Assert.IsFalse(afterDisable.Any(x => x.Mode == LearningCardMode.Production));
        Assert.AreEqual(
            1,
            await fixture.Db.LearningCardReviews.CountAsync(x => x.CardId == productionItem.CardId),
            "Disabling a mode keeps its review history.");

        await fixture.Store.UpdateAsync(
            LearningProfile.DefaultId,
            course.Id,
            null,
            isEnabled: false,
            course.Options,
            CancellationToken.None);
        Assert.AreEqual(0, (await service.GetDueAsync(CancellationToken.None)).Count);
        Assert.AreEqual(
            0,
            await LearningQueries.DueCards(fixture.Db, LearningProfile.DefaultId, DateTime.UtcNow.AddDays(400)).CountAsync());
    }

    [TestMethod]
    public async Task RecognitionAndProductionKeepIndependentFsrsState()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.Service;
        await service.SavePreferencesAsync(0.90, 50, 100, CancellationToken.None);

        await fixture.Store.CreateAsync(
            LearningProfile.DefaultId,
            "ja",
            "de",
            null,
            new LearningCourseOptions(ProductionEnabled: true),
            CancellationToken.None);

        var term = await fixture.AddTermAsync("水", "みず", "Wasser");
        await service.SetStateAsync(term.Id, UserTermState.Learning, CancellationToken.None);

        var due = await service.GetDueAsync(CancellationToken.None);
        Assert.AreEqual(2, due.Count);
        var recognition = due.Single(x => x.Mode == LearningCardMode.Recognition);
        var production = due.Single(x => x.Mode == LearningCardMode.Production);
        Assert.AreEqual(recognition.UnitId, production.UnitId);
        Assert.AreEqual(term.Id, recognition.TermId);
        Assert.AreEqual(term.Id, production.TermId);

        await service.ReviewAsync(recognition.CardId, ReviewRating.Easy, CancellationToken.None);
        await service.ReviewAsync(production.CardId, ReviewRating.Again, CancellationToken.None);

        var cards = await fixture.Db.LearningCards
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Mode);

        Assert.IsTrue(
            cards[LearningCardMode.Recognition].NextReviewAt > cards[LearningCardMode.Production].NextReviewAt,
            "An Easy recognition must be scheduled later than a failed production.");
        Assert.IsTrue(cards[LearningCardMode.Recognition].IntervalDays >= 1);
        Assert.AreEqual(0, cards[LearningCardMode.Production].IntervalDays);

        var histories = await fixture.Db.LearningCardReviews
            .AsNoTracking()
            .GroupBy(x => x.CardId)
            .Select(x => new { CardId = x.Key, Count = x.Count(), Rating = x.Max(r => r.Rating) })
            .ToDictionaryAsync(x => x.CardId);
        Assert.AreEqual(ReviewRating.Easy, histories[recognition.CardId].Rating);
        Assert.AreEqual(ReviewRating.Again, histories[production.CardId].Rating);

        // Only the failed production card is due again right away.
        var remaining = await service.GetDueAsync(CancellationToken.None);
        Assert.IsFalse(remaining.Any(x => x.CardId == recognition.CardId));
    }

    [TestMethod]
    public async Task NonJapaneseCoursesScheduleThroughTheSameService()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.Service;
        await service.SavePreferencesAsync(0.90, 50, 100, CancellationToken.None);

        var romanianGerman = await fixture.Store.CreateAsync(
            LearningProfile.DefaultId,
            "ro",
            "de",
            null,
            new LearningCourseOptions(ProductionEnabled: true),
            CancellationToken.None);
        var germanIndonesian = await fixture.Store.CreateAsync(
            LearningProfile.DefaultId,
            "de",
            "id",
            null,
            new LearningCourseOptions(RecognitionEnabled: false, WritingEnabled: true),
            CancellationToken.None);

        var book = await fixture.Store.CreateUnitAsync(
            LearningUnitKind.Word,
            [
                new LearningVariantInput("ro", "carte"),
                new LearningVariantInput("de", "Buch"),
                new LearningVariantInput("id", "buku")
            ],
            CancellationToken.None);

        await service.AddUnitsToLearningAsync(romanianGerman.Id, [book.Unit.Id], CancellationToken.None);
        await service.AddUnitsToLearningAsync(germanIndonesian.Id, [book.Unit.Id], CancellationToken.None);

        var due = await service.GetDueAsync(CancellationToken.None);

        // Recognition is disabled for de → id: its anchor card exists but is not scheduled.
        Assert.AreEqual(3, due.Count);
        var ro = due.Single(x => x.PromptLanguage == "ro");
        Assert.AreEqual(LearningCardMode.Recognition, ro.Mode);
        Assert.AreEqual("carte", ro.Prompt);
        Assert.AreEqual("Buch", ro.Answer);
        Assert.IsNull(ro.TermId);

        var production = due.Single(x => x.Mode == LearningCardMode.Production);
        Assert.AreEqual("Buch", production.Prompt);
        Assert.AreEqual("carte", production.Answer);

        var writing = due.Single(x => x.Mode == LearningCardMode.Writing);
        Assert.AreEqual("id", writing.PromptLanguage);
        Assert.AreEqual("buku", writing.Prompt);
        Assert.AreEqual("Buch", writing.Answer);

        Assert.AreEqual(
            2,
            await fixture.Db.LearningCards.CountAsync(x => x.CourseId == germanIndonesian.Id));

        foreach (var item in due)
        {
            await service.ReviewAsync(item.CardId, ReviewRating.Good, CancellationToken.None);
        }

        Assert.AreEqual(3, await fixture.Db.LearningCardReviews.CountAsync());
        Assert.AreEqual(3, await fixture.Db.LearningCardReviews.Select(x => x.CardId).Distinct().CountAsync());
    }

    [TestMethod]
    public async Task OfflineEventsAddressDirectionalCardsAndStayIdempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.Service;
        await service.SavePreferencesAsync(0.90, 50, 100, CancellationToken.None);

        await fixture.Store.CreateAsync(
            LearningProfile.DefaultId,
            "ja",
            "de",
            null,
            new LearningCourseOptions(ProductionEnabled: true),
            CancellationToken.None);

        var term = await fixture.AddTermAsync("火", "ひ", "Feuer");
        await service.SetStateAsync(term.Id, UserTermState.Learning, CancellationToken.None);
        var due = await service.GetDueAsync(CancellationToken.None);
        var production = due.Single(x => x.Mode == LearningCardMode.Production);
        var recognition = due.Single(x => x.Mode == LearningCardMode.Recognition);

        var cardEvent = new OfflineReviewEvent(
            Guid.NewGuid(),
            null,
            ReviewRating.Good,
            DateTime.UtcNow.AddMinutes(-2),
            production.CardId);
        var termEvent = new OfflineReviewEvent(
            Guid.NewGuid(),
            term.Id,
            ReviewRating.Hard,
            DateTime.UtcNow.AddMinutes(-1));

        var first = await service.SyncOfflineReviewsAsync(
            [cardEvent, termEvent],
            DateTime.UtcNow,
            CancellationToken.None);
        CollectionAssert.AreEquivalent(
            new[] { cardEvent.EventId, termEvent.EventId },
            first.Accepted.ToArray());

        var reviews = await fixture.Db.LearningCardReviews
            .AsNoTracking()
            .ToDictionaryAsync(x => x.ClientEventId!.Value);
        Assert.AreEqual(production.CardId, reviews[cardEvent.EventId].CardId);
        Assert.AreEqual(
            recognition.CardId,
            reviews[termEvent.EventId].CardId,
            "A term-only event rates the word's Recognition card.");

        var repeat = await service.SyncOfflineReviewsAsync(
            [termEvent, cardEvent],
            DateTime.UtcNow,
            CancellationToken.None);
        Assert.AreEqual(0, repeat.Accepted.Count);
        CollectionAssert.AreEquivalent(
            new[] { cardEvent.EventId, termEvent.EventId },
            repeat.AlreadyApplied.ToArray());
        Assert.AreEqual(2, await fixture.Db.LearningCardReviews.CountAsync());
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
            Service = new LearningService(db, new FsrsReviewScheduler());
        }

        public string Directory { get; }
        public AppDbContext Db { get; }
        public LearningCourseStore Store { get; }
        public LearningService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"anilingo-learning-course-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(
                    $"Data Source={Path.Combine(directory, "anilingo.db")};Foreign Keys=True")
                .Options;

            var fixture = new Fixture(directory, new AppDbContext(options));
            await DatabaseMigrationBridge.UpgradeAsync(fixture.Db);
            return fixture;
        }

        public async Task<Term> AddTermAsync(string canonical, string reading, string meaning)
        {
            var term = new Term
            {
                Language = "ja",
                Canonical = canonical,
                Reading = reading,
                Meaning = meaning
            };
            Db.Terms.Add(term);
            await Db.SaveChangesAsync();
            return term;
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
