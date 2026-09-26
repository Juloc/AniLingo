using AniLingo.Web.Data;
using AniLingo.Web.Features.Kana;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Learning.Courses;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class KanaLearningTests
{
    [TestMethod]
    public void FirstStageStartsWithFiveVowels()
    {
        var hiragana = KanaCatalog.ForStage(KanaScript.Hiragana, 1);
        var katakana = KanaCatalog.ForStage(KanaScript.Katakana, 1);

        CollectionAssert.AreEqual(
            new[] { "あ", "い", "う", "え", "お" },
            hiragana.Select(x => x.Symbol).ToArray());
        CollectionAssert.AreEqual(
            new[] { "ア", "イ", "ウ", "エ", "オ" },
            katakana.Select(x => x.Symbol).ToArray());
    }

    [TestMethod]
    public void EveryCatalogEntryHasStableUniqueTermId()
    {
        var ids = KanaCatalog.All.Select(KanaCatalog.IdFor).ToArray();

        Assert.AreEqual(ids.Length, ids.Distinct().Count());
        Assert.AreEqual(
            KanaCatalog.IdFor(KanaCatalog.All[0]),
            KanaCatalog.IdFor(KanaCatalog.All[0]));
    }

    [TestMethod]
    public void CatalogIncludesVoicedKanaAndCommonCombinations()
    {
        Assert.IsTrue(KanaCatalog.All.Any(x => x.Symbol == "が" && x.Romaji == "ga"));
        Assert.IsTrue(KanaCatalog.All.Any(x => x.Symbol == "ぱ" && x.Romaji == "pa"));
        Assert.IsTrue(KanaCatalog.All.Any(x => x.Symbol == "きゃ" && x.Romaji == "kya"));
        Assert.IsTrue(KanaCatalog.All.Any(x => x.Symbol == "ジョ" && x.Romaji == "jo"));
    }

    [TestMethod]
    public void GroupsStaySmallEnoughForGuidedPractice()
    {
        var largest = KanaCatalog.All
            .GroupBy(x => new { x.Script, x.Stage })
            .Max(group => group.Count());

        Assert.IsTrue(largest <= 12, "A guided stage should not dump the whole kana table on the learner.");
    }

    [TestMethod]
    public void PracticeChecksTheExpectedRepresentation()
    {
        var entry = KanaCatalog.All.Single(x => x.Symbol == "し");

        Assert.IsTrue(KanaPractice.IsCorrect(entry, KanaPracticeMode.KanaToRomaji, "SHI"));
        Assert.IsTrue(KanaPractice.IsCorrect(entry, KanaPracticeMode.RomajiToKana, "し"));
        Assert.IsTrue(KanaPractice.IsCorrect(entry, KanaPracticeMode.AudioToKana, "し"));
        Assert.IsFalse(KanaPractice.IsCorrect(entry, KanaPracticeMode.RomajiToKana, "シ"));
    }

    [TestMethod]
    public async Task KanaPracticeUsesAScriptCourseSeparateFromWords()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"anilingo-kana-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "anilingo.db")};Foreign Keys=True")
                .Options;
            await using var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var learning = LearningTestData.Service(db, "reader");
            var kana = new KanaLearningService(db, learning);
            Assert.IsNull(await kana.FindCourseAsync(CancellationToken.None));

            var courseId = await kana.PrepareAsync(CancellationToken.None);
            Assert.AreEqual(courseId, await kana.PrepareAsync(CancellationToken.None));

            var course = await db.LearningCourses.AsNoTracking().SingleAsync();
            Assert.AreEqual(KanaCatalog.PromptLanguage, course.SourceLanguage);
            Assert.AreEqual(KanaCatalog.AnswerLanguage, course.TargetLanguage);
            Assert.IsFalse(course.IsPrimary);
            Assert.AreEqual(KanaCatalog.All.Count, await db.LearningUnits.CountAsync(x => x.Kind == LearningUnitKind.Script));

            var a = KanaCatalog.IdFor(KanaCatalog.All[0]);
            var previous = await kana.AnswerAsync(courseId, a, correct: true, CancellationToken.None);
            Assert.IsNull(previous);

            var card = (await kana.LoadCardsAsync(courseId, [a], CancellationToken.None))[a];
            Assert.AreEqual(UserTermState.Learning, card.State);
            Assert.AreEqual(ReviewRating.Good, (await db.LearningCardReviews.SingleAsync()).Rating);

            await learning.SetUnitStateAsync(courseId, a, UserTermState.Known, CancellationToken.None);
            Assert.AreEqual(
                UserTermState.Known,
                await kana.AnswerAsync(courseId, a, correct: false, CancellationToken.None));
            Assert.AreEqual(1, await db.LearningCardReviews.CountAsync());

            // A Japanese catalog word never lands in the Kana course.
            var term = new Term { Language = "ja", Canonical = "猫", Meaning = "Katze" };
            db.Terms.Add(term);
            await db.SaveChangesAsync();
            await learning.SetStateAsync(term.Id, UserTermState.Learning, CancellationToken.None);

            var wordCourse = await db.LearningCourses.AsNoTracking().SingleAsync(x => x.Id != courseId);
            Assert.AreEqual("de", wordCourse.TargetLanguage);
            Assert.IsTrue(wordCourse.IsPrimary);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
