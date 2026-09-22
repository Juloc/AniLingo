using AniLingo.Web.Features.Subtitles;

namespace AniLingo.Tests;

[TestClass]
public sealed class AudioTranscriptionTests
{
    [TestMethod]
    public void PrefersJapaneseTaggedAudioStream()
    {
        const string json = """
        {
          "streams": [
            { "index": 1, "tags": { "language": "eng", "title": "English" } },
            { "index": 2, "tags": { "language": "jpn", "title": "Japanese" } }
          ]
        }
        """;

        Assert.AreEqual(
            2,
            EmbeddedSubtitleExtractor.SelectPreferredJapaneseAudioStreamIndex(json));
    }

    [TestMethod]
    public void FallsBackToFirstAudioStreamWhenTagsAreMissing()
    {
        const string json = """
        {
          "streams": [
            { "index": 3 },
            { "index": 4, "tags": { "language": "eng" } }
          ]
        }
        """;

        Assert.AreEqual(
            3,
            EmbeddedSubtitleExtractor.SelectPreferredJapaneseAudioStreamIndex(json));
    }

    [TestMethod]
    public void TranscriptionCacheKeyTracksSourceFingerprint()
    {
        var time = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

        var first = EmbeddedSubtitleExtractor.BuildTranscriptionCacheKey(
            "/media/anime/show/episode.mkv",
            1000,
            time);
        var changedSize = EmbeddedSubtitleExtractor.BuildTranscriptionCacheKey(
            "/media/anime/show/episode.mkv",
            1001,
            time);
        var changedTime = EmbeddedSubtitleExtractor.BuildTranscriptionCacheKey(
            "/media/anime/show/episode.mkv",
            1000,
            time.AddSeconds(1));

        Assert.AreNotEqual(first, changedSize);
        Assert.AreNotEqual(first, changedTime);
        Assert.AreEqual(64, first.Length);
    }

    [TestMethod]
    public void TranscriptionSourceIsSeparateFromEmbeddedTracks()
    {
        var source = EmbeddedSubtitleExtractor.BuildTranscriptionSourceKey(
            "/media/anime/show/episode.mkv");

        StringAssert.StartsWith(
            source,
            EmbeddedSubtitleExtractor.TranscriptionSourcePrefix);
        StringAssert.Contains(source, "#audio=ja");
    }
}
