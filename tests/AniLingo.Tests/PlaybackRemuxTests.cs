using AniLingo.Web.Features.Playback;

namespace AniLingo.Tests;

[TestClass]
public sealed class PlaybackRemuxTests
{
    [TestMethod]
    public void ParsesFirstVideoAndAudioStreams()
    {
        const string json = """
        {
          "streams": [
            { "codec_type": "video", "codec_name": "h264", "pix_fmt": "yuv420p" },
            { "codec_type": "audio", "codec_name": "flac" },
            { "codec_type": "audio", "codec_name": "aac" }
          ]
        }
        """;

        var probe = PlaybackMediaProbe.Parse(json);

        Assert.AreEqual("h264", probe.VideoCodec);
        Assert.AreEqual("yuv420p", probe.PixelFormat);
        Assert.AreEqual("flac", probe.AudioCodec);
    }

    [TestMethod]
    public void H264EightBitCopiesVideoAndConvertsNonAacAudio()
    {
        var plan = PlaybackRemuxPlan.Build(
            new PlaybackProbeResult("h264", "yuv420p", "flac"));

        Assert.IsTrue(plan.CanPrepare);
        Assert.AreEqual(PlaybackAudioMode.Aac, plan.AudioMode);
    }

    [TestMethod]
    public void H264WithAacCopiesAudio()
    {
        var plan = PlaybackRemuxPlan.Build(
            new PlaybackProbeResult("h264", "yuv420p", "aac"));

        Assert.IsTrue(plan.CanPrepare);
        Assert.AreEqual(PlaybackAudioMode.Copy, plan.AudioMode);
    }

    [TestMethod]
    public void H264TenBitRequiresVideoTranscode()
    {
        var plan = PlaybackRemuxPlan.Build(
            new PlaybackProbeResult("h264", "yuv420p10le", "aac"));

        Assert.IsFalse(plan.CanPrepare);
        StringAssert.Contains(plan.Message, "pixel format");
    }

    [TestMethod]
    public void HevcRequiresVideoTranscode()
    {
        var plan = PlaybackRemuxPlan.Build(
            new PlaybackProbeResult("hevc", "yuv420p10le", "aac"));

        Assert.IsFalse(plan.CanPrepare);
        StringAssert.Contains(plan.Message, "hevc");
    }

    [TestMethod]
    public void CacheIdentityChangesWithSourceFingerprint()
    {
        var mediaId = Guid.NewGuid();
        var time = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

        var first = PlaybackCache.BuildPath(mediaId, 1000, time);
        var changedSize = PlaybackCache.BuildPath(mediaId, 1001, time);
        var changedTime = PlaybackCache.BuildPath(mediaId, 1000, time.AddSeconds(1));

        StringAssert.StartsWith(first, PlaybackCache.RootPath);
        Assert.AreNotEqual(first, changedSize);
        Assert.AreNotEqual(first, changedTime);
    }
}
