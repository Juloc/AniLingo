using Jularr.Web.Features.Mapping;

namespace Jularr.Tests;

[TestClass]
public sealed class AnimeMappingPlannerTests
{
    private static LocalEpisodeRef[] Season(int season, int count) =>
        Enumerable.Range(1, count).Select(n => new LocalEpisodeRef(season, n)).ToArray();

    [TestMethod]
    public void ContiguousRange_IsExactAndApplies()
    {
        var preview = AnimeMappingPlanner.BuildPreview(
            Season(1, 3),
            [new AnimeMappingRange(1, 1, 3, "anilist", "100", 1, RemoteEpisodeCount: 3)]);

        Assert.IsTrue(preview.CanApply);
        Assert.AreEqual(3, preview.ExactCount);
        Assert.AreEqual(0, preview.MissingCount);
        Assert.AreEqual(AnimeMappingState.Exact, preview.Ranges[0].State);
        Assert.AreEqual(1, preview.Episodes[0].RemoteEpisode);
        Assert.AreEqual(3, preview.Episodes[2].RemoteEpisode);
    }

    [TestMethod]
    public void UncoveredEpisode_IsMissingButStillApplies()
    {
        var preview = AnimeMappingPlanner.BuildPreview(
            Season(1, 3),
            [new AnimeMappingRange(1, 1, 2, "anilist", "100", 1)]);

        Assert.IsTrue(preview.CanApply);
        Assert.AreEqual(2, preview.ExactCount);
        Assert.AreEqual(1, preview.MissingCount);
        Assert.AreEqual(AnimeMappingState.Missing, preview.Episodes[2].State);
    }

    [TestMethod]
    public void OverlappingRanges_ConflictAndBlockApply()
    {
        var preview = AnimeMappingPlanner.BuildPreview(
            Season(1, 4),
            [
                new AnimeMappingRange(1, 1, 3, "anilist", "100", 1),
                new AnimeMappingRange(1, 2, 4, "anilist", "200", 1)
            ]);

        Assert.IsFalse(preview.CanApply);
        Assert.IsTrue(preview.ConflictCount > 0);
        Assert.IsTrue(preview.Problems.Count > 0);
        Assert.IsTrue(preview.Ranges.All(r => r.State == AnimeMappingState.Conflict));
    }

    [TestMethod]
    public void RemoteOverflow_MarksTailPartial()
    {
        var preview = AnimeMappingPlanner.BuildPreview(
            Season(1, 3),
            [new AnimeMappingRange(1, 1, 3, "anilist", "100", 1, RemoteEpisodeCount: 2)]);

        Assert.IsTrue(preview.CanApply);
        Assert.AreEqual(2, preview.ExactCount);
        Assert.AreEqual(1, preview.PartialCount);
        Assert.AreEqual(AnimeMappingState.Partial, preview.Episodes[2].State);
        Assert.AreEqual(AnimeMappingState.Partial, preview.Ranges[0].State);
    }

    [TestMethod]
    public void RangeDeclaringMissingLocalEpisodes_IsPartial()
    {
        var preview = AnimeMappingPlanner.BuildPreview(
            Season(1, 2),
            [new AnimeMappingRange(1, 1, 3, "anilist", "100", 1)]);

        Assert.AreEqual(AnimeMappingState.Partial, preview.Ranges[0].State);
        Assert.AreEqual(2, preview.ExactCount);
    }

    [TestMethod]
    public void ExplicitUnmappedSpecials_AreUnmapped()
    {
        var preview = AnimeMappingPlanner.BuildPreview(
            [new LocalEpisodeRef(0, 1), new LocalEpisodeRef(0, 2)],
            [new AnimeMappingRange(0, 1, 2, "", "", 0, Unmapped: true)]);

        Assert.IsTrue(preview.CanApply);
        Assert.AreEqual(2, preview.UnmappedCount);
        Assert.IsTrue(preview.Episodes.All(x => x.IsSpecial));
        Assert.AreEqual(AnimeMappingState.Unmapped, preview.Ranges[0].State);
    }

    [TestMethod]
    public void SingleEpisodeOverride_ResolvesThatEpisodeOnly()
    {
        var preview = AnimeMappingPlanner.BuildPreview(
            Season(2, 12),
            [new AnimeMappingRange(2, 13, 13, "anilist", "200", 1)]);

        // Season 2 has episodes 1..12, so the E13 override covers nothing that exists yet.
        Assert.AreEqual(AnimeMappingState.Partial, preview.Ranges[0].State);
        Assert.AreEqual(12, preview.MissingCount);
    }

    [TestMethod]
    public void InvalidRange_IsConflictAndBlocksApply()
    {
        var preview = AnimeMappingPlanner.BuildPreview(
            Season(1, 3),
            [new AnimeMappingRange(1, 0, -1, "anilist", "100", 1)]);

        Assert.IsFalse(preview.CanApply);
        Assert.AreEqual(AnimeMappingState.Conflict, preview.Ranges[0].State);
    }
}
