using Jularr.Web.Data;
using Jularr.Web.Features.Operations;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Tests;

/// <summary>The work-queue queries of the operation store (Admin → Activity).</summary>
[TestClass]
public sealed class OperationActivityStoreTests
{
    private static async Task<AppDbContext> CreateDatabaseAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"jularr-op-activity-{Guid.NewGuid():N}.db");
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path};Foreign Keys=True")
            .Options);
        await DatabaseMigrationBridge.UpgradeAsync(db);
        return db;
    }

    private static async Task<Guid> CreateAsync(
        OperationStore store,
        string title,
        OperationStatus status,
        string kind = "library-scan",
        string category = "Library",
        string? subject = null)
    {
        var id = await store.CreateAsync(new OperationDescriptor(kind, category, title, subject));
        switch (status)
        {
            case OperationStatus.Running:
                await store.MarkRunningAsync(id);
                break;
            case OperationStatus.Succeeded:
                await store.MarkSucceededAsync(id, "Done.");
                break;
            case OperationStatus.Failed:
                await store.MarkFailedAsync(id, "Something broke.");
                break;
            case OperationStatus.Cancelled:
                await store.MarkCancelledAsync(id);
                break;
            case OperationStatus.Interrupted:
                await store.MarkInterruptedAsync(id);
                break;
        }

        return id;
    }

    [TestMethod]
    public async Task ActivityListsRunningFirstThenQueuedFailedInterruptedAndTheRest()
    {
        await using var db = await CreateDatabaseAsync();
        var store = new OperationStore(db);
        await CreateAsync(store, "Done", OperationStatus.Succeeded);
        await CreateAsync(store, "Interrupted", OperationStatus.Interrupted);
        await CreateAsync(store, "Failed", OperationStatus.Failed);
        await CreateAsync(store, "Queued", OperationStatus.Queued);
        await CreateAsync(store, "Running", OperationStatus.Running);

        var page = await store.QueryActivityAsync(new OperationActivityFilter());

        Assert.AreEqual(5, page.Total);
        CollectionAssert.AreEqual(
            new[] { "Running", "Queued", "Failed", "Interrupted", "Done" },
            page.Items.Select(item => item.Title).ToArray());
    }

    [TestMethod]
    public async Task StatusesKindsAndTextNarrowTheQueueAndTheTotalCountsAllMatches()
    {
        await using var db = await CreateDatabaseAsync();
        var store = new OperationStore(db);
        await CreateAsync(store, "Frieren import", OperationStatus.Running, "anime-import", subject: "Season 1");
        await CreateAsync(store, "Dungeon import", OperationStatus.Queued, "anime-import");
        await CreateAsync(store, "Nightly scan", OperationStatus.Queued);
        await CreateAsync(store, "Old scan", OperationStatus.Succeeded);

        var active = await store.QueryActivityAsync(new OperationActivityFilter([OperationStatus.Running, OperationStatus.Queued]));
        Assert.AreEqual(3, active.Total);

        var queuedImports = await store.QueryActivityAsync(new OperationActivityFilter(
            [OperationStatus.Queued],
            [new OperationKindKey("anime-import", "Library")]));
        CollectionAssert.AreEqual(new[] { "Dungeon import" }, queuedImports.Items.Select(item => item.Title).ToArray());

        var bySubject = await store.QueryActivityAsync(new OperationActivityFilter(Search: "season 1"));
        CollectionAssert.AreEqual(new[] { "Frieren import" }, bySubject.Items.Select(item => item.Title).ToArray());

        var wildcard = await store.QueryActivityAsync(new OperationActivityFilter(Search: "%"));
        Assert.AreEqual(0, wildcard.Total, "A percent sign in the search is text, not a wildcard.");

        var noStatuses = await store.QueryActivityAsync(new OperationActivityFilter(Statuses: []));
        Assert.AreEqual(0, noStatuses.Total);
        var noKinds = await store.QueryActivityAsync(new OperationActivityFilter(Kinds: []));
        Assert.AreEqual(0, noKinds.Total);
    }

    [TestMethod]
    public async Task ActivityIsPagedWithTheTotalOfAllPages()
    {
        await using var db = await CreateDatabaseAsync();
        var store = new OperationStore(db);
        for (var index = 0; index < 5; index++)
        {
            await CreateAsync(store, $"Job {index}", OperationStatus.Queued);
        }

        var first = await store.QueryActivityAsync(new OperationActivityFilter(Offset: 0, Limit: 2));
        var last = await store.QueryActivityAsync(new OperationActivityFilter(Offset: 4, Limit: 2));

        Assert.AreEqual(5, first.Total);
        Assert.AreEqual(2, first.Items.Count);
        Assert.AreEqual(1, last.Items.Count);
        Assert.IsFalse(first.Items.Select(item => item.Id).Intersect(last.Items.Select(item => item.Id)).Any());
    }

    [TestMethod]
    public async Task CountsGroupByKindAndStatusUnderTheSearchText()
    {
        await using var db = await CreateDatabaseAsync();
        var store = new OperationStore(db);
        await CreateAsync(store, "Frieren import", OperationStatus.Queued, "anime-import");
        await CreateAsync(store, "Dungeon import", OperationStatus.Queued, "anime-import");
        await CreateAsync(store, "Dungeon import", OperationStatus.Failed, "anime-import");
        await CreateAsync(store, "Nightly scan", OperationStatus.Running);

        var all = await store.CountActivityAsync(null);
        Assert.AreEqual(4, all.Sum(row => row.Count));
        Assert.AreEqual(2, all.Single(row => row.Key.Kind == "anime-import" && row.Status == OperationStatus.Queued).Count);

        var dungeon = await store.CountActivityAsync("dungeon");
        Assert.AreEqual(2, dungeon.Sum(row => row.Count));
        Assert.IsFalse(dungeon.Any(row => row.Key.Kind == "library-scan"));
    }

    private static Task<Guid> CreateWithPriorityAsync(OperationStore store, string title, OperationPriority priority) =>
        store.CreateAsync(new OperationDescriptor("library-scan", "Library", title, Priority: priority));

    [TestMethod]
    public async Task NewWorkIsNormalAndAPriorityGivenAtCreationIsKept()
    {
        await using var db = await CreateDatabaseAsync();
        var store = new OperationStore(db);
        var plain = await store.CreateAsync(new OperationDescriptor("library-scan", "Library", "Plain"));
        var urgent = await CreateWithPriorityAsync(store, "Urgent", OperationPriority.High);

        Assert.AreEqual(OperationPriority.Normal, (await store.GetAsync(plain))!.Priority);
        Assert.AreEqual(OperationPriority.High, (await store.GetAsync(urgent))!.Priority);
    }

    [TestMethod]
    public async Task ThePriorityOfQueuedAndRunningWorkCanBeChangedButNotOfFinishedWork()
    {
        await using var db = await CreateDatabaseAsync();
        var store = new OperationStore(db);
        var queued = await CreateAsync(store, "Queued", OperationStatus.Queued);
        var running = await CreateAsync(store, "Running", OperationStatus.Running);
        var done = await CreateAsync(store, "Done", OperationStatus.Succeeded);
        var failed = await CreateAsync(store, "Failed", OperationStatus.Failed);

        Assert.IsTrue(await store.SetPriorityAsync(queued, OperationPriority.High));
        Assert.IsTrue(await store.SetPriorityAsync(running, OperationPriority.Low));
        Assert.IsFalse(await store.SetPriorityAsync(done, OperationPriority.High));
        Assert.IsFalse(await store.SetPriorityAsync(failed, OperationPriority.High));
        Assert.IsFalse(await store.SetPriorityAsync(Guid.NewGuid(), OperationPriority.High));

        Assert.AreEqual(OperationPriority.High, (await store.GetAsync(queued))!.Priority);
        Assert.AreEqual(OperationPriority.Low, (await store.GetAsync(running))!.Priority);
        Assert.AreEqual(OperationPriority.Normal, (await store.GetAsync(done))!.Priority);
        Assert.AreEqual(OperationPriority.Normal, (await store.GetAsync(failed))!.Priority);

        var logs = await store.ListLogsAsync(new OperationLogFilter(OperationId: queued));
        Assert.IsTrue(logs.Any(entry => entry.Message == "Priority set to high."));

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => store.SetPriorityAsync(queued, (OperationPriority)99));
    }

    [TestMethod]
    public async Task ThePrioritiesOfSeveralOperationsAreReadInOneGo()
    {
        await using var db = await CreateDatabaseAsync();
        var store = new OperationStore(db);
        var low = await CreateWithPriorityAsync(store, "Low", OperationPriority.Low);
        var high = await CreateWithPriorityAsync(store, "High", OperationPriority.High);

        var priorities = await store.GetPrioritiesAsync([low, high, Guid.NewGuid()]);

        Assert.AreEqual(2, priorities.Count);
        Assert.AreEqual(OperationPriority.Low, priorities[low]);
        Assert.AreEqual(OperationPriority.High, priorities[high]);
        Assert.AreEqual(0, (await store.GetPrioritiesAsync([])).Count);
    }

    [TestMethod]
    public async Task ActivityFiltersByPriorityAndPutsHigherPriorityFirstWithinAStatus()
    {
        await using var db = await CreateDatabaseAsync();
        var store = new OperationStore(db);
        await CreateWithPriorityAsync(store, "Queued low", OperationPriority.Low);
        await CreateWithPriorityAsync(store, "Queued normal", OperationPriority.Normal);
        await CreateWithPriorityAsync(store, "Queued high", OperationPriority.High);
        var running = await CreateWithPriorityAsync(store, "Running low", OperationPriority.Low);
        await store.MarkRunningAsync(running);

        var page = await store.QueryActivityAsync(new OperationActivityFilter());
        CollectionAssert.AreEqual(
            new[] { "Running low", "Queued high", "Queued normal", "Queued low" },
            page.Items.Select(item => item.Title).ToArray(),
            "Status still comes first; priority orders the work within it.");

        var high = await store.QueryActivityAsync(new OperationActivityFilter(Priority: OperationPriority.High));
        Assert.AreEqual(1, high.Total);
        Assert.AreEqual("Queued high", high.Items.Single().Title);

        var counts = await store.CountActivityAsync(null);
        Assert.AreEqual(1, counts.Single(row => row.Status == OperationStatus.Queued && row.Priority == OperationPriority.Low).Count);
        Assert.AreEqual(1, counts.Single(row => row.Status == OperationStatus.Running).Count);
    }
}
