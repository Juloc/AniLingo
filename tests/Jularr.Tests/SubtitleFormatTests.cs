using Jularr.Web.Features.Library;
using Jularr.Web.Features.Playback;
using Jularr.Web.Features.Progress;
using Jularr.Web.Features.Subtitles;

namespace Jularr.Tests;

[TestClass]
public sealed class SubtitleFormatTests
{
    [TestMethod]
    [DataRow("subrip", SubtitleFormatKind.Text, "SRT")]
    [DataRow("SRT", SubtitleFormatKind.Text, "SRT")]
    [DataRow("webvtt", SubtitleFormatKind.Text, "WebVTT")]
    [DataRow("mov_text", SubtitleFormatKind.Text, "MP4 text")]
    [DataRow("microdvd", SubtitleFormatKind.Text, "microdvd")]
    [DataRow("eia_608", SubtitleFormatKind.Text, "CEA-608")]
    [DataRow("ass", SubtitleFormatKind.StyledText, "ASS")]
    [DataRow("ssa", SubtitleFormatKind.StyledText, "SSA")]
    [DataRow("hdmv_pgs_subtitle", SubtitleFormatKind.Image, "PGS")]
    [DataRow("dvd_subtitle", SubtitleFormatKind.Image, "VobSub")]
    [DataRow("dvb_subtitle", SubtitleFormatKind.Image, "DVB")]
    [DataRow("xsub", SubtitleFormatKind.Image, "XSUB")]
    [DataRow("dvb_teletext", SubtitleFormatKind.Unsupported, "dvb_teletext")]
    [DataRow("hdmv_text_subtitle", SubtitleFormatKind.Unsupported, "hdmv_text_subtitle")]
    [DataRow("arib_caption", SubtitleFormatKind.Unsupported, "arib_caption")]
    [DataRow("ttml", SubtitleFormatKind.Unsupported, "ttml")]
    public void CodecsAreClassifiedByHowAPlayerCanShowThem(string codec, SubtitleFormatKind kind, string name)
    {
        Assert.AreEqual(kind, SubtitleFormats.Classify(codec));
        Assert.AreEqual(kind is SubtitleFormatKind.Text or SubtitleFormatKind.StyledText, SubtitleFormats.IsText(codec));
        Assert.AreEqual(kind == SubtitleFormatKind.Image, SubtitleFormats.IsImage(codec));
        Assert.AreEqual(name, SubtitleFormats.DisplayName(codec));
    }

    [TestMethod]
    public void MissingCodecIsUnsupportedAndNeverText()
    {
        Assert.AreEqual(SubtitleFormatKind.Unsupported, SubtitleFormats.Classify(null));
        Assert.IsNull(SubtitleFormats.DisplayName(null));
        var stream = new MediaStreamInfo(3, MediaStreamKind.Subtitle, null, "eng", null, null, null, false, false);
        Assert.IsFalse(stream.IsText);
        Assert.IsTrue((stream with { Codec = "mpl2" }).IsText, "Every ffmpeg text decoder extracts to cues.");
        Assert.IsFalse((stream with { Codec = "hdmv_pgs_subtitle" }).IsText);
    }

    [TestMethod]
    public void PlayerMenuMarksPictureSubtitlesAndDisablesUnsupportedOnes()
    {
        var tracks = new List<PlaybackMediaTrack>
        {
            new(1, PlaybackTrackKind.Audio, "aac", "jpn", null, true, false, false),
            new(2, PlaybackTrackKind.Subtitle, "hdmv_pgs_subtitle", "eng", "PGS", false, false, false),
            new(3, PlaybackTrackKind.Subtitle, "dvd_subtitle", "ger", null, false, false, false),
            new(4, PlaybackTrackKind.Subtitle, "ass", "jpn", "Styled", false, false, true),
            new(5, PlaybackTrackKind.Subtitle, "dvb_teletext", "fin", null, false, false, false)
        };
        var media = PlayerControlsTests.Media("episode.mkv", "h264", "yuv420p", "aac", 1080) with { Tracks = tracks };

        var controls = PlayerControls.Build(media, hasLearningCues: false, learningSourceStreamIndex: null,
            new PlaybackPreferencesSnapshot(false, null, null));

        var byId = controls.SubtitleTracks.ToDictionary(x => x.Id);
        Assert.IsTrue(byId["stream:2"].IsImage && byId["stream:2"].IsSelectable);
        Assert.AreEqual("PGS", byId["stream:2"].Format);
        Assert.IsTrue(byId["stream:3"].IsImage);
        Assert.AreEqual("VobSub", byId["stream:3"].Format);
        Assert.IsFalse(byId["stream:4"].IsImage);
        Assert.AreEqual("ASS", byId["stream:4"].Format);
        Assert.IsFalse(byId["stream:5"].IsSelectable, "Teletext cannot be shown and is not offered.");
        Assert.IsFalse(byId.Values.Any(x => x.Label.Contains("image", StringComparison.Ordinal)),
            "The burned-in note is localized by the page, not baked into the label.");
    }
}
