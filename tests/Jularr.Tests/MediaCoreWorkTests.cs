using Jularr.Web.Features.MediaCore;

namespace Jularr.Tests;

[TestClass]
public sealed class MediaCoreWorkTests
{
    [TestMethod]
    public async Task EnsureWorkByExternalIdentityIsIdempotent()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        var works = new WorkService(db);

        var first = await works.EnsureWorkByExternalIdentityAsync(
            WorkMediaType.Anime, "AniList", "20958", "Attack on Titan", 2013, CancellationToken.None);
        var second = await works.EnsureWorkByExternalIdentityAsync(
            WorkMediaType.Anime, "anilist", " 20958 ", "AoT", null, CancellationToken.None);

        Assert.AreEqual(first.Id, second.Id, "The same provider identity must resolve to the same stable work id.");
        Assert.AreEqual(1, db.Works.Count());
        Assert.AreEqual(1, db.WorkExternalIdentities.Count());
    }

    [TestMethod]
    public async Task PrimaryTitleIsCachedAndAlternatesAreKept()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        var works = new WorkService(db);
        var query = new WorkQueryService(db);

        var work = await works.CreateWorkAsync(WorkMediaType.Anime, "placeholder", 2020, CancellationToken.None);
        await works.AddOrUpdateTitleAsync(
            work.Id, WorkTitleType.Primary, "en", "Frieren: Beyond Journey's End", "anilist", true, CancellationToken.None);
        await works.AddOrUpdateTitleAsync(
            work.Id, WorkTitleType.Native, "ja", "葬送のフリーレン", "anilist", false, CancellationToken.None);
        await works.AddOrUpdateTitleAsync(
            work.Id, WorkTitleType.Romaji, "und", "Sousou no Frieren", "anilist", false, CancellationToken.None);

        var titles = await query.GetTitlesAsync(work.Id, CancellationToken.None);
        Assert.AreEqual(3, titles.Count);
        Assert.AreEqual(1, titles.Count(t => t.IsPrimary));

        var reloaded = await query.GetWorkAsync(work.Id, CancellationToken.None);
        Assert.AreEqual("Frieren: Beyond Journey's End", reloaded!.CanonicalTitle);
    }

    [TestMethod]
    public async Task ReAddingSameTitleRefreshesInsteadOfDuplicating()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        var works = new WorkService(db);

        var work = await works.CreateWorkAsync(WorkMediaType.Manga, "Berserk", null, CancellationToken.None);
        await works.AddOrUpdateTitleAsync(work.Id, WorkTitleType.Primary, "en", "Berserk", "anilist", true, CancellationToken.None);
        // Same normalized value (punctuation/casing folded) must not create a second row.
        await works.AddOrUpdateTitleAsync(work.Id, WorkTitleType.Primary, "en", "  berserk  ", "mal", true, CancellationToken.None);

        Assert.AreEqual(1, db.WorkTitles.Count(t => t.WorkId == work.Id && t.TitleType == WorkTitleType.Primary));
    }

    [TestMethod]
    public async Task EpisodesSupportAbsoluteNumberingAndSpecials()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        var works = new WorkService(db);
        var structure = new WorkStructureService(db);
        var query = new WorkQueryService(db);

        var work = await works.CreateWorkAsync(WorkMediaType.Anime, "Demo", null, CancellationToken.None);
        var specials = await structure.AddOrUpdateSeasonAsync(work.Id, 0, "Specials", CancellationToken.None);
        var season1 = await structure.AddOrUpdateSeasonAsync(work.Id, 1, null, CancellationToken.None);

        Assert.IsTrue(specials.IsSpecial);
        Assert.IsFalse(season1.IsSpecial);

        await structure.AddOrUpdateEpisodeAsync(work.Id, 1, 1, absoluteNumber: 1, isSpecial: false, "Ep1", null, season1.Id, CancellationToken.None);
        await structure.AddOrUpdateEpisodeAsync(work.Id, 2, 1, absoluteNumber: 13, isSpecial: false, "S2E1", null, null, CancellationToken.None);
        await structure.AddOrUpdateEpisodeAsync(work.Id, 0, 1, absoluteNumber: null, isSpecial: true, "OVA", null, specials.Id, CancellationToken.None);
        // Upsert on the natural key updates rather than duplicating.
        await structure.AddOrUpdateEpisodeAsync(work.Id, 2, 1, absoluteNumber: 13, isSpecial: false, "S2E1 renamed", null, null, CancellationToken.None);

        var episodes = await query.GetEpisodesAsync(work.Id, CancellationToken.None);
        Assert.AreEqual(3, episodes.Count);
        Assert.AreEqual(13, episodes.Single(e => e.SeasonNumber == 2).AbsoluteNumber);
        Assert.IsTrue(episodes.Single(e => e.SeasonNumber == 0).IsSpecial);
        Assert.AreEqual("S2E1 renamed", episodes.Single(e => e.SeasonNumber == 2).Title);
    }

    [TestMethod]
    public async Task VolumesChaptersEditionsAndVersionsPersist()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        var works = new WorkService(db);
        var structure = new WorkStructureService(db);
        var query = new WorkQueryService(db);

        var work = await works.CreateWorkAsync(WorkMediaType.LightNovel, "Overlord", null, CancellationToken.None);
        var volume = await structure.AddOrUpdateVolumeAsync(work.Id, 1, "The Undead King", CancellationToken.None);
        await structure.AddOrUpdateChapterAsync(work.Id, 1, "Prologue", false, volume.Id, CancellationToken.None);
        await structure.AddOrUpdateChapterAsync(work.Id, 1.5, "Interlude", true, volume.Id, CancellationToken.None);

        var edition = await structure.AddOrUpdateEditionAsync(
            work.Id, "yen-press-en", "en", "ebook", "Yen Press", "9780316272247", "Overlord Vol. 1", true, CancellationToken.None);
        await structure.AddOrUpdateVersionAsync(
            work.Id, "v1-epub", "V01", edition.Id, "retail", null, "yen-press", null, CancellationToken.None);

        Assert.AreEqual(1, (await query.GetVolumesAsync(work.Id, CancellationToken.None)).Count);
        Assert.AreEqual(2, (await query.GetChaptersAsync(work.Id, CancellationToken.None)).Count);
        var editions = await query.GetEditionsAsync(work.Id, CancellationToken.None);
        Assert.AreEqual(1, editions.Count);
        Assert.IsTrue(editions[0].IsPrimary);
        var versions = await query.GetVersionsAsync(work.Id, CancellationToken.None);
        Assert.AreEqual(1, versions.Count);
        Assert.AreEqual(edition.Id, versions[0].EditionId);
    }

    [TestMethod]
    public async Task SummaryAggregatesIdentitiesTitlesAndSources()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        var works = new WorkService(db);
        var query = new WorkQueryService(db);

        var work = await works.EnsureWorkByExternalIdentityAsync(
            WorkMediaType.Series, "tmdb", "1396", "Breaking Bad", 2008, CancellationToken.None);
        await works.LinkExternalIdentityAsync(
            work.Id, WorkMediaType.Series, "imdb", "tt0903747", 1.0, "imdb id", false, false,
            MappingReviewState.Confirmed, CancellationToken.None);
        await works.AddOrUpdateTitleAsync(
            work.Id, WorkTitleType.Primary, "en", "Breaking Bad", "tmdb", true, CancellationToken.None);
        await works.LinkSourceAsync(work.Id, WorkSourceKind.Episode, Guid.NewGuid(), CancellationToken.None);

        var summary = await query.GetSummaryAsync(work.Id, CancellationToken.None);
        Assert.IsNotNull(summary);
        Assert.AreEqual(WorkMediaType.Series, summary!.MediaType);
        Assert.AreEqual(2, summary.Identities.Count);
        Assert.AreEqual(1, summary.Titles.Count);
        Assert.AreEqual(1, summary.Sources.Count);
    }
}
