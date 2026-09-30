using Jularr.Web.Features.Operations;
using Jularr.Web.Pages.Admin;

namespace Jularr.Tests;

/// <summary>Admin → History: what the categories and results mean, the address, and the pills and paging.</summary>
[TestClass]
public sealed class AdminHistoryQueryTests
{
    [TestMethod]
    [DataRow("anime-search", "Acquisition", AdminHistoryCategory.Acquisition)]
    [DataRow("anime-grab", "Acquisition", AdminHistoryCategory.Acquisition)]
    [DataRow("anime-search-request", "Acquisition", AdminHistoryCategory.Acquisition)]
    [DataRow("anime-sabnzbd-download", "Downloads", AdminHistoryCategory.Acquisition)]
    [DataRow("sabnzbd-download", "SABnzbd", AdminHistoryCategory.Acquisition)]
    [DataRow("book-usenet-download", "Books", AdminHistoryCategory.Acquisition)]
    [DataRow("reading-usenet-download", "Reading", AdminHistoryCategory.Acquisition)]
    [DataRow("novel-chapter-download", "Novels", AdminHistoryCategory.Acquisition)]
    [DataRow("anime-import", "Library", AdminHistoryCategory.Imports)]
    [DataRow("media-inbox-import", "Anime", AdminHistoryCategory.Imports)]
    [DataRow("novel-import", "Novels", AdminHistoryCategory.Imports)]
    [DataRow("manga-upload-import", "Manga", AdminHistoryCategory.Imports)]
    [DataRow("remote-epub-import", "Books", AdminHistoryCategory.Imports)]
    [DataRow("media-optimization", "Library", AdminHistoryCategory.Remux)]
    [DataRow("media-optimization-recovery", "Library", AdminHistoryCategory.Repack)]
    [DataRow("episode-subtitle-import", "Learning", AdminHistoryCategory.Subtitle)]
    [DataRow("learning-text-batch", "Subtitles", AdminHistoryCategory.Subtitle)]
    [DataRow("episode-learning-preparation", "Learning", AdminHistoryCategory.Subtitle)]
    [DataRow("book-translation", "Translation", AdminHistoryCategory.Translation)]
    [DataRow("novel-chapter-translation-translategemma", "Translation", AdminHistoryCategory.Translation)]
    [DataRow("anime-metadata-match", "Anime", AdminHistoryCategory.Metadata)]
    [DataRow("anime-repair-refresh-local", "Anime", AdminHistoryCategory.Metadata)]
    [DataRow("anilist-anime-progress-sync", "AniList", AdminHistoryCategory.Metadata)]
    [DataRow("manga-refresh", "Manga", AdminHistoryCategory.Metadata)]
    [DataRow("sonarr-artwork-import", "Artwork", AdminHistoryCategory.Metadata)]
    [DataRow("novel-episode-mapping", "AI", AdminHistoryCategory.Ai)]
    [DataRow("library-scan", "Library", AdminHistoryCategory.Maintenance)]
    [DataRow("anime-rename", "Library", AdminHistoryCategory.Maintenance)]
    [DataRow("trickplay-generation", "Playback", AdminHistoryCategory.Maintenance)]
    [DataRow("segment-detection", "Library", AdminHistoryCategory.Maintenance)]
    [DataRow("background", "Task", AdminHistoryCategory.Maintenance)]
    public void EveryKindOfOperationHasACategory(string kind, string category, AdminHistoryCategory expected) =>
        Assert.AreEqual(expected, AdminHistoryQuery.CategoryOf(kind, category));

    [TestMethod]
    [DataRow(OperationStatus.Succeeded, AdminHistoryResult.Success)]
    [DataRow(OperationStatus.Failed, AdminHistoryResult.Failed)]
    [DataRow(OperationStatus.Cancelled, AdminHistoryResult.Cancelled)]
    [DataRow(OperationStatus.Interrupted, AdminHistoryResult.Warning)]
    public void ResultsMapToStatusesAndBack(OperationStatus status, AdminHistoryResult result)
    {
        Assert.AreEqual(result, AdminHistoryQuery.ResultOf(status));
        CollectionAssert.Contains(AdminHistoryQuery.StatusesOf(result).ToArray(), status);
    }

    [TestMethod]
    public void TheAddressRoundTripsCategoriesResultsAndDays()
    {
        foreach (var category in Enum.GetValues<AdminHistoryCategory>())
        {
            Assert.AreEqual(category, AdminHistoryQuery.ParseCategory(AdminHistoryQuery.CategoryName(category)));
        }

        foreach (var result in Enum.GetValues<AdminHistoryResult>())
        {
            Assert.AreEqual(result, AdminHistoryQuery.TryParseResult(AdminHistoryQuery.ResultName(result)));
        }

        Assert.AreEqual(AdminHistoryCategory.All, AdminHistoryQuery.ParseCategory("nonsense"));
        Assert.IsNull(AdminHistoryQuery.TryParseResult("nonsense"));
        Assert.AreEqual(new DateOnly(2026, 9, 30), AdminHistoryQuery.TryParseDay("2026-09-30"));
        Assert.IsNull(AdminHistoryQuery.TryParseDay("30.09.2026"));
        Assert.IsNull(AdminHistoryQuery.TryParseDay("2026-13-40"));
        Assert.IsNull(AdminHistoryQuery.TryParseDay(null));
    }

