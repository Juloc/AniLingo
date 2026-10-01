using Jularr.Web.Features.ClientApi;

namespace Jularr.Tests;

[TestClass]
public sealed class OfflinePortableRenditionTests
{
    [TestMethod]
    public void VideoRenditionIsH264AacMp4AndCapsItsCanvasAt720p()
    {
        var args = OfflinePortableRenditionService.BuildArguments("input.mkv", "video", "output.mp4");
        CollectionAssert.Contains(args.ToList(), "libx264");
        CollectionAssert.Contains(args.ToList(), "aac");
        CollectionAssert.Contains(args.ToList(), "yuv420p");
        CollectionAssert.Contains(args.ToList(), "scale=1280:720:force_original_aspect_ratio=decrease");
        CollectionAssert.Contains(args.ToList(), "+faststart");
        Assert.AreEqual("output.mp4", args[^1]);
    }

    [TestMethod]
    public void AudioRenditionUsesPortableAacMp4WithoutVideoMapping()
    {
        var args = OfflinePortableRenditionService.BuildArguments("input.flac", "audio", "output.m4a");
        CollectionAssert.Contains(args.ToList(), "aac");
        Assert.IsFalse(args.Contains("libx264"));
        CollectionAssert.Contains(args.ToList(), "0:a:0");
        Assert.AreEqual("output.m4a", args[^1]);
    }

    [TestMethod]
    public void SubtitleRenditionUsesWebVtt()
    {
        var args = OfflinePortableRenditionService.BuildArguments("input.ass", "subtitle", "output.vtt");
        CollectionAssert.Contains(args.ToList(), "webvtt");
        CollectionAssert.Contains(args.ToList(), "-c:s");
        Assert.AreEqual("output.vtt", args[^1]);
    }
}
