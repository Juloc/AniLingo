using AniLingo.Web.Features.Subtitles;

namespace AniLingo.Tests;

[TestClass]
public sealed class EmbeddedSubtitleExtractorTests
{
    [TestMethod]
    public void SelectsFullDefaultJapaneseTextTrackOverSignsAndImageSubtitles()
    {
        const string probeJson = """
        {
          "streams": [
            {
              "index": 2,
              "codec_name": "hdmv_pgs_subtitle",
              "tags": { "language": "jpn", "title": "Full PGS" },
              "disposition": { "default": 1, "forced": 0 }
            },
            {
              "index": 3,
              "codec_name": "ass",
              "tags": { "language": "jpn", "title": "Signs & Songs" },
              "disposition": { "default": 0, "forced": 0 }
            },
            {
              "index": 4,
              "codec_name": "ass",
              "tags": { "language": "jpn", "title": "Full Subtitles" },
              "disposition": { "default": 1, "forced": 0 }
            },
            {
              "index": 5,
              "codec_name": "ass",
              "tags": { "language": "eng", "title": "English" },
              "disposition": { "default": 0, "forced": 0 }
            }
          ]
        }
        """;

        var stream = EmbeddedSubtitleExtractor.SelectPreferredJapaneseTextStream(probeJson);

        Assert.IsNotNull(stream);
        Assert.AreEqual(4, stream.Index);
        Assert.AreEqual("ass", stream.Codec);
    }

    [TestMethod]
    public void UsesJapaneseTitleOnlyWhenLanguageTagIsMissingOrUndefined()
    {
        const string probeJson = """
        {
          "streams": [
            {
              "index": 7,
              "codec_name": "subrip",
              "tags": { "language": "und", "title": "日本語" },
              "disposition": { "default": 0, "forced": 0 }
            },
            {
              "index": 8,
              "codec_name": "subrip",
              "tags": { "language": "eng", "title": "Japanese" },
              "disposition": { "default": 1, "forced": 0 }
            }
          ]
        }
        """;

        var stream = EmbeddedSubtitleExtractor.SelectPreferredJapaneseTextStream(probeJson);

        Assert.IsNotNull(stream);
        Assert.AreEqual(7, stream.Index);
    }

    [TestMethod]
    public void ListsTextAndImageSubtitleStreamsForManualSelection()
    {
        const string probeJson = """
        {
          "streams": [
            {
              "index": 2,
              "codec_name": "hdmv_pgs_subtitle",
              "tags": { "language": "jpn", "title": "Japanese PGS" },
              "disposition": { "default": 1, "forced": 0 }
            },
            {
              "index": 3,
              "codec_name": "ass",
              "tags": { "language": "und", "title": "Full Dialogue" },
              "disposition": { "default": 0, "forced": 0 }
            },
            {
              "index": 4,
              "codec_name": "subrip",
              "tags": { "language": "eng", "title": "English" },
              "disposition": { "default": 0, "forced": 1 }
            }
          ]
        }
        """;

        var streams = EmbeddedSubtitleExtractor.ParseStreams(probeJson);

        Assert.AreEqual(3, streams.Count);

        Assert.AreEqual(2, streams[0].Index);
        Assert.IsFalse(streams[0].IsText);
        Assert.IsTrue(streams[0].IsDefault);

        Assert.AreEqual(3, streams[1].Index);
        Assert.IsTrue(streams[1].IsText);
        Assert.AreEqual("Full Dialogue", streams[1].Title);

        Assert.AreEqual(4, streams[2].Index);
        Assert.IsTrue(streams[2].IsText);
        Assert.IsTrue(streams[2].IsForced);
    }

    [TestMethod]
    public void EmbeddedSourceKeyIsStableAndScopedToTheStream()
    {
        var path = Path.Combine(Path.GetTempPath(), "Anime", "Episode 01.mkv");

        var first = EmbeddedSubtitleExtractor.BuildSourceKey(path, 4);
        var second = EmbeddedSubtitleExtractor.BuildSourceKey(path, 5);

        StringAssert.StartsWith(first, EmbeddedSubtitleExtractor.SourcePrefix);
        StringAssert.Contains(first, "#stream=4");
        Assert.AreNotEqual(first, second);
    }
}