    [TestMethod]
    public void TheDayRangeIsInclusiveAndAReversedRangeIsSwapped()
    {
        var filter = AdminHistoryQuery.Normalize(new AdminHistoryFilter(
            From: new DateOnly(2026, 9, 30),
            To: new DateOnly(2026, 9, 1)));
        var query = AdminHistoryQuery.DatabaseFilter(filter);

        Assert.AreEqual(new DateOnly(2026, 9, 1), filter.From);
        Assert.AreEqual(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), query.FromUtc);
        Assert.AreEqual(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), query.ToUtc, "The last day is included in full.");
    }

    [TestMethod]
    public void HrefKeepsOnlyWhatDiffersFromTheDefaults()
    {
        Assert.AreEqual("/Admin/History", HistoryModel.Href(new AdminHistoryFilter()));
        Assert.AreEqual(
            "/Admin/History?cat=imports&result=failed&from=2026-09-01&to=2026-09-30&q=a%20b&sort=oldest&p=3",
            HistoryModel.Href(new AdminHistoryFilter(
                AdminHistoryCategory.Imports,
                AdminHistoryResult.Failed,
                new DateOnly(2026, 9, 1),
                new DateOnly(2026, 9, 30),
                " a b ",
                OldestFirst: true,
                Page: 3)));
    }

    private static OperationKindCount Count(string kind, string category, int count) =>
        new(new OperationKindKey(kind, category), count);

    [TestMethod]
    public void PillCountsGroupTheKindsAndTheCategoryNarrowsTheKindsToRead()
    {
        var counts = new[]
        {
            Count("anime-import", "Library", 5),
            Count("media-inbox-import", "Anime", 2),
            Count("anime-grab", "Acquisition", 3),
            Count("library-scan", "Library", 10)
        };

        var all = AdminHistoryQuery.Plan(counts, new AdminHistoryFilter());
        Assert.AreEqual(20, all.CategoryCounts[AdminHistoryCategory.All]);
        Assert.AreEqual(7, all.CategoryCounts[AdminHistoryCategory.Imports]);
        Assert.AreEqual(3, all.CategoryCounts[AdminHistoryCategory.Acquisition]);
        Assert.AreEqual(10, all.CategoryCounts[AdminHistoryCategory.Maintenance]);
        Assert.AreEqual(0, all.CategoryCounts[AdminHistoryCategory.Remux]);
        Assert.AreEqual(20, all.Total);
        Assert.AreEqual(0, all.Kinds.Count, "All reads every kind.");

        var imports = AdminHistoryQuery.Plan(counts, new AdminHistoryFilter(AdminHistoryCategory.Imports));
        Assert.AreEqual(7, imports.Total);
        CollectionAssert.AreEquivalent(
            new[] { "anime-import", "media-inbox-import" },
            imports.Kinds.Select(key => key.Kind).ToArray());

        var remux = AdminHistoryQuery.Plan(counts, new AdminHistoryFilter(AdminHistoryCategory.Remux));
        Assert.AreEqual(0, remux.Total);
        Assert.AreEqual(1, remux.PageCount);
    }

    [TestMethod]
    public void APagePastTheEndShowsTheLastOne()
    {
        var counts = new[] { Count("library-scan", "Library", AdminHistoryQuery.PageSize * 2 + 1) };

        var plan = AdminHistoryQuery.Plan(counts, new AdminHistoryFilter(Page: 99));

        Assert.AreEqual(3, plan.PageCount);
        Assert.AreEqual(3, plan.Page);
        Assert.AreEqual(AdminHistoryQuery.PageSize * 2, plan.Offset);
        Assert.IsTrue(plan.HasPrevious);
        Assert.IsFalse(plan.HasNext);
    }

    [TestMethod]
    public void ThePagerOffersTheEndsAndTheNeighboursWithGaps()
    {
        CollectionAssert.AreEqual(new[] { 1 }, AdminHistoryQuery.PageWindow(1, 1).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, AdminHistoryQuery.PageWindow(1, 5).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 0, 20 }, AdminHistoryQuery.PageWindow(1, 20).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 0, 8, 9, 10, 11, 12, 0, 20 }, AdminHistoryQuery.PageWindow(10, 20).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 0, 18, 19, 20 }, AdminHistoryQuery.PageWindow(20, 20).ToArray());
    }
}
