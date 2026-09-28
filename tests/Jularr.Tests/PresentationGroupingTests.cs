using Jularr.Web.Features.Presentation;

namespace Jularr.Tests;

[TestClass]
public sealed class PresentationGroupingTests
{
    private const string Fallback = "Other episodes";

    [TestMethod]
    public void NoGroups_RendersPlainList()
    {
        var sections = PresentationGrouping.Arrange(
            [],
            [1, 2, 3],
            unit => unit,
            Fallback);

        Assert.AreEqual(0, sections.Count, "With no groups the caller renders the plain list.");
    }

    [TestMethod]
    public void GroupsDefinedButNoneMatch_RendersPlainList()
    {
        var sections = PresentationGrouping.Arrange(
            [Group("Part 3", 0, (100, 110))],
            [1, 2, 3, 4, 5],
            unit => unit,
            Fallback);

        Assert.AreEqual(0, sections.Count);
    }

    [TestMethod]
    public void RealityShow_PartitionsEpisodesIntoNamedGroups()
    {
        // S01E01-E20 stored internally, presented as four person groups (#510 example).
        var items = Enumerable.Range(1, 20).ToArray();
        var sections = PresentationGrouping.Arrange(
            [
                Group("Anna", 0, (1, 5)),
                Group("Lisa", 1, (6, 10)),
                Group("Julia", 2, (11, 15)),
                Group("Finale", 3, (16, 20))
            ],
            items,
            unit => unit,
            Fallback);

        Assert.AreEqual(4, sections.Count);
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, sections[0].Items.ToArray());
        Assert.AreEqual("Anna", sections[0].Name);
        CollectionAssert.AreEqual(new[] { 16, 17, 18, 19, 20 }, sections[3].Items.ToArray());
        Assert.IsFalse(sections.Any(section => section.IsFallback));
    }

    [TestMethod]
    public void MultipleRangesInOneGroup_AreAllClaimed()
    {
        var sections = PresentationGrouping.Arrange(
            [Group("Specials", 0, (1, 2), (9, 10))],
            [1, 2, 3, 9, 10],
            unit => unit,
            Fallback);

        Assert.AreEqual(2, sections.Count);
        CollectionAssert.AreEqual(new[] { 1, 2, 9, 10 }, sections[0].Items.ToArray());
        Assert.IsTrue(sections[1].IsFallback);
        CollectionAssert.AreEqual(new[] { 3 }, sections[1].Items.ToArray());
    }

    [TestMethod]
    public void UncoveredEpisodes_FallIntoTrailingFallbackSection()
    {
        var sections = PresentationGrouping.Arrange(
            [Group("Part 1", 0, (1, 5))],
            [1, 2, 3, 4, 5, 6, 7, 8],
            unit => unit,
            Fallback);

        Assert.AreEqual(2, sections.Count);
        Assert.IsFalse(sections[0].IsFallback);
        Assert.IsTrue(sections[1].IsFallback);
        Assert.AreEqual(Fallback, sections[1].Name);
        CollectionAssert.AreEqual(new[] { 6, 7, 8 }, sections[1].Items.ToArray());
    }

    [TestMethod]
    public void SectionsFollowGroupSortOrder_NotInputOrder()
    {
        var sections = PresentationGrouping.Arrange(
            [
                Group("Second half", 1, (6, 10)),
                Group("First half", 0, (1, 5))
            ],
            Enumerable.Range(1, 10).ToArray(),
            unit => unit,
            Fallback);

        Assert.AreEqual("First half", sections[0].Name);
        Assert.AreEqual("Second half", sections[1].Name);
    }

    [TestMethod]
    public void OverlappingRanges_FirstGroupInOrderClaimsTheEpisode()
    {
        var sections = PresentationGrouping.Arrange(
            [
                Group("Cour 1", 0, (1, 5)),
                Group("Cour 2", 1, (3, 8))
            ],
            Enumerable.Range(1, 8).ToArray(),
            unit => unit,
            Fallback);

        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, sections[0].Items.ToArray());
        CollectionAssert.AreEqual(new[] { 6, 7, 8 }, sections[1].Items.ToArray());
    }

    [TestMethod]
    public void Range_IsInclusiveAndOrderInsensitive()
    {
        var range = new PresentationRange(5, 1);

        Assert.AreEqual(1, range.Low);
        Assert.AreEqual(5, range.High);
        Assert.IsTrue(range.Contains(1));
        Assert.IsTrue(range.Contains(5));
        Assert.IsTrue(range.Contains(3));
        Assert.IsFalse(range.Contains(6));
        Assert.IsFalse(range.Contains(0));
    }

    private static PresentationGroup Group(string name, int order, params (int Start, int End)[] ranges) =>
        new(
            Guid.NewGuid(),
            PresentationMediaType.Anime,
            Guid.NewGuid(),
            name,
            order,
            ranges.Select(range => new PresentationRange(range.Start, range.End)).ToArray(),
            DateTime.UtcNow,
            DateTime.UtcNow);
}
