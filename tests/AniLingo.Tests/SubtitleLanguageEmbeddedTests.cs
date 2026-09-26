using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Subtitles;

namespace AniLingo.Tests;

// Embedded-track preference generalized by target language: EmbeddedSubtitleExtractorTests.cs
// keeps the Japanese-only static helpers (SelectPreferredJapaneseTextStream, IsJapanese)
// covered unchanged; these tests cover the same selection through the generalized
// SelectPreferredTextStream/MatchesLanguage for de and id targets.
[TestClass]
public sealed class SubtitleLanguageEmbeddedTests
{
    [TestMethod]
    public void PrefersGermanTaggedTextStreamOverOtherLanguages()
    {
        const string probeJson = """
        {
          "streams": [
            {
              "index": 2,
              "codec_type": "subtitle",
              "codec_name": "subrip",
              "tags": { "language": "eng", "title": "English" },
              "disposition": { "default": 0, "forced": 0 }
            },
            {
              "index": 3,
              "codec_type": "subtitle",
              "codec_name": "ass",
              "tags": { "language": "ger", "title": "Full Subtitles" },
              "disposition": { "default": 1, "forced": 0 }
            }
          ]
        }
        """;

        var stream = EmbeddedSubtitleExtractor.SelectPreferredTextStream(
            MediaProbeParser.Parse(probeJson).SubtitleStreams,
            "de");

        Assert.IsNotNull(stream);
        Assert.AreEqual(3, stream.Index);
    }

    [TestMethod]
    public void PrefersIndonesianTaggedTextStreamOverOtherLanguages()
    {
        const string probeJson = """
        {
          "streams": [
            {
              "index": 4,
              "codec_type": "subtitle",
              "codec_name": "subrip",
              "tags": { "language": "jpn", "title": "Japanese" },
              "disposition": { "default": 1, "forced": 0 }
            },
            {
              "index": 5,
              "codec_type": "subtitle",
              "codec_name": "subrip",
              "tags": { "language": "ind", "title": "Indonesian" },
              "disposition": { "default": 0, "forced": 0 }
            }
          ]
        }
        """;

        var stream = EmbeddedSubtitleExtractor.SelectPreferredTextStream(
            MediaProbeParser.Parse(probeJson).SubtitleStreams,
            "id");

        Assert.IsNotNull(stream);
        Assert.AreEqual(5, stream.Index);
    }

    [TestMethod]
    public void FallsBackToUndefinedTaggedTitleMatchForTheTargetLanguage()
    {
        const string probeJson = """
        {
          "streams": [
            {
              "index": 6,
              "codec_type": "subtitle",
              "codec_name": "subrip",
              "tags": { "language": "und", "title": "Deutsch" },
              "disposition": { "default": 0, "forced": 0 }
            },
            {
              "index": 7,
              "codec_type": "subtitle",
              "codec_name": "subrip",
              "tags": { "language": "eng", "title": "German" },
              "disposition": { "default": 1, "forced": 0 }
            }
          ]
        }
        """;

        var stream = EmbeddedSubtitleExtractor.SelectPreferredTextStream(
            MediaProbeParser.Parse(probeJson).SubtitleStreams,
            "de");

        Assert.IsNotNull(stream);
        Assert.AreEqual(6, stream.Index);
    }

    [TestMethod]
    public void ReturnsNullWhenNoStreamMatchesTheTargetLanguage()
    {
        const string probeJson = """
        {
          "streams": [
            {
              "index": 8,
              "codec_type": "subtitle",
              "codec_name": "subrip",
              "tags": { "language": "eng", "title": "English" },
              "disposition": { "default": 1, "forced": 0 }
            }
          ]
        }
        """;

        var stream = EmbeddedSubtitleExtractor.SelectPreferredTextStream(
            MediaProbeParser.Parse(probeJson).SubtitleStreams,
            "de");

        Assert.IsNull(stream);
    }
}
