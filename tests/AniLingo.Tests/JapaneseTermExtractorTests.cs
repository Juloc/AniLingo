using AniLingo.Web.Features.Vocabulary;

namespace AniLingo.Tests;

[TestClass]
public sealed class JapaneseTermExtractorTests
{
    [TestMethod]
    public void KeepsLexicalLemmasAndFiltersParticles()
    {
        var extractor = new JapaneseTermExtractor(new StubMorphology(new Dictionary<string, JapaneseMorphToken[]>
        {
            ["学校に行った"] =
            [
                new("学校", "学校", "ガッコウ", "名詞"),
                new("に", "に", "ニ", "助詞"),
                new("行っ", "行く", "イク", "動詞"),
                new("た", "た", "タ", "助動詞")
            ]
        }));

        var terms = extractor.Extract("学校に行った");

        CollectionAssert.AreEqual(
            new[] { "学校", "行く" },
            terms.Select(term => term.Canonical).ToArray());
        CollectionAssert.AreEqual(
            new[] { "がっこう", "いく" },
            terms.Select(term => term.Reading).ToArray());
    }

    [TestMethod]
    public void InflectionsUseSameLemma()
    {
        var extractor = new JapaneseTermExtractor(new StubMorphology(new Dictionary<string, JapaneseMorphToken[]>
        {
            ["食べた"] = [new("食べ", "食べる", "タベル", "動詞"), new("た", "た", "タ", "助動詞")],
            ["食べる"] = [new("食べる", "食べる", "タベル", "動詞")]
        }));

        Assert.AreEqual("食べる", extractor.Extract("食べた").Single().Canonical);
        Assert.AreEqual("食べる", extractor.Extract("食べる").Single().Canonical);
    }

    [TestMethod]
    public void IgnoresLatinSubtitleNoise()
    {
        var extractor = new JapaneseTermExtractor(new StubMorphology(new Dictionary<string, JapaneseMorphToken[]>
        {
            ["SIGN: Tokyo 2026"] =
            [
                new("SIGN", "SIGN", "SIGN", "名詞"),
                new("Tokyo", "Tokyo", "Tokyo", "名詞")
            ]
        }));

        Assert.AreEqual(0, extractor.Extract("SIGN: Tokyo 2026").Count);
    }

    private sealed class StubMorphology(IReadOnlyDictionary<string, JapaneseMorphToken[]> tokens)
        : IJapaneseMorphology
    {
        public IReadOnlyList<JapaneseMorphToken> Analyze(string text) =>
            tokens.GetValueOrDefault(text) ?? [];
    }
}
