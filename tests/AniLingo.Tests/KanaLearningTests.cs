using AniLingo.Web.Features.Kana;

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
    public void SentenceFilterAcceptsShortJapaneseAndRejectsNoise()
    {
        Assert.IsTrue(KanaPractice.IsSuitableSentence("今日は学校に行く。"));
        Assert.IsFalse(KanaPractice.IsSuitableSentence("hello world"));
        Assert.IsFalse(KanaPractice.IsSuitableSentence("あ\nい"));
    }
}
