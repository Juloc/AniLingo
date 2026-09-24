using AniLingo.Web.Features.Subtitles;

namespace AniLingo.Tests;

[TestClass]
public sealed class LearningTextPreparationTests
{
    [TestMethod]
    public void FallbackPolicyUsesJimakuBeforeWhisperWhenConfigured()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                LearningTextFallbackStage.LocalSubtitle,
                LearningTextFallbackStage.EmbeddedSubtitle,
                LearningTextFallbackStage.Jimaku,
                LearningTextFallbackStage.Whisper
            },
            LearningTextFallbackPolicy.Build(jimakuConfigured: true).ToArray());
    }

    [TestMethod]
    public void FallbackPolicySkipsJimakuWhenNotConfigured()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                LearningTextFallbackStage.LocalSubtitle,
                LearningTextFallbackStage.EmbeddedSubtitle,
                LearningTextFallbackStage.Whisper
            },
            LearningTextFallbackPolicy.Build(jimakuConfigured: false).ToArray());
    }

    [TestMethod]
    public void JimakuMatcherPrefersFullAssForRequestedEpisode()
    {
        var selected = JimakuSubtitleMatcher.SelectBest(
            new[]
            {
                new JimakuSubtitleFileCandidate("Show - 02.ass", "https://jimaku.cc/2"),
                new JimakuSubtitleFileCandidate("Show - 01.srt", "https://jimaku.cc/1-srt"),
                new JimakuSubtitleFileCandidate("Show - 01 Signs.ass", "https://jimaku.cc/1-signs"),
                new JimakuSubtitleFileCandidate("Show - 01.ass", "https://jimaku.cc/1-ass")
            },
            episodeNumber: 1,
            episodeFiltered: false);

        Assert.IsNotNull(selected);
        Assert.AreEqual("Show - 01.ass", selected.Name);
    }

    [TestMethod]
    public void JimakuMatcherUnderstandsSeasonEpisodeNames()
    {
        Assert.IsTrue(JimakuSubtitleMatcher.MatchesEpisode(
            "[Group] Show S02E12v2 1080p.ass",
            12));

        Assert.IsFalse(JimakuSubtitleMatcher.MatchesEpisode(
            "[Group] Show S02E12v2 1080p.ass",
            11));
    }

    [TestMethod]
    public void JimakuFilteredResultDoesNotRequireEpisodeInFilename()
    {
        var selected = JimakuSubtitleMatcher.SelectBest(
            new[]
            {
                new JimakuSubtitleFileCandidate(
                    "Japanese dialogue.ass",
                    "https://jimaku.cc/subtitle")
            },
            episodeNumber: 7,
            episodeFiltered: true);

        Assert.IsNotNull(selected);
        Assert.AreEqual("Japanese dialogue.ass", selected.Name);
    }

    [TestMethod]
    public void JimakuUnfilteredResultRejectsWrongEpisode()
    {
        var selected = JimakuSubtitleMatcher.SelectBest(
            new[]
            {
                new JimakuSubtitleFileCandidate(
                    "Show - 08.ass",
                    "https://jimaku.cc/subtitle")
            },
            episodeNumber: 7,
            episodeFiltered: false);

        Assert.IsNull(selected);
    }
}
