using AniLingo.Web.Features.Library;
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

        var probe = PlaybackProbeResult.From(MediaProbeParser.Parse(json));

        Assert.AreEqual("h264", probe.VideoCodec);
        Assert.AreEqual("yuv420p", probe.PixelFormat);
        Assert.AreEqual("flac", probe.AudioCodec);
    }

    [TestMethod]
    public void ParsesSourceDurationFromFormat()
    {
        const string json = """
        {
          "streams": [
            { "codec_type": "video", "codec_name": "h264", "pix_fmt": "yuv420p" }
          ],
          "format": {
            "duration": "1440.250"
          }
        }
        """;

        var probe = PlaybackProbeResult.From(MediaProbeParser.Parse(json));

        Assert.IsNotNull(probe.DurationSeconds);
        Assert.AreEqual(1440.25, probe.DurationSeconds.Value, 0.001);
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
    public void CompatibleRemuxStreamsFragmentedMp4WithoutVideoEncoding()
    {
        var plan = PlaybackPreparationPlan.Build(
            new PlaybackProbeResult("h264", "yuv420p", "flac"),
            PlaybackRequestedMode.Device);

        var arguments = LivePlaybackCommand.BuildArguments(
            "/media/anime/episode.mkv",
            plan);

        CollectionAssert.Contains(arguments.ToList(), "pipe:1");
        CollectionAssert.Contains(arguments.ToList(), "+frag_keyframe+empty_moov+default_base_moof");
        CollectionAssert.Contains(arguments.ToList(), "copy");
        CollectionAssert.Contains(arguments.ToList(), "aac");
        CollectionAssert.DoesNotContain(arguments.ToList(), "libx264");
        CollectionAssert.DoesNotContain(arguments.ToList(), "-ss");
    }

    [TestMethod]
    public void InstantPlaybackSeekStartsFfmpegNearRequestedPosition()
    {
        var plan = PlaybackPreparationPlan.Build(
            new PlaybackProbeResult("av1", "yuv420p10le", "opus"),
            PlaybackRequestedMode.Server);

        var arguments = LivePlaybackCommand.BuildArguments(
            "/media/anime/episode.mkv",
            plan,
            1080.5).ToList();

        var seekIndex = arguments.IndexOf("-ss");
        var inputIndex = arguments.IndexOf("-i");

        Assert.IsTrue(seekIndex >= 0);
        Assert.IsTrue(inputIndex > seekIndex);
        Assert.AreEqual("1080.5", arguments[seekIndex + 1]);
        CollectionAssert.Contains(arguments, "make_zero");
    }

    [TestMethod]
    public void ServerFallbackStreamsH264Immediately()
    {
        var plan = PlaybackPreparationPlan.Build(
            new PlaybackProbeResult("av1", "yuv420p10le", "opus"),
            PlaybackRequestedMode.Server);

        var arguments = LivePlaybackCommand.BuildArguments(
            "/media/anime/episode.mkv",
            plan);

        CollectionAssert.Contains(arguments.ToList(), "libx264");
        CollectionAssert.Contains(arguments.ToList(), "yuv420p");
        CollectionAssert.Contains(arguments.ToList(), "pipe:1");
    }


    [TestMethod]
    public void HlsFallbackUsesBoundedFmp4SegmentsAndRestartPosition()
    {
        var arguments = HlsPlaybackSessionManager.BuildArguments(
            "/media/anime/episode.mkv",
            "/data/playback-cache/hls/session",
            512.25).ToList();

        var seekIndex = arguments.IndexOf("-ss");
        var inputIndex = arguments.IndexOf("-i");

        Assert.IsTrue(seekIndex >= 0);
        Assert.IsTrue(inputIndex > seekIndex);
        Assert.AreEqual("512.25", arguments[seekIndex + 1]);
        CollectionAssert.Contains(arguments, "libx264");
        CollectionAssert.Contains(arguments, "aac");
        CollectionAssert.Contains(arguments, "fmp4");
        CollectionAssert.Contains(arguments, "delete_segments+independent_segments");
        CollectionAssert.Contains(arguments, HlsPlaybackSessionManager.PlaylistSegments.ToString());
        Assert.IsTrue(
            arguments.Any(x =>
                x.Replace('\\', '/')
                    .EndsWith(
                        "/data/playback-cache/hls/session/segment-%05d.m4s",
                        StringComparison.Ordinal)));
        StringAssert.EndsWith(
            arguments[^1].Replace('\\', '/'),
            "/data/playback-cache/hls/session/index.m3u8");
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
