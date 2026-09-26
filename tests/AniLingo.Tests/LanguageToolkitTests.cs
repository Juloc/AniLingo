using AniLingo.Web.Features.Learning.Courses;
using AniLingo.Web.Features.Learning.LanguageAssistance;
using AniLingo.Web.Features.Learning.Toolkits;
using AniLingo.Web.Features.Vocabulary;

namespace AniLingo.Tests;

/// <summary>
/// Covers the pluggable language toolkit pieces added for #231: generic
/// tokenization for non-Japanese languages, capability-gated dictionary/reading
/// access and the fact that Japanese behaviour through the same registry is
/// unchanged.
/// </summary>
[TestClass]
public sealed class LanguageToolkitTests
{
    [TestMethod]
    public void GenericToolkit_SupportsTokenizationAndTts_NotDictionaryOrReadings()
    {
        var toolkit = new GenericLearningLanguageToolkit("de");

        Assert.IsTrue(toolkit.Supports(LearningLanguageCapability.Tokenization));
        Assert.IsTrue(toolkit.Supports(LearningLanguageCapability.TextToSpeech));
        Assert.IsFalse(toolkit.Supports(LearningLanguageCapability.Dictionary));
        Assert.IsFalse(toolkit.Supports(LearningLanguageCapability.Readings));
        Assert.IsFalse(toolkit.Supports(LearningLanguageCapability.Transliteration));
        Assert.IsFalse(toolkit.Supports(LearningLanguageCapability.ScriptTrainer));

        Assert.IsNotNull(toolkit.TermExtractor);
        Assert.IsNull(toolkit.Dictionary);
    }

    [TestMethod]
    public void JapaneseToolkit_SupportsEveryCapability()
    {
        var toolkit = new LearningLanguageToolkitRegistry(
            new JapaneseTermExtractor(new NullMorphology()),
            new JapaneseDictionary(Path.GetTempPath())).Get("ja");

        foreach (var capability in Enum.GetValues<LearningLanguageCapability>())
        {
            Assert.IsTrue(toolkit.Supports(capability), capability.ToString());
        }

        Assert.IsNotNull(toolkit.TermExtractor);
        Assert.IsNotNull(toolkit.Dictionary);
    }

    [TestMethod]
    public void Registry_StaticSupports_MatchesResolvedToolkitCapabilities()
    {
        foreach (var capability in Enum.GetValues<LearningLanguageCapability>())
        {
            Assert.AreEqual(
                new GenericLearningLanguageToolkit("ro").Supports(capability),
                LearningLanguageToolkitRegistry.Supports("ro", capability),
                capability.ToString());

            Assert.AreEqual(
                JapaneseLearningLanguageToolkit.SupportsCapability(capability),
                LearningLanguageToolkitRegistry.Supports("ja", capability),
                capability.ToString());
        }
    }

    [TestMethod]
    public void GenericTermExtractor_TokenizesGermanTextWithCaseFoldingAndStopwords()
    {
        var extractor = new GenericTermExtractor("de");

        var terms = extractor.Extract("Der Hund und die Katze laufen schnell.");

        CollectionAssert.AreEqual(
            new[] { "hund", "katze", "laufen", "schnell" },
            terms.Select(x => x.Canonical).ToArray());
        Assert.IsTrue(terms.All(x => x.Reading is null));
    }

    [TestMethod]
    public void GenericTermExtractor_TokenizesIndonesianTextWithStopwords()
    {
        var extractor = new GenericTermExtractor("id");

        var terms = extractor.Extract("Kucing itu berlari dengan cepat di taman.");

        CollectionAssert.AreEqual(
            new[] { "kucing", "berlari", "cepat", "taman" },
            terms.Select(x => x.Canonical).ToArray());
    }

    [TestMethod]
    public void GenericTermExtractor_TokenizesRomanianTextWithStopwords()
    {
        var extractor = new GenericTermExtractor("ro");

        var terms = extractor.Extract("Pisica și câinele aleargă repede în parc.");

        CollectionAssert.AreEqual(
            new[] { "pisica", "câinele", "aleargă", "repede", "parc" },
            terms.Select(x => x.Canonical).ToArray());
    }

    [TestMethod]
    public void GenericTermExtractor_UnlistedLanguageGetsNoStopwordFiltering()
    {
        // French has no curated stopword list; every word survives rather than
        // being (mis)filtered by a guessed list.
        var extractor = new GenericTermExtractor("fr");

        var terms = extractor.Extract("Le chat et le chien courent vite.");

        CollectionAssert.AreEqual(
            new[] { "le", "chat", "et", "le", "chien", "courent", "vite" },
            terms.Select(x => x.Canonical).ToArray());
    }

    [TestMethod]
    public void Analyze_GenericLanguage_NeverProducesReadingsOrMeaning()
    {
        var analyzer = new LanguageTextAnalyzer(
            new NullMorphology(),
            new JapaneseDictionary(Path.GetTempPath()));

        var tokens = analyzer.Analyze("Der Hund läuft.", "de");

        Assert.IsTrue(tokens.Where(x => x.Interactive).All(x => x.Reading is null));
        Assert.IsTrue(tokens.Where(x => x.Interactive).All(x => x.Meaning is null));
    }

    [TestMethod]
    public void LookUp_GenericLanguage_ReturnsNoDictionaryState()
    {
        var analyzer = new LanguageTextAnalyzer(
            new NullMorphology(),
            new JapaneseDictionary(Path.GetTempPath()));

        var (reading, meaning) = analyzer.LookUp("hund", "de");

        Assert.IsNull(reading);
        Assert.IsNull(meaning);
    }

    [TestMethod]
    public void SentenceExplanationSupport_OnlySupportsReadingsCapableLanguage()
    {
        Assert.IsTrue(SentenceExplanationSupport.Supports("ja"));
        Assert.IsFalse(SentenceExplanationSupport.Supports("de"));
        Assert.IsFalse(SentenceExplanationSupport.Supports("id"));
        Assert.IsFalse(SentenceExplanationSupport.Supports("ro"));
    }

    private sealed class NullMorphology : IJapaneseMorphology
    {
        public IReadOnlyList<JapaneseMorphToken> Analyze(string text) => [];
    }
}
