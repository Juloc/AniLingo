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
