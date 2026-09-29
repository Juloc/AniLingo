using Jularr.Web.Features.Operations;

namespace Jularr.Tests;

/// <summary>Renders the real Activity center page (#413) over operations stored in the database.</summary>
[TestClass]
public sealed class ActivityCenterPageTests
{
    [TestMethod]
    public async Task PageGroupsStoredOperationsAndGatesCancelAndRetry()
    {
        await using var host = await ManageSheetPageTestHost.CreateAsync();
        var store = new OperationStore(host.Db);

        var download = await store.CreateAsync(new OperationDescriptor(
            "anime-sabnzbd-download", "External downloads", "Frieren S01", IsDownload: true));
        await store.MarkRunningAsync(download);
        await store.ReportProgressAsync(download, 42, "Downloading.", 420, 1000, 2048, DateTime.UtcNow.AddMinutes(3));

        var scan = await store.CreateAsync(new OperationDescriptor(
            "library-scan", "Library", "Scan Anime root", Lane: OperationLane.Maintenance));

        var failed = await store.CreateAsync(new OperationDescriptor(
            "media-optimization", "Library", "Optimize media for Direct Play", Lane: OperationLane.Maintenance));
        await store.MarkRunningAsync(failed);
        await store.MarkFailedAsync(failed, "IOException: disk full");

        var html = await host.GetHtmlAsync("/Admin/Activity", asOwner: true);

        StringAssert.Contains(html, "data-activity-lane=\"downloads\"");
        StringAssert.Contains(html, "data-activity-lane=\"scans\"");
        StringAssert.Contains(html, "data-activity-lane=\"maintenance\"");
        StringAssert.Contains(html, "Frieren S01");
        StringAssert.Contains(html, "42%");
        StringAssert.Contains(html, "IOException: disk full");
        StringAssert.Contains(html, "/css/activity-center.css");
        StringAssert.Contains(html, "/js/activity-center.js");

        // Running and queued work can be cancelled; a failed operation whose in-process work is
        // gone (this host never queued it) shows Retry disabled and cannot be cancelled.
        Assert.AreEqual(1, Occurrences(html, $"name=\"id\" value=\"{scan}\""), "Queued scan: cancel only.");
        Assert.AreEqual(1, Occurrences(html, $"name=\"id\" value=\"{download}\""), "Running download: cancel only.");
        Assert.AreEqual(1, Occurrences(html, $"name=\"id\" value=\"{failed}\""), "Failed job: retry only.");
        StringAssert.Contains(html, "Retry payload is no longer available");
        Assert.AreEqual(2, Occurrences(html, "handler=Cancel"));
        Assert.AreEqual(1, Occurrences(html, "handler=Retry"));
    }

    [TestMethod]
    public async Task PageStaysCalmWhenNothingIsHappening()
    {
        await using var host = await ManageSheetPageTestHost.CreateAsync();

        var html = await host.GetHtmlAsync("/Admin/Activity", asOwner: true);

        StringAssert.Contains(html, "Nothing is running and nothing needs attention.");
        Assert.AreEqual(0, Occurrences(html, "data-activity-lane="));
        Assert.AreEqual(1, Occurrences(html, "Activity center</h1>"));
    }

    private static int Occurrences(string haystack, string needle) =>
        haystack.Split(needle).Length - 1;
}
