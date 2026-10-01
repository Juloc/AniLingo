using Jularr.Web.Features.Admin;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Operations;
using Jularr.Web.Features.Playback.Decision;

namespace Jularr.Tests;

/// <summary>The figures behind the Admin dashboard: Jularr plus PostgreSQL cgroup readings and formatting.</summary>
[TestClass]
public sealed class AdminDashboardTests
{
    [TestMethod]
    public void CgroupV2ParsersAcceptOnlyNonNegativeUsageValues()
    {
        Assert.AreEqual(42_000L, CgroupV2Parser.ParseCpuUsageMicroseconds("user_usec 100\nusage_usec 42000\n"));
        Assert.AreEqual(1_024L, CgroupV2Parser.ParseMemoryBytes("1024\n"));
        Assert.IsNull(CgroupV2Parser.ParseCpuUsageMicroseconds("usage_usec -1"));
        Assert.IsNull(CgroupV2Parser.ParseMemoryBytes("not-a-number"));
        var io = CgroupV2Parser.ParseIoBytes("8:0 rbytes=123 wbytes=456 rios=1 wios=2\n");
        Assert.AreEqual(123L, io!.ReadBytes);
        Assert.AreEqual(456L, io.WriteBytes);
        Assert.IsNull(CgroupV2Parser.ParseIoBytes("8:0 rios=1 wios=2"));
    }

    [TestMethod]
    public void ContainerMountsSelectTheFilesystemAtTheDeepestVisibleMount()
    {
        var mounts = ContainerMounts.Parse(
        [
            "24 23 0:21 / / rw,relatime - overlay overlay rw",
            "25 24 0:22 / /media rw,relatime - ext4 /dev/sda1 rw",
            "26 25 0:23 / /media/archive rw,relatime - cifs //nas/archive rw"
        ]);

        Assert.AreEqual("cifs", mounts.Where(mount => "/media/archive/shows".StartsWith(mount.MountPoint, StringComparison.Ordinal)).OrderByDescending(mount => mount.MountPoint.Length).First().FileSystemType);
    }

    [TestMethod]
    public async Task StackSamplerKeepsJularrAndPostgresSeparateAndCalculatesCpuRates()
    {
        var source = new FakeResourceSource();
        var sampler = new StackResourceTelemetrySampler(source, TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<StackResourceTelemetrySampler>.Instance);

        var first = await sampler.SampleAsync(CancellationToken.None);
        await Task.Delay(20);
        var second = await sampler.SampleAsync(CancellationToken.None);

        Assert.IsNull(first.Jularr!.CpuPercent, "The first reading only establishes CPU baselines.");
        Assert.AreEqual(100, first.Jularr.MemoryBytes);
        Assert.AreEqual(200, first.PostgreSql!.MemoryBytes);
        Assert.IsTrue(second.Jularr!.CpuPercent is > 0);
        Assert.IsTrue(second.PostgreSql!.CpuPercent is > 0);
        Assert.IsTrue(second.Jularr.ReadBytesPerSecond is > 0);
        Assert.IsTrue(second.Jularr.WriteBytesPerSecond is > 0);
        Assert.AreEqual(300, second.TotalMemoryBytes);
    }

    [TestMethod]
    public void RatesReadInMegabytesAndKilobytesPerSecond()
    {
        var ui = UiTextBundle.English;

        Assert.AreEqual("48.5 MB/s", AdminDashboardFormat.Rate(ui, 48_500_000));
        Assert.AreEqual("12 MB/s", AdminDashboardFormat.Rate(ui, 12_000_000));
        Assert.AreEqual("640 KB/s", AdminDashboardFormat.Rate(ui, 640_000));
        Assert.AreEqual("0 KB/s", AdminDashboardFormat.Rate(ui, -5));
    }

    [TestMethod]
    public void DurationsReadInTheLargestTwoUnits()
    {
        var ui = UiTextBundle.English;

        Assert.AreEqual("3 d 4 h", AdminDashboardFormat.Duration(ui, TimeSpan.FromHours(76.5)));
        Assert.AreEqual("5 h 12 min", AdminDashboardFormat.Duration(ui, TimeSpan.FromMinutes(312)));
        Assert.AreEqual("8 min", AdminDashboardFormat.Duration(ui, TimeSpan.FromMinutes(8.4)));
        Assert.AreEqual("1 min", AdminDashboardFormat.Duration(ui, TimeSpan.FromSeconds(10)));
    }

