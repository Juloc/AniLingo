using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Vocabulary;

namespace AniLingo.Tests;

[TestClass]
public sealed class EpisodePreparationPlannerTests
{
    [TestMethod]
    public void SelectsMinimalRankedPrefixToReachNinetyFivePercent()
    {
        var known = Term("known", 75, 75, UserTermState.Known);
        var first = Term("first", 10, 20, null);
        var second = Term("second", 10, 50, null);
        var third = Term("third", 5, 500, null);

        var snapshot = EpisodePreparationPlanner.Build([known, first, second, third]);

        CollectionAssert.AreEqual(
            new[] { second.TermId, first.TermId },
            snapshot.RecommendedTermIds.ToArray());
        Assert.AreEqual(75, snapshot.PreparedPercent);
        Assert.AreEqual(2, snapshot.RecommendedCount);
    }

    [TestMethod]
    public void CountsLearningTermsAsPreparedButNotKnown()
    {
        var known = Term("known", 40, 40, UserTermState.Known);
        var learning = Term("learning", 55, 55, UserTermState.Learning);
        var unknown = Term("unknown", 5, 5, null);

        var snapshot = EpisodePreparationPlanner.Build([known, learning, unknown]);

        Assert.AreEqual(40, snapshot.KnownPercent);
        Assert.AreEqual(95, snapshot.PreparedPercent);
        Assert.AreEqual(0, snapshot.RecommendedCount);
    }

    [TestMethod]
    public void UsesCanonicalAsStableFinalTieBreaker()
    {
        var known = Term("known", 90, 90, UserTermState.Known);
        var beta = Term("beta", 5, 10, null);
        var alpha = Term("alpha", 5, 10, null);

        var snapshot = EpisodePreparationPlanner.Build([known, beta, alpha]);

        Assert.AreEqual(alpha.TermId, snapshot.RecommendedTermIds[0]);
    }

    private static EpisodePreparationTerm Term(
        string canonical,
        int episodeOccurrences,
        int animeOccurrences,
        UserTermState? state) =>
        new(
            Guid.NewGuid(),
            canonical,
            null,
            null,
            episodeOccurrences,
            animeOccurrences,
            state);
}
