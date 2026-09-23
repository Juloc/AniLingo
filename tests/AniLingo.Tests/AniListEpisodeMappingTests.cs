using AniLingo.Web.Features.Metadata;
using AniLingo.Web.Features.Tracking;

namespace AniLingo.Tests;

[TestClass]
public sealed class AniListEpisodeMappingTests
{
    [TestMethod]
    public void PartTwoMappingTranslatesLocalEpisodeThirteenToRemoteEpisodeOne()
    {
        var mapping = Mapping(
            season: 2,
            localStart: 13,
            localEnd: 24,
            remoteStart: 1,
            episodeCount: 12);

        Assert.IsTrue(mapping.Contains(2, 13));
        Assert.IsTrue(mapping.Contains(2, 24));
        Assert.IsFalse(mapping.Contains(2, 12));
        Assert.IsFalse(mapping.Contains(1, 13));
        Assert.AreEqual(1, mapping.ResolveRemoteEpisode(13));
        Assert.AreEqual(12, mapping.ResolveRemoteEpisode(24));
    }

    [TestMethod]
    public void OverlapDetectionIsScopedToLocalSeason()
    {
        var mapping = Mapping(
            season: 2,
            localStart: 13,
            localEnd: 24,
            remoteStart: 1,
            episodeCount: 12);

        Assert.IsTrue(AnimeEpisodeMetadataRules.Overlaps(mapping, 2, 12, 13));
        Assert.IsTrue(AnimeEpisodeMetadataRules.Overlaps(mapping, 2, 24, 30));
        Assert.IsFalse(AnimeEpisodeMetadataRules.Overlaps(mapping, 2, 1, 12));
        Assert.IsFalse(AnimeEpisodeMetadataRules.Overlaps(mapping, 3, 13, 24));
    }

    [TestMethod]
    public void AutomaticEndUsesRemainingAniListEpisodes()
    {
        Assert.AreEqual(
            12,
            AnimeEpisodeMetadataRules.ResolveAutomaticLocalEnd(
                localEpisodeStart: 1,
                localSeasonMaximum: 24,
                remoteEpisodeStart: 1,
                remoteEpisodeCount: 12));

        Assert.AreEqual(
            18,
            AnimeEpisodeMetadataRules.ResolveAutomaticLocalEnd(
                localEpisodeStart: 13,
                localSeasonMaximum: 24,
                remoteEpisodeStart: 7,
                remoteEpisodeCount: 12));

        Assert.AreEqual(
            3,
            AnimeEpisodeMetadataRules.ResolveAutomaticLocalEnd(
                localEpisodeStart: 3,
                localSeasonMaximum: 10,
                remoteEpisodeStart: 1,
                remoteEpisodeCount: null));
    }

    [TestMethod]
    public void ResolvedPartProgressUsesRemoteNumberForSafety()
    {
        var mapping = Mapping(
            season: 2,
            localStart: 13,
            localEnd: 24,
            remoteStart: 1,
            episodeCount: 12);

        var requestedProgress = mapping.ResolveRemoteEpisode(14);
        var preview = AniListAccountService.EvaluateRemoteProgressSafety(
            Remote(progress: 0),
            requestedProgress,
            mapping.EpisodeCount,
            mapping.PreferredTitle);

        Assert.AreEqual(2, requestedProgress);
        Assert.IsTrue(preview.CanSync);
        Assert.AreEqual(2, preview.RequestedProgress);
    }

    [TestMethod]
    public void FinalEpisodeOfMappedPartKeepsExistingCompletionGuard()
    {
        var mapping = Mapping(
            season: 2,
            localStart: 13,
            localEnd: 24,
            remoteStart: 1,
            episodeCount: 12);

        var requestedProgress = mapping.ResolveRemoteEpisode(24);
        var preview = AniListAccountService.EvaluateRemoteProgressSafety(
            Remote(progress: 11),
            requestedProgress,
            mapping.EpisodeCount,
            mapping.PreferredTitle);

        Assert.AreEqual(12, requestedProgress);
        Assert.IsFalse(preview.CanSync);
        StringAssert.Contains(preview.Message, "final");
    }

    private static AnimeEpisodeMetadataMapping Mapping(
        int season,
        int localStart,
        int localEnd,
        int remoteStart,
        int? episodeCount) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            season,
            localStart,
            localEnd,
            remoteStart,
            AniListMetadataProvider.ProviderKey,
            "12345",
            "Test Part",
            episodeCount,
            DateTimeOffset.UtcNow);

    private static AniListRemoteListEntry Remote(int progress) =>
        new(
            Id: 1,
            UserId: 2,
            MediaId: 12345,
            Status: "CURRENT",
            Progress: progress,
            Score: null,
            Repeat: 0,
            Priority: 0,
            Private: false,
            Notes: null,
            HiddenFromStatusLists: false,
            CustomLists: null,
            AdvancedScores: null,
            StartedAt: null,
            CompletedAt: null,
            UpdatedAt: null);
}
