using Jularr.Web.Features.MediaCore;

namespace Jularr.Tests;

[TestClass]
public sealed class MediaCoreIdentityTests
{
    [TestMethod]
    public async Task SameProviderIdentityCannotBeClaimedByTwoWorks()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        var works = new WorkService(db);
        var query = new WorkQueryService(db);

        var a = await works.CreateWorkAsync(WorkMediaType.Anime, "Work A", null, CancellationToken.None);
        var b = await works.CreateWorkAsync(WorkMediaType.Anime, "Work B", null, CancellationToken.None);

        Assert.IsTrue(await works.LinkExternalIdentityAsync(
            a.Id, WorkMediaType.Anime, "anilist", "101", 1.0, "", true, false, MappingReviewState.Confirmed, CancellationToken.None));
        // The same identity is already claimed by A: linking it to B is refused, not silently moved.
        Assert.IsFalse(await works.LinkExternalIdentityAsync(
            b.Id, WorkMediaType.Anime, "anilist", "101", 1.0, "", true, false, MappingReviewState.Confirmed, CancellationToken.None));

        Assert.AreEqual(a.Id, await query.FindWorkIdByExternalIdentityAsync(WorkMediaType.Anime, "anilist", "101", CancellationToken.None));
    }

    [TestMethod]
    public async Task ReassignCorrectsMappingAndMarksManualOverride()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        var works = new WorkService(db);
        var query = new WorkQueryService(db);

        var wrong = await works.CreateWorkAsync(WorkMediaType.Anime, "Wrong match", null, CancellationToken.None);
        var right = await works.CreateWorkAsync(WorkMediaType.Anime, "Right match", null, CancellationToken.None);
        await works.LinkExternalIdentityAsync(
            wrong.Id, WorkMediaType.Anime, "tvdb", "500", 0.4, "weak title match", false, false,
            MappingReviewState.NeedsReview, CancellationToken.None);

        var corrected = await works.ReassignExternalIdentityAsync(
            WorkMediaType.Anime, "tvdb", "500", right.Id, "owner", "owner confirmed", CancellationToken.None);

        Assert.IsNotNull(corrected);
        Assert.AreEqual(right.Id, corrected!.WorkId);
        Assert.IsTrue(corrected.IsManualOverride);
        Assert.AreEqual(MappingReviewState.Confirmed, corrected.ReviewState);
        Assert.AreEqual(right.Id, await query.FindWorkIdByExternalIdentityAsync(WorkMediaType.Anime, "tvdb", "500", CancellationToken.None));
    }

    [TestMethod]
    public async Task ManualIdentityIsNotDowngradedByProviderRefresh()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        var works = new WorkService(db);

        var work = await works.CreateWorkAsync(WorkMediaType.Anime, "Work", null, CancellationToken.None);
        await works.LinkExternalIdentityAsync(
            work.Id, WorkMediaType.Anime, "mal", "1", 1.0, "owner", true, true, MappingReviewState.Confirmed, CancellationToken.None);

        // A later non-manual refresh of the same identity must not clear the manual override.
        var applied = await works.LinkExternalIdentityAsync(
            work.Id, WorkMediaType.Anime, "mal", "1", 0.5, "auto", false, false, MappingReviewState.NeedsReview, CancellationToken.None);

        Assert.IsFalse(applied);
        var identity = db.WorkExternalIdentities.Single(x => x.Provider == "mal" && x.ExternalId == "1");
        Assert.IsTrue(identity.IsManualOverride);
        Assert.AreEqual(MappingReviewState.Confirmed, identity.ReviewState);
    }

    [TestMethod]
    public async Task ProviderNamespacesAreSeparatePerMediaType()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        var works = new WorkService(db);
        var query = new WorkQueryService(db);

        var anime = await works.EnsureWorkByExternalIdentityAsync(
            WorkMediaType.Anime, "anilist", "30002", "Anime 30002", null, CancellationToken.None);
        var manga = await works.EnsureWorkByExternalIdentityAsync(
            WorkMediaType.Manga, "anilist", "30002", "Manga 30002", null, CancellationToken.None);

        Assert.AreNotEqual(anime.Id, manga.Id, "The same provider id in different media namespaces is two distinct works.");
        Assert.AreEqual(anime.Id, await query.FindWorkIdByExternalIdentityAsync(WorkMediaType.Anime, "anilist", "30002", CancellationToken.None));
        Assert.AreEqual(manga.Id, await query.FindWorkIdByExternalIdentityAsync(WorkMediaType.Manga, "anilist", "30002", CancellationToken.None));
    }

    [TestMethod]
    public async Task FieldProvenanceFollowsPriorityLadder()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        var works = new WorkService(db);
        var query = new WorkQueryService(db);
        var work = await works.CreateWorkAsync(WorkMediaType.Anime, "Work", null, CancellationToken.None);

        // filename (weakest) → local beats it → preferred provider beats local → manual beats everything.
        Assert.IsTrue(await works.SetFieldProvenanceAsync(work.Id, "title", MetadataFieldSources.Filename, null, null, false, "anilist", CancellationToken.None));
        Assert.IsTrue(await works.SetFieldProvenanceAsync(work.Id, "title", MetadataFieldSources.Local, null, null, false, "anilist", CancellationToken.None));
        Assert.IsTrue(await works.SetFieldProvenanceAsync(work.Id, "title", "anilist", "20958", 1.0, false, "anilist", CancellationToken.None));
        // A weaker secondary provider must NOT overwrite the preferred provider.
        Assert.IsFalse(await works.SetFieldProvenanceAsync(work.Id, "title", "tmdb", "1", 1.0, false, "anilist", CancellationToken.None));
        // An owner manual override wins over the preferred provider.
        Assert.IsTrue(await works.SetFieldProvenanceAsync(work.Id, "title", MetadataFieldSources.Owner, null, null, true, "anilist", CancellationToken.None));
        // A later provider refresh must NOT overwrite the manual override.
        Assert.IsFalse(await works.SetFieldProvenanceAsync(work.Id, "title", "anilist", "20958", 1.0, false, "anilist", CancellationToken.None));

        var provenance = (await query.GetProvenanceAsync(work.Id, CancellationToken.None)).Single(p => p.FieldKey == "title");
        Assert.AreEqual(MetadataFieldSources.Owner, provenance.Source);
        Assert.IsTrue(provenance.IsManualOverride);
        Assert.AreEqual(MetadataFieldSources.PriorityManual, provenance.FallbackPriority);
    }

    [TestMethod]
    public void TitleNormalizationFoldsCaseDiacriticsAndPunctuation()
    {
        Assert.AreEqual("attack on titan", MediaCoreNormalization.NormalizeTitle("  Attack  on Titan!! "));
        Assert.AreEqual("frieren beyond journeys end", MediaCoreNormalization.NormalizeTitle("Frieren: Beyond Journey's End"));
        Assert.AreEqual("pokemon", MediaCoreNormalization.NormalizeTitle("Pokémon"));
    }
}
