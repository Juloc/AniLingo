using System.Globalization;
using Jularr.Web.Data;
using Jularr.Web.Features.Operations;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Tests;

/// <summary>The history queries of the operation store (Admin → History) and who is recorded as the actor.</summary>
[TestClass]
public sealed class OperationHistoryStoreTests
{
    private static readonly DateTime Day = new(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<AppDbContext> CreateDatabaseAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"jularr-op-history-{Guid.NewGuid():N}.db");
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path};Foreign Keys=True")
            .Options);
        await DatabaseMigrationBridge.UpgradeAsync(db);
        return db;
    }

    /// <summary>Creates an operation, ends it with <paramref name="status"/> and back-dates when it finished.</summary>
    private static async Task<Guid> FinishedAsync(
        AppDbContext db,
        string title,
        OperationStatus status,
        DateTime finishedAtUtc,
        string kind = "library-scan",
        string category = "Library",
        string? actor = null,
        string? subject = null)
    {
        var store = new OperationStore(db);
        var id = await store.CreateAsync(new OperationDescriptor(kind, category, title, subject, ActorProfileId: actor));
        switch (status)
        {
            case OperationStatus.Succeeded:
                await store.MarkSucceededAsync(id, "Done.");
                break;
            case OperationStatus.Failed:
                await store.MarkFailedAsync(id, "Something broke.");
                break;
            case OperationStatus.Cancelled:
                await store.MarkCancelledAsync(id);
                break;
            default:
                await store.MarkInterruptedAsync(id);
                break;
        }

        var stamp = finishedAtUtc.ToString("O", CultureInfo.InvariantCulture);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "Operations" SET "FinishedAtUtc" = {stamp}, "UpdatedAtUtc" = {stamp} WHERE "Id" = {id.ToString("D")}""");
        return id;
    }

    [TestMethod]
    public async Task HistoryOnlyListsFinishedOperationsNewestFirstAndCanBeReversed()
    {
        await using var db = await CreateDatabaseAsync();
        var store = new OperationStore(db);
        await store.CreateAsync(new OperationDescriptor("library-scan", "Library", "Still queued"));
        await FinishedAsync(db, "Old", OperationStatus.Succeeded, Day);
        await FinishedAsync(db, "New", OperationStatus.Failed, Day.AddDays(2));
        await FinishedAsync(db, "Middle", OperationStatus.Cancelled, Day.AddDays(1));

        var newest = await store.QueryHistoryAsync(new OperationHistoryFilter());
        var oldest = await store.QueryHistoryAsync(new OperationHistoryFilter(NewestFirst: false));

        Assert.AreEqual(3, newest.Total);
        CollectionAssert.AreEqual(new[] { "New", "Middle", "Old" }, newest.Items.Select(item => item.Title).ToArray());
        CollectionAssert.AreEqual(new[] { "Old", "Middle", "New" }, oldest.Items.Select(item => item.Title).ToArray());
    }

    [TestMethod]
    public async Task TheDateRangeIncludesItsFirstInstantAndExcludesItsEnd()
    {
        await using var db = await CreateDatabaseAsync();
        var store = new OperationStore(db);
        await FinishedAsync(db, "Before", OperationStatus.Succeeded, Day.AddTicks(-1));
        await FinishedAsync(db, "At start", OperationStatus.Succeeded, Day);
        await FinishedAsync(db, "Late that day", OperationStatus.Succeeded, Day.AddDays(1).AddTicks(-1));
        await FinishedAsync(db, "At end", OperationStatus.Succeeded, Day.AddDays(1));

        var page = await store.QueryHistoryAsync(new OperationHistoryFilter(Day, Day.AddDays(1), NewestFirst: false));

        CollectionAssert.AreEqual(new[] { "At start", "Late that day" }, page.Items.Select(item => item.Title).ToArray());
        Assert.AreEqual(2, page.Total);
    }

    [TestMethod]
    public async Task ResultKindAndTextFiltersNarrowTheHistoryAndTheKindCounts()
    {
        await using var db = await CreateDatabaseAsync();
        var store = new OperationStore(db);
        await FinishedAsync(db, "Import One", OperationStatus.Succeeded, Day, "anime-import", "Library", subject: "Frieren S01");
        await FinishedAsync(db, "Import Two", OperationStatus.Failed, Day.AddHours(1), "anime-import", "Library");
        await FinishedAsync(db, "Scan", OperationStatus.Failed, Day.AddHours(2), "library-scan", "Library");
        await FinishedAsync(db, "Discount 100%", OperationStatus.Succeeded, Day.AddHours(3), "library-scan", "Library");

        var failed = new OperationHistoryFilter(Statuses: [OperationStatus.Failed]);
        var byKind = await store.CountHistoryByKindAsync(failed);
        Assert.AreEqual(1, byKind.Single(row => row.Key.Kind == "anime-import").Count);
        Assert.AreEqual(1, byKind.Single(row => row.Key.Kind == "library-scan").Count);

        var failedImports = await store.QueryHistoryAsync(failed with { Kinds = [new OperationKindKey("anime-import", "Library")] });
        Assert.AreEqual("Import Two", failedImports.Items.Single().Title);
        Assert.AreEqual(0, (await store.QueryHistoryAsync(new OperationHistoryFilter(Kinds: []))).Total, "No kinds means no rows.");

        var bySubject = await store.QueryHistoryAsync(new OperationHistoryFilter(Search: "frieren"));
        Assert.AreEqual("Import One", bySubject.Items.Single().Title);
        var byError = await store.QueryHistoryAsync(new OperationHistoryFilter(Search: "broke"));
        Assert.AreEqual(2, byError.Total);
        var percent = await store.QueryHistoryAsync(new OperationHistoryFilter(Search: "100%"));
        Assert.AreEqual("Discount 100%", percent.Items.Single().Title);
        var wildcard = await store.QueryHistoryAsync(new OperationHistoryFilter(Search: "%"));
        Assert.AreEqual(1, wildcard.Total, "A percent sign in the search is text, not a wildcard.");
    }

    [TestMethod]
    public async Task PagingReturnsTheTotalAndTheRequestedSlice()
    {
        await using var db = await CreateDatabaseAsync();
        var store = new OperationStore(db);
        for (var index = 0; index < 5; index++)
        {
            await FinishedAsync(db, $"Entry {index}", OperationStatus.Succeeded, Day.AddMinutes(index));
        }

        var page = await store.QueryHistoryAsync(new OperationHistoryFilter(Offset: 2, Limit: 2));

        Assert.AreEqual(5, page.Total);
        CollectionAssert.AreEqual(new[] { "Entry 2", "Entry 1" }, page.Items.Select(item => item.Title).ToArray());
    }

    [TestMethod]
    public async Task TheActorIsRecordedAndFoundBySearch()
    {
        await using var db = await CreateDatabaseAsync();
        var store = new OperationStore(db);
        await FinishedAsync(db, "By alice", OperationStatus.Succeeded, Day, actor: "alice-id");
        await FinishedAsync(db, "By the server", OperationStatus.Succeeded, Day.AddHours(1));

        var all = (await store.QueryHistoryAsync(new OperationHistoryFilter(NewestFirst: false))).Items;
        Assert.AreEqual("alice-id", all[0].ActorProfileId);
        Assert.IsNull(all[1].ActorProfileId);

        var byName = await store.QueryHistoryAsync(new OperationHistoryFilter(Search: "alice", SearchActorIds: ["alice-id"]));
        Assert.AreEqual(1, byName.Items.Count(item => item.ActorProfileId == "alice-id"));
        Assert.AreEqual("alice-id", (await store.GetAsync(all[0].Id))!.ActorProfileId);
    }

    [TestMethod]
    public async Task AnOperationCreatedDuringARequestRecordsTheSignedInAccount()
    {
        await using var db = await CreateDatabaseAsync();
        var store = new OperationStore(db);
        Guid duringRequest;
        Guid explicitActor;
        using (OperationActor.Enter("alice-id"))
        {
            duringRequest = await store.CreateAsync(new OperationDescriptor("library-scan", "Library", "During a request"));
            explicitActor = await store.CreateAsync(new OperationDescriptor("library-scan", "Library", "Explicit", ActorProfileId: "bob-id"));
            using (OperationActor.Enter(null))
            {
                Assert.IsNull(OperationActor.Current);
            }

            Assert.AreEqual("alice-id", OperationActor.Current, "Leaving a scope restores the previous account.");
        }

        var byServer = await store.CreateAsync(new OperationDescriptor("library-scan", "Library", "By the server"));

        Assert.IsNull(OperationActor.Current);
        Assert.AreEqual("alice-id", (await store.GetAsync(duringRequest))!.ActorProfileId);
        Assert.AreEqual("bob-id", (await store.GetAsync(explicitActor))!.ActorProfileId);
        Assert.IsNull((await store.GetAsync(byServer))!.ActorProfileId);
    }
}
