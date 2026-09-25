using AniLingo.Web.Features.MediaMapping;

namespace AniLingo.Tests;

[TestClass]
public sealed class UnifiedAniListMappingTests
{
    [TestMethod]
    public void ExactTitleAndCompatibleFormatAutoMatchWhenCandidateIsUnique()
    {
        var decision = AutomaticMediaMatcher.Select(
            new AutomaticMediaMatchInput(
                "Frieren: Beyond Journey's End",
                Format: "ANIME"),
            [
                Candidate(
                    "1",
                    "Frieren: Beyond Journey's End",
                    "TV"),
                Candidate(
                    "2",
                    "Frieren at the Funeral",
                    "TV")
            ]);

        Assert.AreEqual(AutomaticMediaMatchDisposition.Auto, decision.Disposition);
        Assert.AreEqual("1", decision.Candidate?.ExternalId);
        Assert.IsTrue(decision.Score >= AutomaticMediaMatcher.AutoApplyScore);
        Assert.IsTrue(decision.Score - decision.RunnerUpScore >= AutomaticMediaMatcher.MinimumAutoLead);
    }

    [TestMethod]
    public void EquallyStrongTitleMatchesRequireReview()
    {
        var decision = AutomaticMediaMatcher.Select(
            new AutomaticMediaMatchInput(
                "Example",
                Format: "MANGA"),
            [
                Candidate("10", "Example", "MANGA"),
                Candidate("11", "Example", "MANGA")
            ]);

        Assert.AreEqual(AutomaticMediaMatchDisposition.Review, decision.Disposition);
        Assert.IsFalse(decision.CanApply);
    }

    [TestMethod]
    public void EpisodeCountCanDisambiguateAnimeParts()
    {
        var decision = AutomaticMediaMatcher.Select(
            new AutomaticMediaMatchInput(
                "Example Anime",
                UnitCount: 12,
                Format: "ANIME"),
            [
                new AutomaticMediaMatchCandidate(
                    "anilist",
                    "20",
                    "Example Anime",
                    ["Example Anime"],
                    UnitCount: 12,
                    Format: "TV"),
                new AutomaticMediaMatchCandidate(
                    "anilist",
                    "21",
                    "Example Anime",
                    ["Example Anime"],
                    UnitCount: 24,
                    Format: "TV")
            ]);

        Assert.AreEqual(AutomaticMediaMatchDisposition.Auto, decision.Disposition);
        Assert.AreEqual("20", decision.Candidate?.ExternalId);
    }

    [TestMethod]
    public void WeakTitleSimilarityDoesNotAutoApply()
    {
        var decision = AutomaticMediaMatcher.Select(
            new AutomaticMediaMatchInput(
                "Completely Different Work",
                Format: "MANGA"),
            [
                Candidate("30", "Another Series", "MANGA")
            ]);

        Assert.AreEqual(AutomaticMediaMatchDisposition.None, decision.Disposition);
    }

    [TestMethod]
    public void SegmentMappingTranslatesPartOffsets()
    {
        var mapping = new MediaSegmentMapping(
            LocalStart: 13,
            LocalEnd: 24,
            RemoteStart: 1);

        Assert.AreEqual(1, mapping.Resolve(13));
        Assert.AreEqual(12, mapping.Resolve(24));
    }

    [TestMethod]
    public void AnimeSequenceSplitsOneLocalSeasonAcrossAniListParts()
    {
        var local = Enumerable.Range(1, 24)
            .Select(number => new LocalEpisodeCoordinate(1, number))
            .ToArray();
        var remote = new[]
        {
            new RemoteAnimePart("anilist", "100", "Part 1", 12),
            new RemoteAnimePart("anilist", "101", "Part 2", 12)
        };

        var plan = AnimeSequenceMappingPlanner.Plan(
            local,
            remote,
            anchorExternalId: "100");

        Assert.IsTrue(plan.CanApply);
        Assert.AreEqual(2, plan.Ranges.Count);
        Assert.AreEqual(1, plan.Ranges[0].LocalEpisodeStart);
        Assert.AreEqual(12, plan.Ranges[0].LocalEpisodeEnd);
        Assert.AreEqual("100", plan.Ranges[0].RemotePart.ExternalId);
        Assert.AreEqual(13, plan.Ranges[1].LocalEpisodeStart);
        Assert.AreEqual(24, plan.Ranges[1].LocalEpisodeEnd);
        Assert.AreEqual(1, plan.Ranges[1].RemoteEpisodeStart);
        Assert.AreEqual("101", plan.Ranges[1].RemotePart.ExternalId);
    }

    [TestMethod]
    public void AnimeSequenceCanCrossLocalSeasonBoundaryInsideOneAniListEntry()
    {
        var local = Enumerable.Range(1, 6)
            .Select(number => new LocalEpisodeCoordinate(1, number))
            .Concat(Enumerable.Range(1, 6)
                .Select(number => new LocalEpisodeCoordinate(2, number)))
            .ToArray();
        var remote = new[]
        {
            new RemoteAnimePart("anilist", "200", "Combined Part", 12)
        };

        var plan = AnimeSequenceMappingPlanner.Plan(
            local,
            remote,
            anchorExternalId: "200");

        Assert.IsTrue(plan.CanApply);
        Assert.AreEqual(2, plan.Ranges.Count);
        Assert.AreEqual(1, plan.Ranges[0].RemoteEpisodeStart);
        Assert.AreEqual(7, plan.Ranges[1].RemoteEpisodeStart);
    }

    [TestMethod]
    public void AnimeSequenceRejectsAmbiguousWindowAroundAnchor()
    {
        var local = Enumerable.Range(1, 24)
            .Select(number => new LocalEpisodeCoordinate(1, number))
            .ToArray();
        var remote = new[]
        {
            new RemoteAnimePart("anilist", "300", "Previous", 12),
            new RemoteAnimePart("anilist", "301", "Anchor", 12),
            new RemoteAnimePart("anilist", "302", "Next", 12)
        };

        var plan = AnimeSequenceMappingPlanner.Plan(
            local,
            remote,
            anchorExternalId: "301");

        Assert.IsFalse(plan.CanApply);
        StringAssert.Contains(plan.Reason, "More than one");
    }

    [TestMethod]
    public void ReadingProgressUsesLastCompletedIntegerChapter()
    {
        var unfinished = AutomaticMediaMatcher.ResolveReadingProgress(
            localUnitNumber: 12,
            position: 500,
            completedThreshold: 950);
        var finished = AutomaticMediaMatcher.ResolveReadingProgress(
            localUnitNumber: 12,
            position: 1000,
            completedThreshold: 950);

        Assert.IsTrue(unfinished.CanSync);
        Assert.AreEqual(11, unfinished.Progress);
        Assert.IsTrue(finished.CanSync);
        Assert.AreEqual(12, finished.Progress);
    }

    [TestMethod]
    public void FractionalMangaChapterRequiresExplicitMapping()
    {
        var result = AutomaticMediaMatcher.ResolveReadingProgress(
            localUnitNumber: 12.5,
            position: 100,
            completedThreshold: 99);

        Assert.IsFalse(result.CanSync);
        StringAssert.Contains(result.Reason, "explicit mapping");
    }

    private static AutomaticMediaMatchCandidate Candidate(
        string id,
        string title,
        string format) =>
        new(
            "anilist",
            id,
            title,
            [title],
            Format: format);
}
