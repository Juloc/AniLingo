using AniLingo.Web.Features.Subtitles;

namespace AniLingo.Tests;

[TestClass]
public sealed class SubtitleLanguageAliasesTests
{
    [TestMethod]
    public void TokensForIncludesEveryAliasOfTheLanguage()
    {
        (string LanguageTag, string[] ExpectedTokens)[] cases =
        [
            ("ja", ["ja", "jp", "jpn", "japanese", "日本語"]),
            ("de", ["de", "deu", "ger", "german", "deutsch"]),
            ("id", ["id", "ind", "indonesian"]),
            ("en", ["en", "eng", "english"]),
            ("ro", ["ro", "ron", "rum", "romanian"])
        ];

        foreach (var (languageTag, expectedTokens) in cases)
        {
            var tokens = SubtitleLanguageAliases.TokensFor(languageTag);
            foreach (var expected in expectedTokens)
            {
                Assert.IsTrue(
                    tokens.Contains(expected, StringComparer.OrdinalIgnoreCase),
                    $"Expected {languageTag} tokens to contain '{expected}'.");
            }
        }
    }

    [TestMethod]
    public void HasTokenMatchesAnyAliasCaseInsensitively()
    {
        (string LanguageTag, string Token)[] cases =
        [
            ("ja", "jpn"),
            ("ja", "JAPANESE"),
            ("de", "deu"),
            ("de", "GERMAN"),
            ("id", "ind"),
            ("en", "eng"),
            ("ro", "rum")
        ];

        foreach (var (languageTag, token) in cases)
        {
            Assert.IsTrue(
                SubtitleLanguageAliases.HasToken([token], languageTag),
                $"Expected '{token}' to match {languageTag}.");
        }
    }

    [TestMethod]
    public void HasTokenFallsBackToTheBareTagForLanguagesWithoutAnEntry()
    {
        Assert.IsTrue(SubtitleLanguageAliases.HasToken(["fr-CA"], "fr-CA"));
        Assert.IsFalse(SubtitleLanguageAliases.HasToken(["fr"], "fr-CA"));
    }

    [TestMethod]
    public void IsTaggedAsOtherLanguageRejectsKnownForeignTokensOnly()
    {
        Assert.IsTrue(SubtitleLanguageAliases.IsTaggedAsOtherLanguage(["en"], "ja"));
        Assert.IsTrue(SubtitleLanguageAliases.IsTaggedAsOtherLanguage(["zh"], "de"));
        Assert.IsFalse(SubtitleLanguageAliases.IsTaggedAsOtherLanguage(["ja"], "ja"));
        Assert.IsFalse(SubtitleLanguageAliases.IsTaggedAsOtherLanguage(["SubsPlease"], "ja"));
    }

    [TestMethod]
    public void NameTokensExcludeShortIsoCodesButKeepFullNamesAndNonAsciiScripts()
    {
        var japaneseNames = SubtitleLanguageAliases.NameTokensFor("ja");
        CollectionAssert.DoesNotContain(japaneseNames.ToArray(), "ja");
        CollectionAssert.DoesNotContain(japaneseNames.ToArray(), "jp");
        CollectionAssert.DoesNotContain(japaneseNames.ToArray(), "jpn");
        CollectionAssert.Contains(japaneseNames.ToArray(), "japanese");
        CollectionAssert.Contains(japaneseNames.ToArray(), "日本語");

        var germanNames = SubtitleLanguageAliases.NameTokensFor("de");
        CollectionAssert.DoesNotContain(germanNames.ToArray(), "de");
        CollectionAssert.Contains(germanNames.ToArray(), "german");
        CollectionAssert.Contains(germanNames.ToArray(), "deutsch");
    }
}
