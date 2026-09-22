using AniLingo.Web.Features.Vocabulary;

namespace AniLingo.Tests;

[TestClass]
public sealed class JapaneseTermExtractorTests
{
    private readonly JapaneseTermExtractor extractor = new();

    [TestMethod]
    public void SplitsCommonTrailingParticleFromKanjiTerm()
    {
        var terms = extractor.Extract("学校に行く");

        CollectionAssert.Contains(terms.ToList(), "学校");
        CollectionAssert.Contains(terms.ToList(), "行く");
    }

    [TestMethod]
    public void IgnoresLatinSubtitleNoise()
    {
        var terms = extractor.Extract("SIGN: Tokyo 2026");

        Assert.AreEqual(0, terms.Count);
    }
}
