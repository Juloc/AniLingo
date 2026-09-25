using AniLingo.Web.Features.Acquisition;

namespace AniLingo.Tests;

[TestClass]
public sealed class AnimeReleaseParserTests
{
    [TestMethod]
    public void ParsesSonarrStyleSingleEpisodeWithLanguagesAndProviderId()
    {
        const string input =
            "The Series Title's! - S01E01 - Episode Title (1) WEBDL-1080p Proper AVC DTS[DE] [EN+DE] RlsGrp tt12345";

        var release = AnimeReleaseParser.Parse(input);

        Assert.AreEqual("The Series Title's!", release.SeriesTitle);
        Assert.AreEqual(1, release.SeasonNumber);
        Assert.AreEqual(1, release.EpisodeStart);
        Assert.AreEqual(1, release.EpisodeEnd);
        Assert.AreEqual(1080, release.Resolution);
        Assert.AreEqual(AnimeReleaseSource.WebDl, release.Source);
        Assert.AreEqual(AnimeVideoCodec.Avc, release.VideoCodec);
        Assert.AreEqual(AnimeAudioCodec.Dts, release.AudioCodec);
        CollectionAssert.AreEqual(new[] { "DE" }, release.AudioLanguages.ToArray());
        CollectionAssert.AreEqual(new[] { "EN", "DE" }, release.SubtitleLanguages.ToArray());
        Assert.IsTrue(release.IsProper);
        Assert.AreEqual("RlsGrp", release.ReleaseGroup);
        Assert.AreEqual("tt12345", release.ImdbId);
        Assert.IsFalse(release.IsMultiEpisode);
        Assert.IsTrue(release.Confidence >= 0.8);
    }

    [TestMethod]
    public void ParsesPrefixedMultiEpisodeRange()
    {
        const string input =
            "The Series Title's! - S01E01-E03 - Episode Title WEBDL-1080p Proper AVC DTS[DE] [EN+DE] RlsGrp tt12345";

        var release = AnimeReleaseParser.Parse(input);

        Assert.AreEqual(1, release.SeasonNumber);
        Assert.AreEqual(1, release.EpisodeStart);
        Assert.AreEqual(3, release.EpisodeEnd);
        Assert.IsTrue(release.IsMultiEpisode);
    }

    [TestMethod]
    public void ParsesAnimeAbsoluteNumberAndLeadingReleaseGroup()
    {
        const string input = "[SubsPlease] Sousou no Frieren - 03 (1080p) [ABCDEF12].mkv";

        var release = AnimeReleaseParser.Parse(input);

        Assert.AreEqual("Sousou no Frieren", release.SeriesTitle);
        Assert.AreEqual(3, release.AbsoluteEpisodeStart);
        Assert.AreEqual(3, release.AbsoluteEpisodeEnd);
        Assert.AreEqual("SubsPlease", release.ReleaseGroup);
        Assert.AreEqual(1080, release.Resolution);
        Assert.IsNull(release.SeasonNumber);
        Assert.IsNull(release.EpisodeStart);
    }

    [TestMethod]
    public void ParsesAnimeRevisionCodecBitDepthAndHdr()
    {
        const string input = "[Group] Anime Name - 13v2 WEB-DL 1080p HEVC 10bit HDR10 AAC[JA] [EN+DE]";

        var release = AnimeReleaseParser.Parse(input);

        Assert.AreEqual(13, release.AbsoluteEpisodeStart);
        Assert.AreEqual(2, release.Version);
        Assert.AreEqual(AnimeVideoCodec.Hevc, release.VideoCodec);
        Assert.AreEqual(10, release.BitDepth);
        Assert.AreEqual(AnimeHdrFormat.Hdr10, release.HdrFormat);
        Assert.AreEqual(AnimeAudioCodec.Aac, release.AudioCodec);
        CollectionAssert.AreEqual(new[] { "JA" }, release.AudioLanguages.ToArray());
        CollectionAssert.AreEqual(new[] { "EN", "DE" }, release.SubtitleLanguages.ToArray());
    }

    [TestMethod]
    public void ParsesSeasonPackWithoutInventingEpisodes()
    {
        const string input = "[Group] Anime Name S02 Complete BluRay 1080p x265 FLAC 5.1";

        var release = AnimeReleaseParser.Parse(input);

        Assert.AreEqual("Anime Name", release.SeriesTitle);
        Assert.AreEqual(2, release.SeasonNumber);
        Assert.IsTrue(release.IsSeasonPack);
        Assert.IsNull(release.EpisodeStart);
        Assert.IsNull(release.AbsoluteEpisodeStart);
        Assert.AreEqual(AnimeReleaseSource.BluRay, release.Source);
        Assert.AreEqual(AnimeVideoCodec.Hevc, release.VideoCodec);
        Assert.AreEqual(AnimeAudioCodec.Flac, release.AudioCodec);
        Assert.AreEqual("5.1", release.AudioChannels);
    }

    [TestMethod]
    public void ParsesDailyEpisodeDate()
    {
        const string input = "The Series Title - 2013-10-30 - Episode Title WEBDL-1080p";

        var release = AnimeReleaseParser.Parse(input);

        Assert.AreEqual(new DateOnly(2013, 10, 30), release.AirDate);
        Assert.AreEqual("The Series Title", release.SeriesTitle);
        Assert.IsNull(release.EpisodeStart);
        Assert.IsNull(release.AbsoluteEpisodeStart);
    }

    [TestMethod]
    public void KeepsUnknownTechnicalFieldsUnknown()
    {
        const string input = "Anime Name - 07";

        var release = AnimeReleaseParser.Parse(input);

        Assert.AreEqual("Anime Name", release.SeriesTitle);
        Assert.AreEqual(7, release.AbsoluteEpisodeStart);
        Assert.AreEqual(AnimeReleaseSource.Unknown, release.Source);
        Assert.AreEqual(AnimeVideoCodec.Unknown, release.VideoCodec);
        Assert.AreEqual(AnimeAudioCodec.Unknown, release.AudioCodec);
        Assert.IsNull(release.Resolution);
        Assert.IsNull(release.BitDepth);
    }

    [TestMethod]
    public void ReleaseKeyIsStableAcrossWhitespaceAndCase()
    {
        var first = AnimeReleaseParser.Parse("[Group] Anime Name - 01 WEB-DL 1080p");
        var second = AnimeReleaseParser.Parse("[GROUP]   ANIME NAME - 01 WEB-DL 1080p");

        Assert.AreEqual(first.ReleaseKey, second.ReleaseKey);
    }

    [TestMethod]
    public void TryParseRejectsBlankInput()
    {
        Assert.IsFalse(AnimeReleaseParser.TryParse("  ", out _));
    }

    [TestMethod]
    public void EvidenceExplainsDetectedFields()
    {
        var release = AnimeReleaseParser.Parse("[Group] Anime - 01 WEB-DL 1080p x265 AAC[JA]");

        Assert.IsTrue(release.Evidence.Any(item => item.Field == "releaseGroup" && item.Value == "Group"));
        Assert.IsTrue(release.Evidence.Any(item => item.Field == "absoluteEpisodeRange" && item.Value == "1"));
        Assert.IsTrue(release.Evidence.Any(item => item.Field == "source" && item.Value == nameof(AnimeReleaseSource.WebDl)));
        Assert.IsTrue(release.Evidence.Any(item => item.Field == "videoCodec" && item.Value == nameof(AnimeVideoCodec.Hevc)));
        Assert.IsTrue(release.Evidence.Any(item => item.Field == "audioLanguages" && item.Value == "JA"));
    }
}
