using Jularr.Web.Features.Operations;
using Jularr.Web.Pages.Admin;

namespace Jularr.Tests;

/// <summary>Admin → Activity: what the tabs mean, the address, the tab counts and the paging.</summary>
[TestClass]
public sealed class AdminActivityQueryTests
{
    private static OperationActivityCount Count(string kind, string category, OperationStatus status, int count) =>
        new(new OperationKindKey(kind, category), status, count);

    private static readonly OperationActivityCount[] Counts =
    [
        Count("anime-import", "Library", OperationStatus.Running, 2),
        Count("anime-import", "Library", OperationStatus.Queued, 3),
        Count("anime-import", "Library", OperationStatus.Succeeded, 40),
        Count("media-optimization", "Library", OperationStatus.Failed, 1),
        Count("media-optimization", "Library", OperationStatus.Interrupted, 2),
        Count("library-scan", "Library", OperationStatus.Cancelled, 5),
        Count("library-scan", "Library", OperationStatus.Queued, 1)
    ];

    [TestMethod]
    public void EachTabShowsTheStatusesItsNameSays()
    {
        CollectionAssert.AreEquivalent(
            new[] { OperationStatus.Running, OperationStatus.Queued, OperationStatus.Failed, OperationStatus.Interrupted },
            AdminActivityQuery.StatusesOf(AdminActivityTab.Todo).ToArray());
        CollectionAssert.AreEqual(new[] { OperationStatus.Running }, AdminActivityQuery.StatusesOf(AdminActivityTab.Running).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { OperationStatus.Failed, OperationStatus.Interrupted },
            AdminActivityQuery.StatusesOf(AdminActivityTab.Failed).ToArray());
        CollectionAssert.AreEquivalent(
            Enum.GetValues<OperationStatus>(),
            AdminActivityQuery.StatusesOf(AdminActivityTab.All).ToArray());
    }

    [TestMethod]
    [DataRow(null, AdminActivityTab.Todo)]
    [DataRow("", AdminActivityTab.Todo)]
    [DataRow("nonsense", AdminActivityTab.Todo)]
    [DataRow("running", AdminActivityTab.Running)]
    [DataRow("FAILED", AdminActivityTab.Failed)]
    [DataRow(" all ", AdminActivityTab.All)]
    public void ATabIsReadFromTheAddressAndUnknownMeansToDo(string? value, AdminActivityTab expected) =>
        Assert.AreEqual(expected, AdminActivityQuery.ParseTab(value));

    [TestMethod]
    public void StatusesRoundTripAndUnknownOnesMeanNoFilter()
    {
        foreach (var status in Enum.GetValues<OperationStatus>())
        {
            Assert.AreEqual(status, AdminActivityQuery.TryParseStatus(AdminActivityQuery.StatusName(status)));
        }

        Assert.IsNull(AdminActivityQuery.TryParseStatus("3"));
        Assert.IsNull(AdminActivityQuery.TryParseStatus("paused"));
        Assert.IsNull(AdminActivityQuery.TryParseStatus(null));
    }

    [TestMethod]
    public void AStatusTheTabDoesNotShowIsDropped()
    {
        var running = AdminActivityQuery.Normalize(new AdminActivityFilter(AdminActivityTab.Running, Status: OperationStatus.Failed));
        Assert.IsNull(running.Status);

        var failed = AdminActivityQuery.Normalize(new AdminActivityFilter(AdminActivityTab.Failed, Status: OperationStatus.Interrupted));
        Assert.AreEqual(OperationStatus.Interrupted, failed.Status);
    }

