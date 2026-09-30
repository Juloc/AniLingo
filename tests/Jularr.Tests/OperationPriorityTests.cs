using Jularr.Web.Features.Media.Optimization;
using Jularr.Web.Features.Operations;
using Jularr.Web.Pages.Admin;

namespace Jularr.Tests;

/// <summary>Job priority and the job types of the media optimization, without a database.</summary>
[TestClass]
public sealed class OperationPriorityTests
{
    private static OperationActivityCount Count(
        string kind,
        OperationStatus status,
        int count,
        OperationPriority priority = OperationPriority.Normal) =>
        new(new OperationKindKey(kind, "Library"), status, count, priority);

    [TestMethod]
    public void TheHighestPriorityRunsNextAndTheEarliestWinsAmongEquals()
    {
        Assert.AreEqual(-1, OperationPriorities.PickNext([]));
        Assert.AreEqual(0, OperationPriorities.PickNext([OperationPriority.Normal]));
        Assert.AreEqual(0, OperationPriorities.PickNext([OperationPriority.Normal, OperationPriority.Normal, OperationPriority.Low]));
        Assert.AreEqual(2, OperationPriorities.PickNext([OperationPriority.Low, OperationPriority.Normal, OperationPriority.High, OperationPriority.High]));
        Assert.AreEqual(1, OperationPriorities.PickNext([OperationPriority.Low, OperationPriority.Normal]));
    }

    [TestMethod]
    public void PrioritiesRoundTripThroughTheirNamesAndUnknownOnesMeanNone()
    {
        foreach (var priority in Enum.GetValues<OperationPriority>())
        {
            Assert.AreEqual(priority, OperationPriorities.TryParse(OperationPriorities.Name(priority)));
        }

        Assert.AreEqual(OperationPriority.High, OperationPriorities.TryParse(" HIGH "));
        Assert.IsNull(OperationPriorities.TryParse("2"));
        Assert.IsNull(OperationPriorities.TryParse("urgent"));
        Assert.IsNull(OperationPriorities.TryParse(null));
    }

    [TestMethod]
    public void NewWorkIsNormalUnlessItSaysOtherwise()
    {
        Assert.AreEqual(OperationPriority.Normal, OperationDescriptor.Background().Priority);
        Assert.AreEqual(OperationPriority.Normal, OperationDescriptor.Playback().Priority);
    }

    [TestMethod]
    public void ThePriorityFilterNarrowsTheTabCountsAndTheRowsToRead()
    {
        var counts = new[]
        {
            Count("anime-import", OperationStatus.Queued, 3),
            Count("anime-import", OperationStatus.Queued, 2, OperationPriority.High),
            Count("anime-import", OperationStatus.Failed, 1, OperationPriority.High),
            Count("library-scan", OperationStatus.Running, 4, OperationPriority.Low),
            Count("library-scan", OperationStatus.Succeeded, 9, OperationPriority.High)
        };

        var everything = AdminActivityQuery.Plan(counts, new AdminActivityFilter());
        Assert.AreEqual(3 + 2 + 1 + 4, everything.TabCounts[AdminActivityTab.Todo]);
        Assert.IsNull(AdminActivityQuery.DatabaseFilter(everything).Priority);

        var high = AdminActivityQuery.Plan(counts, new AdminActivityFilter(Priority: OperationPriority.High));
        Assert.AreEqual(2 + 1, high.TabCounts[AdminActivityTab.Todo]);
        Assert.AreEqual(0, high.TabCounts[AdminActivityTab.Running]);
        Assert.AreEqual(1, high.TabCounts[AdminActivityTab.Failed]);
        Assert.AreEqual(2 + 1 + 9, high.TabCounts[AdminActivityTab.All]);
        Assert.AreEqual(3, high.Total);
        Assert.AreEqual(OperationPriority.High, AdminActivityQuery.DatabaseFilter(high).Priority);

        var lowScans = AdminActivityQuery.Plan(
            counts,
            new AdminActivityFilter(Category: AdminHistoryCategory.Maintenance, Priority: OperationPriority.Low));
        Assert.AreEqual(4, lowScans.Total);
        CollectionAssert.AreEqual(new[] { new OperationKindKey("library-scan", "Library") }, lowScans.Kinds.ToArray());
    }

    [TestMethod]
    public void ThePriorityIsPartOfTheAddressAndCountsAsNarrowing()
    {
        Assert.IsFalse(new AdminActivityFilter().HasNarrowing);
        Assert.IsTrue(new AdminActivityFilter(Priority: OperationPriority.Low).HasNarrowing);
        Assert.AreEqual(
            "/Admin/Operations?priority=high",
            OperationsModel.Href(new AdminActivityFilter(Priority: OperationPriority.High)));
        Assert.AreEqual(
            "/Admin/Operations?tab=all&type=repack&status=queued&priority=low&q=x&p=2",
            OperationsModel.Href(new AdminActivityFilter(
                AdminActivityTab.All,
                AdminHistoryCategory.Repack,
                OperationStatus.Queued,
                "x",
                2,
                OperationPriority.Low)));
    }

    [TestMethod]
    public void TheRemuxAndTheRepackRunsOfTheMediaOptimizationAreJobTypesOfTheirOwn()
    {
        Assert.AreEqual(
            AdminHistoryCategory.Remux,
            AdminHistoryQuery.CategoryOf(MediaContainerOptimizer.OperationKind, MediaOptimizationQueue.OperationCategory));
        Assert.AreEqual(
            AdminHistoryCategory.Repack,
            AdminHistoryQuery.CategoryOf(MediaContainerOptimizer.RecoveryOperationKind, MediaOptimizationQueue.OperationCategory));

        var counts = new[]
        {
            Count(MediaContainerOptimizer.OperationKind, OperationStatus.Queued, 2),
            Count(MediaContainerOptimizer.RecoveryOperationKind, OperationStatus.Failed, 1)
        };
        var repack = AdminActivityQuery.Plan(counts, new AdminActivityFilter(Category: AdminHistoryCategory.Repack));
        Assert.AreEqual(1, repack.Total, "The recovery run is not counted as a remux.");
        CollectionAssert.AreEqual(
            new[] { new OperationKindKey(MediaContainerOptimizer.RecoveryOperationKind, "Library") },
            repack.Kinds.ToArray());

        var history = AdminHistoryQuery.Plan(
            [
                new OperationKindCount(new OperationKindKey(MediaContainerOptimizer.OperationKind, "Library"), 4),
                new OperationKindCount(new OperationKindKey(MediaContainerOptimizer.RecoveryOperationKind, "Library"), 1)
            ],
            new AdminHistoryFilter());
        Assert.AreEqual(4, history.CategoryCounts[AdminHistoryCategory.Remux]);
        Assert.AreEqual(1, history.CategoryCounts[AdminHistoryCategory.Repack]);
    }
}
