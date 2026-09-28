using Jularr.Web.Features.Subtitles;

namespace Jularr.Tests;

// Pure cutoff/priority evaluation (#526): no DB, no I/O - just "given these per-item satisfied
// flags and this cutoff position, is the profile complete / cutoff-met / missing something".
[TestClass]
public sealed class SubtitleCompletionEvaluatorTests
{
    [TestMethod]
    public void NoWantedLanguagesIsComplete()
    {
        var result = SubtitleCompletionEvaluator.Evaluate([], cutoffPosition: null);

        Assert.AreEqual(SubtitleProfileCompletionStatus.Complete, result.Status);
        Assert.AreEqual(0, result.MissingIndexes.Count);
    }

    [TestMethod]
    public void NoCutoffRequiresEveryItem()
    {
        var result = SubtitleCompletionEvaluator.Evaluate([true, false, true], cutoffPosition: null);

        Assert.AreEqual(SubtitleProfileCompletionStatus.Missing, result.Status);
        CollectionAssert.AreEqual(new[] { 1 }, result.MissingIndexes.ToArray());
    }

    [TestMethod]
    public void NoCutoffAndEverythingSatisfiedIsComplete()
    {
        var result = SubtitleCompletionEvaluator.Evaluate([true, true], cutoffPosition: null);

        Assert.AreEqual(SubtitleProfileCompletionStatus.Complete, result.Status);
        Assert.AreEqual(0, result.MissingIndexes.Count);
    }

    [TestMethod]
    public void CutoffMetByHigherPriorityItemStopsRequiringTheRest()
    {
        // en (0) satisfied, de (1) is the cutoff, ja (2) unmet - top priority already beats cutoff.
        var result = SubtitleCompletionEvaluator.Evaluate([true, false, false], cutoffPosition: 1);

        Assert.AreEqual(SubtitleProfileCompletionStatus.CutoffMet, result.Status);
        Assert.AreEqual(0, result.MissingIndexes.Count);
    }

    [TestMethod]
    public void CutoffMetExactlyAtCutoffItem()
    {
        var result = SubtitleCompletionEvaluator.Evaluate([false, true], cutoffPosition: 1);

        Assert.AreEqual(SubtitleProfileCompletionStatus.CutoffMet, result.Status);
        Assert.AreEqual(0, result.MissingIndexes.Count);
    }

    [TestMethod]
    public void EverySatisfiedUpToAndIncludingCutoffIsCompleteNotJustCutoffMet()
    {
        var result = SubtitleCompletionEvaluator.Evaluate([true, true], cutoffPosition: 1);

        Assert.AreEqual(SubtitleProfileCompletionStatus.Complete, result.Status);
    }

    [TestMethod]
    public void CutoffNotYetMetListsOnlyItemsUpToTheCutoffAsMissing()
    {
        // Cutoff at index 2; index 3 unmet too but it is past the cutoff so it is not "missing" yet.
        var result = SubtitleCompletionEvaluator.Evaluate([false, false, false, false], cutoffPosition: 2);

        Assert.AreEqual(SubtitleProfileCompletionStatus.Missing, result.Status);
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, result.MissingIndexes.ToArray());
    }

    [TestMethod]
    public void PartiallySatisfiedBeforeCutoffOnlyListsTheUnmetOnes()
    {
        // Index 0 (highest priority) unmet, index 1 unmet, index 2 satisfied but that is *past*
        // the cutoff (index 1): satisfying a lower-priority item never counts as reaching cutoff.
        var result = SubtitleCompletionEvaluator.Evaluate([false, false, true], cutoffPosition: 1);

        Assert.AreEqual(SubtitleProfileCompletionStatus.Missing, result.Status);
        CollectionAssert.AreEqual(new[] { 0, 1 }, result.MissingIndexes.ToArray());
    }

    [TestMethod]
    public void SatisfyingOnlyALowerPriorityItemDoesNotMeetTheCutoff()
    {
        var result = SubtitleCompletionEvaluator.Evaluate([false, true, false], cutoffPosition: 0);

        Assert.AreEqual(SubtitleProfileCompletionStatus.Missing, result.Status);
        CollectionAssert.AreEqual(new[] { 0 }, result.MissingIndexes.ToArray());
    }
}
