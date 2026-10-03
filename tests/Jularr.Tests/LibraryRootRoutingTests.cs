using Jularr.Tests.Infrastructure;
using Jularr.Web.Data;
using Jularr.Web.Features.Library;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Tests;

[TestClass]
public sealed class LibraryRootRoutingTests
{
    [TestMethod]
    public async Task ResolvesExactlyTheConfiguredDefaultForOneContentType()
    {
        await using var db = await CreateDbAsync();
        var one = new LibraryRoot { Name = "Games A", Path = "/media/games-a", PlacementPolicy = LibraryPlacementPolicy.HardlinkOrCopy };
        var two = new LibraryRoot { Name = "Games B", Path = "/media/games-b", PlacementPolicy = LibraryPlacementPolicy.Copy };
        db.LibraryRoots.AddRange(one, two);
        await db.SaveChangesAsync();

        var routing = new LibraryRootRoutingService(db);
        await routing.SetSupportedAsync(one.Id, LibraryContentType.Game, true);
        await routing.SetSupportedAsync(two.Id, LibraryContentType.Game, true);
        await routing.SetDefaultAsync(LibraryContentType.Game, two.Id);

        var resolved = await routing.ResolveDefaultAsync(LibraryContentType.Game);

        Assert.IsNotNull(resolved);
        Assert.AreEqual(two.Id, resolved.LibraryRootId);
        Assert.AreEqual(LibraryPlacementPolicy.Copy, resolved.PlacementPolicy);
        Assert.IsTrue(resolved.IsDefault);

        var all = await routing.ListAsync(LibraryContentType.Game);
        Assert.AreEqual(2, all.Count);
        Assert.AreEqual(1, all.Count(x => x.IsDefault));
    }

    [TestMethod]
    public async Task ChangingDefaultIsTransactionalAndLeavesOnlyOneDefault()
    {
        await using var db = await CreateDbAsync();
        var one = new LibraryRoot { Name = "One", Path = "/media/one" };
        var two = new LibraryRoot { Name = "Two", Path = "/media/two" };
        db.LibraryRoots.AddRange(one, two);
        db.LibraryRootContentAssignments.AddRange(
            new LibraryRootContentAssignment { LibraryRootId = one.Id, ContentType = LibraryContentType.Game },
            new LibraryRootContentAssignment { LibraryRootId = two.Id, ContentType = LibraryContentType.Game });
        await db.SaveChangesAsync();

        var routing = new LibraryRootRoutingService(db);
        await routing.SetDefaultAsync(LibraryContentType.Game, one.Id);
        await routing.SetDefaultAsync(LibraryContentType.Game, two.Id);

        var assignments = await db.LibraryRootContentAssignments
            .AsNoTracking()
            .Where(x => x.ContentType == LibraryContentType.Game)
            .ToListAsync();

        Assert.AreEqual(1, assignments.Count(x => x.IsDefault));
        Assert.IsTrue(assignments.Single(x => x.LibraryRootId == two.Id).IsDefault);
    }

    [TestMethod]
    public async Task DatabaseRejectsTwoDefaultsForTheSameContentType()
    {
        await using var db = await CreateDbAsync();
        var one = new LibraryRoot { Name = "One", Path = "/media/one" };
        var two = new LibraryRoot { Name = "Two", Path = "/media/two" };
        db.LibraryRoots.AddRange(one, two);
        db.LibraryRootContentAssignments.AddRange(
            new LibraryRootContentAssignment
            {
                LibraryRootId = one.Id,
                ContentType = LibraryContentType.Game,
                IsDefault = true
            },
            new LibraryRootContentAssignment
            {
                LibraryRootId = two.Id,
                ContentType = LibraryContentType.Game,
                IsDefault = true
            });

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [TestMethod]
    public async Task DisabledRootIsNeverResolvedAsDefault()
    {
        await using var db = await CreateDbAsync();
        var root = new LibraryRoot { Name = "Disabled", Path = "/media/disabled", IsEnabled = false };
        db.LibraryRoots.Add(root);
        db.LibraryRootContentAssignments.Add(
            new LibraryRootContentAssignment
            {
                LibraryRootId = root.Id,
                ContentType = LibraryContentType.Game,
                IsDefault = true
            });
        await db.SaveChangesAsync();

        var routing = new LibraryRootRoutingService(db);
        Assert.IsNull(await routing.ResolveDefaultAsync(LibraryContentType.Game));
    }

    [TestMethod]
    public async Task CannotSelectUnsupportedOrDisabledRootAsDefault()
    {
        await using var db = await CreateDbAsync();
        var unsupported = new LibraryRoot { Name = "Unsupported", Path = "/media/u" };
        var disabled = new LibraryRoot { Name = "Disabled", Path = "/media/d", IsEnabled = false };
        db.LibraryRoots.AddRange(unsupported, disabled);
        db.LibraryRootContentAssignments.Add(
            new LibraryRootContentAssignment
            {
                LibraryRootId = disabled.Id,
                ContentType = LibraryContentType.Game
            });
        await db.SaveChangesAsync();

        var routing = new LibraryRootRoutingService(db);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => routing.SetDefaultAsync(LibraryContentType.Game, unsupported.Id));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => routing.SetDefaultAsync(LibraryContentType.Game, disabled.Id));
    }

    [TestMethod]
    public async Task AssignmentForeignKeyDoesNotCascadeWithLibraryRootDeletion()
    {
        await using var db = await CreateDbAsync();
        var root = new LibraryRoot { Name = "Games", Path = "/media/games" };
        db.LibraryRoots.Add(root);
        db.LibraryRootContentAssignments.Add(
            new LibraryRootContentAssignment
            {
                LibraryRootId = root.Id,
                ContentType = LibraryContentType.Game
            });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        db.Entry(new LibraryRoot { Id = root.Id, Name = root.Name, Path = root.Path }).State = EntityState.Deleted;
        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [TestMethod]
    public async Task ContentTypesDoNotFallBackToAnotherTypesDefault()
    {
        await using var db = await CreateDbAsync();
        var root = new LibraryRoot { Name = "Games", Path = "/media/games" };
        db.LibraryRoots.Add(root);
        db.LibraryRootContentAssignments.Add(
            new LibraryRootContentAssignment
            {
                LibraryRootId = root.Id,
                ContentType = LibraryContentType.Game,
                IsDefault = true
            });
        await db.SaveChangesAsync();

        var routing = new LibraryRootRoutingService(db);

        Assert.IsNotNull(await routing.ResolveDefaultAsync(LibraryContentType.Game));
        Assert.IsNull(await routing.ResolveDefaultAsync(LibraryContentType.Movie));
    }

    private static async Task<AppDbContext> CreateDbAsync()
    {
        var connection = TestPostgres.ResolveConnectionString(
            $"Data Source=library-routing-{Guid.NewGuid():N}.db");
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connection)
                .Options);
        await DatabaseMigrationBridge.UpgradeAsync(db);
        return db;
    }
}
