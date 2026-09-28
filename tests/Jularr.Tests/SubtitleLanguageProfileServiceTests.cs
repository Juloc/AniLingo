using Jularr.Web.Features.Subtitles;
using Jularr.Web.Features.Watchlist;

namespace Jularr.Tests;

// Owner-managed language profiles and their media-type/library-root assignment (#526): CRUD,
// the root -> media-type -> global-default fallback chain, and the zero-configuration bootstrap.
[TestClass]
public sealed class SubtitleLanguageProfileServiceTests
{
    [TestMethod]
    public async Task ResolvingWithNoProfilesBootstrapsADefaultFromTheContentLanguage()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var service = new SubtitleLanguageProfileService(fixture.Db);

        var resolution = await service.ResolveAsync(WatchlistMediaType.Anime, null, CancellationToken.None);

        Assert.AreEqual(SubtitleLanguageProfileService.DefaultProfileName, resolution.Profile.Name);
        Assert.AreEqual(1, resolution.Items.Count);
        Assert.AreEqual("ja", resolution.Items[0].LanguageTag);
    }

    [TestMethod]
    public async Task FirstProfileCreatedBecomesTheGlobalDefault()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var service = new SubtitleLanguageProfileService(fixture.Db);

        var id = await service.UpsertAsync(
            null, "English", [new SubtitleLanguageProfileItemInput("en", false, false)], null, CancellationToken.None);

        var resolution = await service.ResolveAsync(WatchlistMediaType.Anime, null, CancellationToken.None);
        Assert.AreEqual(id, resolution.Profile.Id);

        var detail = await service.GetDetailAsync(id, CancellationToken.None);
        Assert.IsNotNull(detail);
        Assert.IsTrue(detail!.IsGlobalDefault);
    }

    [TestMethod]
    public async Task LibraryRootAssignmentTakesPriorityOverMediaTypeWhichTakesPriorityOverGlobalDefault()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var (_, _, _, root) = await fixture.AddEpisodeAsync();
        var service = new SubtitleLanguageProfileService(fixture.Db);

        var globalId = await service.UpsertAsync(
            null, "Global", [new SubtitleLanguageProfileItemInput("ja", false, false)], null, CancellationToken.None);
        var animeId = await service.UpsertAsync(
            null, "AnimeDefault", [new SubtitleLanguageProfileItemInput("en", false, false)], null, CancellationToken.None);
        var rootId = await service.UpsertAsync(
            null, "RootSpecific", [new SubtitleLanguageProfileItemInput("de", false, false)], null, CancellationToken.None);

        await service.AssignMediaTypeAsync(WatchlistMediaType.Anime, animeId, CancellationToken.None);

        // No root-specific override yet: media-type assignment wins over the global default.
        var beforeRootAssignment = await service.ResolveAsync(WatchlistMediaType.Anime, root.Id, CancellationToken.None);
        Assert.AreEqual(animeId, beforeRootAssignment.Profile.Id);

        // A different media type still falls back to the global default.
        var otherMediaType = await service.ResolveAsync(WatchlistMediaType.Manga, root.Id, CancellationToken.None);
        Assert.AreEqual(globalId, otherMediaType.Profile.Id);

        await service.AssignLibraryRootAsync(root.Id, rootId, CancellationToken.None);

        var afterRootAssignment = await service.ResolveAsync(WatchlistMediaType.Anime, root.Id, CancellationToken.None);
        Assert.AreEqual(rootId, afterRootAssignment.Profile.Id);

        // Clearing the root override falls back to the media-type assignment again.
        await service.AssignLibraryRootAsync(root.Id, null, CancellationToken.None);
        var afterClearing = await service.ResolveAsync(WatchlistMediaType.Anime, root.Id, CancellationToken.None);
        Assert.AreEqual(animeId, afterClearing.Profile.Id);
    }

    [TestMethod]
    public async Task CannotDeleteAProfileThatIsAssigned()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var service = new SubtitleLanguageProfileService(fixture.Db);

        var id = await service.UpsertAsync(
            null, "Only", [new SubtitleLanguageProfileItemInput("en", false, false)], null, CancellationToken.None);

        var deleted = await service.DeleteAsync(id, CancellationToken.None);

        Assert.IsFalse(deleted);
    }

    [TestMethod]
    public async Task DeletingAnUnassignedProfileSucceeds()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var service = new SubtitleLanguageProfileService(fixture.Db);

        await service.UpsertAsync(
            null, "Default", [new SubtitleLanguageProfileItemInput("en", false, false)], null, CancellationToken.None);
        var unusedId = await service.UpsertAsync(
            null, "Unused", [new SubtitleLanguageProfileItemInput("de", false, false)], null, CancellationToken.None);

        var deleted = await service.DeleteAsync(unusedId, CancellationToken.None);

        Assert.IsTrue(deleted);
        Assert.IsNull(await service.GetDetailAsync(unusedId, CancellationToken.None));
    }

    [TestMethod]
    public async Task UpsertRejectsDuplicateNamesEmptyItemListsAndOutOfRangeCutoffs()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var service = new SubtitleLanguageProfileService(fixture.Db);

        await service.UpsertAsync(
            null, "English", [new SubtitleLanguageProfileItemInput("en", false, false)], null, CancellationToken.None);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => service.UpsertAsync(
            null, "English", [new SubtitleLanguageProfileItemInput("de", false, false)], null, CancellationToken.None));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => service.UpsertAsync(
            null, "Empty", [], null, CancellationToken.None));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => service.UpsertAsync(
            null, "BadCutoff", [new SubtitleLanguageProfileItemInput("en", false, false)], 5, CancellationToken.None));
    }

    [TestMethod]
    public async Task UpsertPreservesItemOrderAndCutoffPosition()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var service = new SubtitleLanguageProfileService(fixture.Db);

        var id = await service.UpsertAsync(
            null,
            "Multi",
            [
                new SubtitleLanguageProfileItemInput("en", false, false),
                new SubtitleLanguageProfileItemInput("en", true, false),
                new SubtitleLanguageProfileItemInput("de", false, false)
            ],
            cutoffPosition: 1,
            CancellationToken.None);

        var detail = await service.GetDetailAsync(id, CancellationToken.None);

        Assert.IsNotNull(detail);
        Assert.AreEqual(1, detail!.CutoffPosition);
        Assert.AreEqual(3, detail.Items.Count);
        Assert.AreEqual("en", detail.Items[0].LanguageTag);
        Assert.IsFalse(detail.Items[0].Forced);
        Assert.AreEqual("en", detail.Items[1].LanguageTag);
        Assert.IsTrue(detail.Items[1].Forced);
        Assert.AreEqual("de", detail.Items[2].LanguageTag);
    }

    [TestMethod]
    public async Task SetGlobalDefaultReplacesThePreviousGlobalDefault()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var service = new SubtitleLanguageProfileService(fixture.Db);

        var first = await service.UpsertAsync(
            null, "First", [new SubtitleLanguageProfileItemInput("en", false, false)], null, CancellationToken.None);
        var second = await service.UpsertAsync(
            null, "Second", [new SubtitleLanguageProfileItemInput("de", false, false)], null, CancellationToken.None);

        await service.SetGlobalDefaultAsync(second, CancellationToken.None);

        var resolution = await service.ResolveAsync(WatchlistMediaType.Anime, null, CancellationToken.None);
        Assert.AreEqual(second, resolution.Profile.Id);

        var firstDetail = await service.GetDetailAsync(first, CancellationToken.None);
        Assert.IsFalse(firstDetail!.IsGlobalDefault);
    }
}
