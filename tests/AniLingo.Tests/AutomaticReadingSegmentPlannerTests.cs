using AniLingo.Web.Features.MediaMapping;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniLingo.Tests;

[TestClass]
public sealed class AutomaticReadingSegmentPlannerTests
{
    [TestMethod]
    public void UniqueTwoPartSequenceBuildsChapterAndVolumeOffsets()
    {
        var local = Enumerable.Range(1, 24)
            .Select(number => new LocalReadingChapter(
                number,
                number switch
                {
                    <= 6 => 1,
                    <= 12 => 2,
                    <= 18 => 3,
                    _ => 4
                }))
            .ToArray();

        var remote = new[]
        {
            new RemoteReadingPart(
                "anilist",
                "100",
                "Part 1",
                ChapterCount: 12,
                VolumeCount: 2),
            new RemoteReadingPart(
                "anilist",
                "101",
                "Part 2",
                ChapterCount: 12,
                VolumeCount: 2)
        };

        var plan = AutomaticReadingSegmentPlanner.Plan(
            local,
            remote,
            anchorExternalId: "100");

        Assert.IsTrue(plan.CanApply);
        Assert.AreEqual(2, plan.Segments.Count);

        var first = plan.Segments[0];
        Assert.AreEqual(1d, first.LocalChapterStart);
        Assert.AreEqual(12d, first.LocalChapterEnd);
        Assert.AreEqual("100", first.RemotePart.ExternalId);
        Assert.AreEqual(1, first.RemoteChapterStart);
        Assert.AreEqual(1, first.LocalVolumeStart);
        Assert.AreEqual(2, first.LocalVolumeEnd);
        Assert.AreEqual(1, first.RemoteVolumeStart);

        var second = plan.Segments[1];
        Assert.AreEqual(13d, second.LocalChapterStart);
        Assert.AreEqual(24d, second.LocalChapterEnd);
        Assert.AreEqual("101", second.RemotePart.ExternalId);
        Assert.AreEqual(3, second.LocalVolumeStart);
        Assert.AreEqual(4, second.LocalVolumeEnd);
        Assert.AreEqual(1, second.RemoteVolumeStart);
    }

    [TestMethod]
    public void OffsetSingleEntryCreatesSegmentWhenLocalNumberingDoesNotStartAtOne()
    {
        var local = Enumerable.Range(101, 12)
            .Select(number => new LocalReadingChapter(number))
            .ToArray();

        var plan = AutomaticReadingSegmentPlanner.Plan(
            local,
            [
                new RemoteReadingPart(
                    "anilist",
                    "200",
                    "Imported Volume",
                    ChapterCount: 12)
            ],
            anchorExternalId: "200");

        Assert.IsTrue(plan.CanApply);
        Assert.AreEqual(1, plan.Segments.Count);
        Assert.AreEqual(101d, plan.Segments[0].LocalChapterStart);
        Assert.AreEqual(112d, plan.Segments[0].LocalChapterEnd);
        Assert.AreEqual(1, plan.Segments[0].RemoteChapterStart);
    }

    [TestMethod]
    public void MatchingPrimaryNumberingNeedsNoSegmentFile()
    {
        var local = Enumerable.Range(1, 12)
            .Select(number => new LocalReadingChapter(number))
            .ToArray();

        var plan = AutomaticReadingSegmentPlanner.Plan(
            local,
            [
                new RemoteReadingPart(
                    "anilist",
                    "300",
                    "Normal Work",
                    ChapterCount: 12)
            ],
            anchorExternalId: "300");

        Assert.IsFalse(plan.CanApply);
        Assert.IsTrue(plan.NoMappingRequired);
    }

    [TestMethod]
    public void MultipleWindowsAroundAnchorAreRejected()
    {
        var local = Enumerable.Range(1, 24)
            .Select(number => new LocalReadingChapter(number))
            .ToArray();

        var plan = AutomaticReadingSegmentPlanner.Plan(
            local,
            [
                new RemoteReadingPart("anilist", "400", "Previous", 12),
                new RemoteReadingPart("anilist", "401", "Anchor", 12),
                new RemoteReadingPart("anilist", "402", "Next", 12)
            ],
            anchorExternalId: "401");

        Assert.IsFalse(plan.CanApply);
        StringAssert.Contains(plan.Reason, "More than one");
    }

    [TestMethod]
    public void UnknownRemoteChapterCountBlocksAutomaticMapping()
    {
        var plan = AutomaticReadingSegmentPlanner.Plan(
            [
                new LocalReadingChapter(1),
                new LocalReadingChapter(2)
            ],
            [
                new RemoteReadingPart(
                    "anilist",
                    "500",
                    "Unknown",
                    ChapterCount: 0)
            ],
            anchorExternalId: "500");

        Assert.IsFalse(plan.CanApply);
        StringAssert.Contains(plan.Reason, "reliable chapter counts");
    }

