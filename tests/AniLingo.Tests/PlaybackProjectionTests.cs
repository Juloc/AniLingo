using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Playback;
using AniLingo.Web.Features.Vocabulary;

namespace AniLingo.Tests;

[TestClass]
public sealed class PlaybackProjectionTests
{
    [TestMethod]
    public void PreservesCueTextAndAddsMetadataOnlyForEpisodeTerms()
    {
        var termId = Guid.NewGuid();
        var projector = new PlaybackCueProjector(new FakeMorphology());
        var terms = new Dictionary<string, PlaybackTermInfo>(StringComparer.Ordinal)
        {
            ["猫"] = new(
                termId,
                "猫",
                "ねこ",
                "Katze",
                UserTermState.Known)
        };

        var cue = projector.Project(1000, 2500, "猫 が来た！", terms);

        Assert.AreEqual("猫 が来た！", string.Concat(cue.Tokens.Select(x => x.Surface)));

        var vocabularyToken = cue.Tokens.Single(x => x.TermId == termId);
        Assert.AreEqual("猫", vocabularyToken.Canonical);
        Assert.AreEqual("ねこ", vocabularyToken.Reading);
        Assert.AreEqual("Katze", vocabularyToken.Meaning);
        Assert.AreEqual("Known", vocabularyToken.State);

        Assert.IsFalse(cue.Tokens.Single(x => x.Surface == "が").IsVocabulary);
    }

    [TestMethod]
    public void DistinguishesDirectPlayContainersFromKnownNonBrowserContainers()
    {
        Assert.AreEqual("video/mp4", PlaybackMediaTypes.GetContentType("/media/test.MP4"));
        Assert.IsTrue(PlaybackMediaTypes.IsLikelyBrowserSupported("/media/test.mp4"));

        Assert.AreEqual("video/x-matroska", PlaybackMediaTypes.GetContentType("/media/test.mkv"));
        Assert.IsFalse(PlaybackMediaTypes.IsLikelyBrowserSupported("/media/test.mkv"));
    }

    private sealed class FakeMorphology : IJapaneseMorphology
    {
        public IReadOnlyList<JapaneseMorphToken> Analyze(string text) =>
        [
            new("猫", "猫", "ネコ", "名詞"),
            new("が", "が", "ガ", "助詞"),
            new("来た", "来る", "キタ", "動詞"),
            new("！", "！", "！", "記号")
        ];
    }
}
