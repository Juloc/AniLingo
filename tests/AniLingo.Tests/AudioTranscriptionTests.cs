using AniLingo.Web.Features.Library;
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
            { "index": 1, "codec_type": "audio", "tags": { "language": "eng", "title": "English" } },
            { "index": 2, "codec_type": "audio", "tags": { "language": "jpn", "title": "Japanese" } }
          ]
        }
        """;

        Assert.AreEqual(
            2,
            EmbeddedSubtitleExtractor.SelectPreferredJapaneseAudioStreamIndex(
                MediaProbeParser.Parse(json).AudioStreams));
    }

    [TestMethod]
    public void FallsBackToFirstAudioStreamWhenTagsAreMissing()
    {
        const string json = """
        {
          "streams": [
            { "index": 3, "codec_type": "audio" },
            { "index": 4, "codec_type": "audio", "tags": { "language": "eng" } }
          ]
        }
        """;

        Assert.AreEqual(
            3,
            EmbeddedSubtitleExtractor.SelectPreferredJapaneseAudioStreamIndex(
                MediaProbeParser.Parse(json).AudioStreams));
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
