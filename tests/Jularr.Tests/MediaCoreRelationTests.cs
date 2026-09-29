using Jularr.Web.Features.MediaCore;

namespace Jularr.Tests;

[TestClass]
public sealed class MediaCoreRelationTests
{
    [TestMethod]
    public async Task RelationWithInverseCreatesBothDirectedEdges()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        var works = new WorkService(db);
        var query = new WorkQueryService(db);

        var season1 = await works.CreateWorkAsync(WorkMediaType.Anime, "Season 1", null, CancellationToken.None);
        var season2 = await works.CreateWorkAsync(WorkMediaType.Anime, "Season 2", null, CancellationToken.None);

        await works.AddRelationAsync(
            season1.Id, season2.Id, WorkRelationType.Sequel, "anilist", isManualOverride: false, includeInverse: true, CancellationToken.None);

        var fromSeason1 = await query.GetRelationsAsync(season1.Id, CancellationToken.None);
        Assert.AreEqual(2, fromSeason1.Count);
        Assert.IsTrue(fromSeason1.Any(r => r.FromWorkId == season1.Id && r.ToWorkId == season2.Id && r.RelationType == WorkRelationType.Sequel));
        Assert.IsTrue(fromSeason1.Any(r => r.FromWorkId == season2.Id && r.ToWorkId == season1.Id && r.RelationType == WorkRelationType.Prequel));
    }

    [TestMethod]
    public async Task RelationUpsertIsUniquePerFromToType()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        var works = new WorkService(db);

        var a = await works.CreateWorkAsync(WorkMediaType.Anime, "A", null, CancellationToken.None);
        var b = await works.CreateWorkAsync(WorkMediaType.Anime, "B", null, CancellationToken.None);

        await works.AddRelationAsync(a.Id, b.Id, WorkRelationType.SideStory, "anilist", false, false, CancellationToken.None);
        await works.AddRelationAsync(a.Id, b.Id, WorkRelationType.SideStory, "mal", false, false, CancellationToken.None);

        Assert.AreEqual(1, db.WorkRelations.Count(r => r.FromWorkId == a.Id && r.ToWorkId == b.Id && r.RelationType == WorkRelationType.SideStory));
    }

    [TestMethod]
    public async Task ManualRelationIsNotOverwrittenByProvider()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        var works = new WorkService(db);

        var a = await works.CreateWorkAsync(WorkMediaType.Anime, "A", null, CancellationToken.None);
        var b = await works.CreateWorkAsync(WorkMediaType.Anime, "B", null, CancellationToken.None);

        await works.AddRelationAsync(a.Id, b.Id, WorkRelationType.SpinOff, "owner", isManualOverride: true, includeInverse: false, CancellationToken.None);
        await works.AddRelationAsync(a.Id, b.Id, WorkRelationType.SpinOff, "anilist", isManualOverride: false, includeInverse: false, CancellationToken.None);

        var relation = db.WorkRelations.Single(r => r.FromWorkId == a.Id && r.ToWorkId == b.Id);
        Assert.IsTrue(relation.IsManualOverride);
        Assert.AreEqual("owner", relation.Source);
    }

    [TestMethod]
    public async Task WorkCannotRelateToItself()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        var works = new WorkService(db);
        var a = await works.CreateWorkAsync(WorkMediaType.Anime, "A", null, CancellationToken.None);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            works.AddRelationAsync(a.Id, a.Id, WorkRelationType.Sequel, "anilist", false, false, CancellationToken.None));
    }

    [TestMethod]
    public void ProviderRelationStringsParseToTypedEdges()
    {
        Assert.AreEqual(WorkRelationType.SideStory, WorkRelationTypes.Parse("SIDE_STORY"));
        Assert.AreEqual(WorkRelationType.SpinOff, WorkRelationTypes.Parse("spin-off"));
        Assert.AreEqual(WorkRelationType.Source, WorkRelationTypes.Parse("source"));
        Assert.AreEqual(WorkRelationType.Other, WorkRelationTypes.Parse("no-such-relation"));
        Assert.AreEqual("side-story", WorkRelationTypes.ToStorage(WorkRelationType.SideStory));
        Assert.AreEqual(WorkRelationType.Prequel, WorkRelationTypes.Inverse(WorkRelationType.Sequel));
    }
}