    [TestMethod]
    public void TabCountsFollowTheirStatusesAndTheCategoryButNotTheStatusFilter()
    {
        var plan = AdminActivityQuery.Plan(Counts, new AdminActivityFilter());
        Assert.AreEqual(2 + 3 + 1 + 2 + 1, plan.TabCounts[AdminActivityTab.Todo]);
        Assert.AreEqual(2, plan.TabCounts[AdminActivityTab.Running]);
        Assert.AreEqual(3, plan.TabCounts[AdminActivityTab.Failed]);
        Assert.AreEqual(2 + 3 + 40 + 1 + 2 + 5 + 1, plan.TabCounts[AdminActivityTab.All]);
        Assert.AreEqual(9, plan.Total);

        var imports = AdminActivityQuery.Plan(Counts, new AdminActivityFilter(Category: AdminHistoryCategory.Imports, Status: OperationStatus.Queued));
        Assert.AreEqual(5, imports.TabCounts[AdminActivityTab.Todo]);
        Assert.AreEqual(0, imports.TabCounts[AdminActivityTab.Failed]);
        Assert.AreEqual(3, imports.Total, "The status filter narrows the list but not the tab numbers.");
        CollectionAssert.AreEqual(new[] { OperationStatus.Queued }, imports.Statuses.ToArray());
        CollectionAssert.AreEqual(new[] { new OperationKindKey("anime-import", "Library") }, imports.Kinds.ToArray());
    }

    [TestMethod]
    public void PagesAreClampedAndCounted()
    {
        var many = new[] { Count("library-scan", "Library", OperationStatus.Succeeded, 41) };
        var plan = AdminActivityQuery.Plan(many, new AdminActivityFilter(AdminActivityTab.All, Page: 99));

        Assert.AreEqual(3, plan.PageCount);
        Assert.AreEqual(3, plan.Page);
        Assert.AreEqual(40, plan.Offset);
        Assert.IsTrue(plan.HasPrevious);
        Assert.IsFalse(plan.HasNext);

        var empty = AdminActivityQuery.Plan([], new AdminActivityFilter());
        Assert.AreEqual(1, empty.PageCount);
        Assert.AreEqual(0, empty.Total);
    }

    [TestMethod]
    public void TheDatabaseFilterCarriesTheTabTheCategoryTheTextAndThePage()
    {
        var plan = AdminActivityQuery.Plan(
            Counts,
            new AdminActivityFilter(AdminActivityTab.Failed, AdminHistoryCategory.Remux, Search: "  episode "));
        var database = AdminActivityQuery.DatabaseFilter(plan);

        CollectionAssert.AreEquivalent(new[] { OperationStatus.Failed, OperationStatus.Interrupted }, database.Statuses!.ToArray());
        CollectionAssert.AreEqual(new[] { new OperationKindKey("media-optimization", "Library") }, database.Kinds!.ToArray());
        Assert.AreEqual("episode", database.Search);
        Assert.AreEqual(0, database.Offset);
        Assert.AreEqual(AdminActivityQuery.PageSize, database.Limit);

        Assert.IsNull(AdminActivityQuery.DatabaseFilter(AdminActivityQuery.Plan(Counts, new AdminActivityFilter())).Kinds);
    }

    [TestMethod]
    public void TheAddressKeepsOnlyWhatDiffersFromTheDefaults()
    {
        Assert.AreEqual("/Admin/Operations", OperationsModel.Href(new AdminActivityFilter()));
        Assert.AreEqual("/Admin/Operations?tab=failed", OperationsModel.Href(new AdminActivityFilter(AdminActivityTab.Failed)));
        Assert.AreEqual(
            "/Admin/Operations?tab=all&type=imports&status=queued&q=a%20b&p=3",
            OperationsModel.Href(new AdminActivityFilter(
                AdminActivityTab.All,
                AdminHistoryCategory.Imports,
                OperationStatus.Queued,
                " a b ",
                3)));
    }

    [TestMethod]
    public void TheNoteIsTheErrorOfFailedWorkAndTheMessageOfEverythingElse()
    {
        static OperationSnapshot Snapshot(OperationStatus status, string? message, string? error) =>
            new(
                Guid.NewGuid(), "library-scan", "Library", OperationLane.Normal, status, null, "Scan", null,
                null, message, error, false, null, null, null, null, 1, true, null, null,
                DateTime.UtcNow, null, null, DateTime.UtcNow);

        Assert.AreEqual("Boom", AdminActivityQuery.NoteOf(Snapshot(OperationStatus.Failed, "Working", "Boom")));
        Assert.AreEqual("Working", AdminActivityQuery.NoteOf(Snapshot(OperationStatus.Failed, "Working", null)));
        Assert.AreEqual("Working", AdminActivityQuery.NoteOf(Snapshot(OperationStatus.Running, "Working", "Old")));
        Assert.IsNull(AdminActivityQuery.NoteOf(Snapshot(OperationStatus.Queued, "  ", null)));
    }
}