    [TestMethod]
    public void LocalChapterGapsBlockAutomaticMapping()
    {
        var plan = AutomaticReadingSegmentPlanner.Plan(
            [
                new LocalReadingChapter(1),
                new LocalReadingChapter(3)
            ],
            [
                new RemoteReadingPart(
                    "anilist",
                    "600",
                    "Gap",
                    ChapterCount: 2)
            ],
            anchorExternalId: "600");

        Assert.IsFalse(plan.CanApply);
        StringAssert.Contains(plan.Reason, "gaps");
    }

    [TestMethod]
    public async Task RelationTraversalRejectsBranchingBackLinks()
    {
        var nodes = new Dictionary<string, LinearRelationNode<string>>(
            StringComparer.Ordinal)
        {
            ["A"] = new(
                "Anchor",
                ["P"],
                []),
            ["P"] = new(
                "Previous",
                [],
                ["A", "X"])
        };

        var sequence = await LinearRelationSequence.ResolveAsync(
            "A",
            (id, _) => Task.FromResult(
                nodes.TryGetValue(id, out var node)
                    ? node
                    : null),
            CancellationToken.None);

        Assert.IsFalse(sequence.IsUnambiguous);
        StringAssert.Contains(sequence.Reason, "branch");
    }

    [TestMethod]
    public async Task RelationTraversalReturnsLinearPrequelAndSequelOrder()
    {
        var nodes = new Dictionary<string, LinearRelationNode<string>>(
            StringComparer.Ordinal)
        {
            ["A"] = new(
                "Anchor",
                ["P"],
                ["N"]),
            ["P"] = new(
                "Previous",
                [],
                ["A"]),
            ["N"] = new(
                "Next",
                ["A"],
                [])
        };

        var sequence = await LinearRelationSequence.ResolveAsync(
            "A",
            (id, _) => Task.FromResult(
                nodes.TryGetValue(id, out var node)
                    ? node
                    : null),
            CancellationToken.None);

        Assert.IsTrue(sequence.IsUnambiguous);
        CollectionAssert.AreEqual(
            new[] { "Previous", "Anchor", "Next" },
            sequence.Entries.ToArray());
    }

    [TestMethod]
    public async Task ManualSegmentSetCannotBeOverwrittenAutomatically()
    {
        var directory = TempDirectory();

        try
        {
            var store = new ReadingSegmentMappingStore(
                NullLogger<ReadingSegmentMappingStore>.Instance,
                new DirectoryInfo(directory));
            var localId = Guid.NewGuid().ToString();

            await store.AddAsync(
                Mapping(
                    localId,
                    "700",
                    1,
                    12));

            var replaced = await store.ReplaceAutomaticAsync(
                "manga",
                localId,
                [
                    Mapping(
                        localId,
                        "701",
                        1,
                        12) with
                    {
                        Source = "automatic"
                    }
                ]);

            Assert.IsFalse(replaced);

            var mappings = await store.ListAsync("manga", localId);
            Assert.AreEqual(1, mappings.Count);
            Assert.AreEqual("700", mappings[0].ExternalId);
            Assert.AreEqual("manual", mappings[0].Source);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [TestMethod]
    public async Task AutomaticSegmentSetCanBeReconciledAtomically()
    {
        var directory = TempDirectory();

        try
        {
            var store = new ReadingSegmentMappingStore(
                NullLogger<ReadingSegmentMappingStore>.Instance,
                new DirectoryInfo(directory));
            var localId = Guid.NewGuid().ToString();

            Assert.IsTrue(await store.ReplaceAutomaticAsync(
                "novel",
                localId,
                [
                    Mapping(
                        localId,
                        "800",
                        1,
                        12,
                        mediaType: "novel") with
                    {
                        Source = "automatic"
                    }
                ]));

            Assert.IsTrue(await store.ReplaceAutomaticAsync(
                "novel",
                localId,
                [
                    Mapping(
                        localId,
                        "801",
                        1,
                        6,
                        mediaType: "novel") with
                    {
                        Source = "automatic"
                    },
                    Mapping(
                        localId,
                        "802",
                        7,
                        12,
                        mediaType: "novel") with
                    {
                        Source = "automatic"
                    }
                ]));

            var mappings = await store.ListAsync("novel", localId);
            Assert.AreEqual(2, mappings.Count);
            Assert.IsTrue(mappings.All(x => x.Source == "automatic"));
            CollectionAssert.AreEquivalent(
                new[] { "801", "802" },
                mappings.Select(x => x.ExternalId).ToArray());
        }
        finally
        {
            TryDelete(directory);
        }
    }

    private static ReadingMediaSegmentMapping Mapping(
        string localId,
        string externalId,
        double localStart,
        double localEnd,
        string mediaType = "manga") =>
        new(
            Guid.NewGuid(),
            mediaType,
            localId,
            localStart,
            localEnd,
            1,
            "anilist",
            externalId,
            $"AniList {externalId}",
            (int)Math.Round(localEnd - localStart + 1),
            null,
            null,
            null,
            DateTimeOffset.UtcNow);

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-auto-segments-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
