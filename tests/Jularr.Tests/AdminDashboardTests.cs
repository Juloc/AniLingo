using Jularr.Web.Features.Admin;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Operations;
using Jularr.Web.Features.Playback.Decision;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jularr.Tests;

/// <summary>The figures behind the Admin dashboard: host readings, the sampler and how values read.</summary>
[TestClass]
public sealed class AdminDashboardTests
{
    private const string ProcStat =
        "cpu  4705 150 1120 16250 520 0 30 0 0 0\ncpu0 1175 37 280 4062 130 0 8 0 0 0\nintr 12345\n";

    [TestMethod]
    public void CpuTicksCountIdleAndIowaitAsIdleAndIgnoreGuestTime()
    {
        var ticks = HostMetricsParser.ParseCpuTicks(ProcStat);

        Assert.IsNotNull(ticks);
        Assert.AreEqual(4705 + 150 + 1120 + 16250 + 520 + 30, ticks.Value.Total);
        Assert.AreEqual(4705 + 150 + 1120 + 30, ticks.Value.Busy);
    }

    [TestMethod]
    public void CpuPercentIsTheBusyShareBetweenTwoReadings()
    {
        var percent = HostMetricsParser.CpuPercent((100, 1000), (400, 1600));

        Assert.AreEqual(50d, percent);
        Assert.IsNull(HostMetricsParser.CpuPercent((100, 1000), (100, 1000)), "No time passed, no figure.");
        Assert.IsNull(HostMetricsParser.CpuPercent((500, 1000), (400, 1600)), "Counters that went backwards are dropped.");
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("cpu0 1 2 3 4 5")]
    [DataRow("cpu  1 2 3")]
    [DataRow("cpu  a b c d e")]
    public void UnreadableCpuLinesGiveNoFigure(string? text) =>
        Assert.IsNull(HostMetricsParser.ParseCpuTicks(text));

    [TestMethod]
    public void MemInfoReportsBytesFromKibibytes()
    {
        var memory = HostMetricsParser.ParseMemInfo(
            "MemTotal:       16384000 kB\nMemFree:         1000000 kB\nMemAvailable:    8192000 kB\nBuffers: 1 kB\n");

        Assert.IsNotNull(memory);
        Assert.AreEqual(16384000L * 1024, memory.Value.TotalBytes);
        Assert.AreEqual(8192000L * 1024, memory.Value.AvailableBytes);
        Assert.IsNull(HostMetricsParser.ParseMemInfo("MemTotal: 100 kB\n"), "Without MemAvailable memory use is unknown.");
        Assert.IsNull(HostMetricsParser.ParseMemInfo("MemTotal: 100 kB\nMemAvailable: 200 kB\n"));
    }

    [TestMethod]
    public void LoadAverageReadsTheThreeLeadingNumbers()
    {
        var load = HostMetricsParser.ParseLoadAverage("0.73 0.71 0.69 2/512 12345\n");

        Assert.IsNotNull(load);
        Assert.AreEqual(0.73, load.One);
        Assert.AreEqual(0.71, load.Five);
        Assert.AreEqual(0.69, load.Fifteen);
        Assert.IsNull(HostMetricsParser.ParseLoadAverage("0.73 x 0.69"));
        Assert.IsNull(HostMetricsParser.ParseLoadAverage(null));
    }

    [TestMethod]
    public void RatesAreBytesPerSecondAndSkipRestartedCounters()
    {
        Assert.AreEqual(2_000d, HostMetricsParser.Rate(1_000, 11_000, TimeSpan.FromSeconds(5)));
        Assert.IsNull(HostMetricsParser.Rate(11_000, 1_000, TimeSpan.FromSeconds(5)));
        Assert.IsNull(HostMetricsParser.Rate(0, 100, TimeSpan.Zero));
    }

    [TestMethod]
    public async Task TheSamplerRecordsReadingsAndTheSecondOneCarriesProcessCpu()
    {
        var sampler = new HostTelemetrySampler(TimeProvider.System, NullLogger<HostTelemetrySampler>.Instance);

        var first = sampler.Sample();
        Assert.IsNull(first.ProcessCpuPercent, "One reading has no rate.");
        Assert.IsTrue(first.ProcessWorkingSetBytes > 0);

        await Task.Delay(50);
        var second = sampler.Sample();
        Assert.IsNotNull(second.ProcessCpuPercent);
        Assert.IsTrue(second.ProcessCpuPercent is >= 0 and <= 100);

        Assert.AreEqual(0, sampler.GetSnapshot().History.Count, "Only the hosted loop records into the history.");
        await sampler.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (sampler.GetSnapshot().Current is null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.IsNotNull(sampler.GetSnapshot().Current, "The loop takes its first reading right away.");
        }
        finally
        {
            await sampler.StopAsync(CancellationToken.None);
        }
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
}
