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
    public void H264EightBitUsesUniversalRemuxWithoutVideoEncoding()
    {
        var plan = PlaybackPreparationPlan.Build(
            new PlaybackProbeResult("h264", "yuv420p", "flac"),
            PlaybackRequestedMode.Device);

        Assert.IsTrue(plan.CanPrepare);
        Assert.AreEqual(PlaybackPreparationKind.CompatibleRemux, plan.Kind);
        Assert.AreEqual(PlaybackVideoMode.Copy, plan.VideoMode);
        Assert.AreEqual(PlaybackAudioMode.Aac, plan.AudioMode);
        Assert.IsFalse(plan.TagHevcAsHvc1);
    }

    [TestMethod]
    public void H264WithAacCopiesAudio()
    {
        var plan = PlaybackPreparationPlan.Build(
            new PlaybackProbeResult("h264", "yuv420p", "aac"),
            PlaybackRequestedMode.Server);

        Assert.IsTrue(plan.CanPrepare);
        Assert.AreEqual(PlaybackPreparationKind.CompatibleRemux, plan.Kind);
        Assert.AreEqual(PlaybackVideoMode.Copy, plan.VideoMode);
        Assert.AreEqual(PlaybackAudioMode.Copy, plan.AudioMode);
    }

    [TestMethod]
    public void H264TenBitRejectsDevicePathButServerCanTranscode()
    {
        var probe = new PlaybackProbeResult("h264", "yuv420p10le", "aac");

        var device = PlaybackPreparationPlan.Build(probe, PlaybackRequestedMode.Device);
        var server = PlaybackPreparationPlan.Build(probe, PlaybackRequestedMode.Server);

        Assert.IsFalse(device.CanPrepare);
        Assert.IsTrue(server.CanPrepare);
        Assert.AreEqual(PlaybackPreparationKind.ServerH264Transcode, server.Kind);
        Assert.AreEqual(PlaybackVideoMode.H264, server.VideoMode);
    }

    [TestMethod]
    public void HevcDevicePathKeepsVideoWhileServerFallbackUsesH264()
    {
        var probe = new PlaybackProbeResult("hevc", "yuv420p10le", "aac");

        var device = PlaybackPreparationPlan.Build(probe, PlaybackRequestedMode.Device);
        var server = PlaybackPreparationPlan.Build(probe, PlaybackRequestedMode.Server);

        Assert.IsTrue(device.CanPrepare);
        Assert.AreEqual(PlaybackPreparationKind.DeviceHevcRemux, device.Kind);
        Assert.AreEqual(PlaybackVideoMode.Copy, device.VideoMode);
        Assert.IsTrue(device.TagHevcAsHvc1);

        Assert.IsTrue(server.CanPrepare);
        Assert.AreEqual(PlaybackPreparationKind.ServerH264Transcode, server.Kind);
        Assert.AreEqual(PlaybackVideoMode.H264, server.VideoMode);
        Assert.IsFalse(server.TagHevcAsHvc1);
    }

    [TestMethod]
    public void UnsupportedDeviceCodecStillHasServerFallback()
    {
        var probe = new PlaybackProbeResult("av1", "yuv420p10le", "opus");

        var device = PlaybackPreparationPlan.Build(probe, PlaybackRequestedMode.Device);
        var server = PlaybackPreparationPlan.Build(probe, PlaybackRequestedMode.Server);

        Assert.IsFalse(device.CanPrepare);
        Assert.IsTrue(server.CanPrepare);
        Assert.AreEqual(PlaybackVideoMode.H264, server.VideoMode);
        Assert.AreEqual(PlaybackAudioMode.Aac, server.AudioMode);
    }

    [TestMethod]
    public void CacheIdentitySeparatesSourceFingerprintAndPreparationKind()
    {
        var mediaId = Guid.NewGuid();
        var time = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

        var compatible = PlaybackCache.BuildPath(
            mediaId,
            1000,
            time,
            PlaybackPreparationKind.CompatibleRemux);
        var changedSize = PlaybackCache.BuildPath(
            mediaId,
            1001,
            time,
            PlaybackPreparationKind.CompatibleRemux);
        var changedTime = PlaybackCache.BuildPath(
            mediaId,
            1000,
            time.AddSeconds(1),
            PlaybackPreparationKind.CompatibleRemux);
        var hevc = PlaybackCache.BuildPath(
            mediaId,
            1000,
            time,
            PlaybackPreparationKind.DeviceHevcRemux);
        var server = PlaybackCache.BuildPath(
            mediaId,
            1000,
            time,
            PlaybackPreparationKind.ServerH264Transcode);

        StringAssert.StartsWith(compatible, PlaybackCache.RootPath);
        Assert.AreNotEqual(compatible, changedSize);
        Assert.AreNotEqual(compatible, changedTime);
        Assert.AreNotEqual(compatible, hevc);
        Assert.AreNotEqual(hevc, server);
    }
}