    [TestMethod]
    public void ProgressComesFromThePercentOrTheBytes()
    {
        Assert.AreEqual(68, AdminDashboardFormat.Progress(Operation(progress: 68)));
        Assert.AreEqual(25, AdminDashboardFormat.Progress(Operation(bytesTotal: 400, bytesCompleted: 100)));
        Assert.IsNull(AdminDashboardFormat.Progress(Operation()));
        Assert.AreEqual(100, AdminDashboardFormat.Progress(Operation(progress: 250)));
    }

    [TestMethod]
    public void AnEtaOnlyShowsWhileRunningAndInTheFuture()
    {
        var ui = UiTextBundle.English;
        var now = new DateTime(2026, 9, 30, 9, 0, 0, DateTimeKind.Utc);

        Assert.AreEqual("12 min", AdminDashboardFormat.Eta(ui, Operation(eta: now.AddMinutes(12)), now));
        Assert.IsNull(AdminDashboardFormat.Eta(ui, Operation(eta: now.AddMinutes(-1)), now));
        Assert.IsNull(AdminDashboardFormat.Eta(ui, Operation(eta: now.AddMinutes(5), status: OperationStatus.Queued), now));
        Assert.IsNull(AdminDashboardFormat.Eta(ui, Operation(), now));
    }

    [TestMethod]
    public void TheDownloadClientKeepsTheProductsSpelling()
    {
        Assert.AreEqual("SABnzbd", AdminDashboardFormat.ClientName("sabnzbd"));
        Assert.AreEqual("other", AdminDashboardFormat.ClientName("other"));
        Assert.IsNull(AdminDashboardFormat.ClientName(" "));
    }

    [TestMethod]
    public void VideoShowsTheConversionOnlyWhenTheVideoIsReEncoded()
    {
        Assert.AreEqual("2160p HEVC → 1080p H264", AdminDashboardFormat.Video(Plan(new PlaybackVideoOutput(false, "hevc", "h264", 3840, 2160, 1080, null, null))));
        Assert.AreEqual("1080p H264", AdminDashboardFormat.Video(Plan(new PlaybackVideoOutput(true, "h264", "h264", 1920, 1080, null, null, null))));
        Assert.AreEqual("1080p HEVC → 1080p H264", AdminDashboardFormat.Video(Plan(new PlaybackVideoOutput(false, "hevc", "h264", 1920, 1080, 2160, null, null))));
        Assert.IsNull(AdminDashboardFormat.Video(Plan(null)));
    }

    [TestMethod]
    public void SparklinesNeedTwoValuesAndStayInsideTheBox()
    {
        Assert.IsNull(AdminDashboardFormat.SparklinePoints([1], 100));
        Assert.IsNull(AdminDashboardFormat.SparklinePoints([1, 2], 0));

        var points = AdminDashboardFormat.SparklinePoints([0, 50, 100, 250], 100, width: 90, height: 32);

        Assert.AreEqual("0,30 30,16 60,2 90,2", points);
    }

    private static OperationSnapshot Operation(
        int? progress = null,
        long? bytesTotal = null,
        long? bytesCompleted = null,
        DateTime? eta = null,
        OperationStatus status = OperationStatus.Running) =>
        new(
            Guid.NewGuid(),
            "library-scan",
            "Library",
            OperationLane.Normal,
            status,
            null,
            "Title",
            null,
            progress,
            null,
            null,
            false,
            bytesTotal,
            bytesCompleted,
            null,
            eta,
            1,
            true,
            null,
            null,
            DateTime.UtcNow,
            null,
            null,
            DateTime.UtcNow);

    private static PlaybackPlan Plan(PlaybackVideoOutput? video) =>
        new(
            PlaybackDeliveryMode.Transcode,
            PlaybackTransport.File,
            "mp4",
            video,
            Audio: null,
            new PlaybackQualityResolution(PlaybackQualityPreset.Auto, PlaybackNetworkClass.Local, null, PlaybackLimitSource.None, null, null),
            [],
            PlaybackCapabilitySupport.Confirmed);

    private sealed class FakeResourceSource : IStackResourceSource
    {
        private int sample;

        public Task<CgroupResourceUsage?> ReadJularrAsync(CancellationToken cancellationToken)
        {
            var value = Interlocked.Increment(ref sample) * 10_000L;
            return Task.FromResult<CgroupResourceUsage?>(new CgroupResourceUsage(value, 100, value, value / 2, value / 4, value / 8));
        }

        public Task<CgroupResourceUsage?> ReadPostgreSqlAsync(CancellationToken cancellationToken) =>
            Task.FromResult<CgroupResourceUsage?>(new CgroupResourceUsage(Interlocked.Increment(ref sample) * 10_000L, 200, ReadBytes: 10_000, WriteBytes: 2_000));
    }
}
