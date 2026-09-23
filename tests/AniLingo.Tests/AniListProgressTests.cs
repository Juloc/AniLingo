using System.Text.Json.Nodes;
using AniLingo.Web.Features.Tracking;

namespace AniLingo.Tests;

[TestClass]
public sealed class AniListProgressTests
{
    [TestMethod]
    public void ProgressMutationContainsOnlyEntryIdAndProgress()
    {
        var variables = AniListAccountService.BuildProgressMutationVariables(
            listEntryId: 123,
            progress: 7);

        Assert.AreEqual(2, variables.Count);
        Assert.AreEqual(123, variables["id"]);
        Assert.AreEqual(7, variables["progress"]);
        CollectionAssert.AreEquivalent(
            new[] { "id", "progress" },
            variables.Keys.ToArray());
    }

    [TestMethod]
    public void AlreadyHigherRemoteProgressIsNoOpAndNeverMovesBackward()
    {
        var preview = AniListAccountService.EvaluateRemoteProgressSafety(
            Remote(status: "CURRENT", progress: 8),
            requestedProgress: 7,
            aniListEpisodeCount: 12,
            mediaTitle: "Test Anime");

        Assert.IsFalse(preview.CanSync);
        Assert.IsTrue(preview.IsNoOp);
        Assert.AreEqual(8, preview.RemoteProgress);
        StringAssert.Contains(preview.Message, "never lowers");
    }

    [TestMethod]
    public void NonCurrentAniListEntryIsNeverModified()
    {
        var preview = AniListAccountService.EvaluateRemoteProgressSafety(
            Remote(status: "PAUSED", progress: 3),
            requestedProgress: 4,
            aniListEpisodeCount: 12,
            mediaTitle: "Test Anime");

        Assert.IsFalse(preview.CanSync);
        Assert.IsFalse(preview.IsNoOp);
        StringAssert.Contains(preview.Message, "CURRENT");
    }

    [TestMethod]
    public void FinalEpisodeIsBlockedToAvoidCompletionSideEffects()
    {
        var preview = AniListAccountService.EvaluateRemoteProgressSafety(
            Remote(status: "CURRENT", progress: 11),
            requestedProgress: 12,
            aniListEpisodeCount: 12,
            mediaTitle: "Test Anime");

        Assert.IsFalse(preview.CanSync);
        Assert.IsFalse(preview.IsNoOp);
        StringAssert.Contains(preview.Message, "final");
    }

    [TestMethod]
    public void CurrentMiddleEpisodeMayIncreaseProgress()
    {
        var preview = AniListAccountService.EvaluateRemoteProgressSafety(
            Remote(status: "CURRENT", progress: 3),
            requestedProgress: 4,
            aniListEpisodeCount: 12,
            mediaTitle: "Test Anime");

        Assert.IsTrue(preview.CanSync);
        Assert.IsFalse(preview.IsNoOp);
        Assert.AreEqual(3, preview.RemoteProgress);
        Assert.AreEqual(4, preview.RequestedProgress);
    }

    [TestMethod]
    public void ProtectedFieldsDetectUnexpectedAniListChanges()
    {
        var before = Remote(status: "CURRENT", progress: 3);
        var progressOnly = before with { Progress = 4 };
        var scoreChanged = before with { Progress = 4, Score = 8.5 };

        Assert.IsTrue(before.ProtectedFieldsEqual(progressOnly));
        Assert.IsFalse(before.ProtectedFieldsEqual(scoreChanged));
    }

    [TestMethod]
    public void ParsesRemoteListFieldsUsedForSafetySnapshot()
    {
        const string json = """
        {
          "data": {
            "MediaList": {
              "id": 123,
              "userId": 42,
              "mediaId": 999,
              "status": "CURRENT",
              "progress": 4,
              "score": 7.5,
              "repeat": 1,
              "priority": 2,
              "private": true,
              "notes": "keep me",
              "hiddenFromStatusLists": true,
              "customLists": { "Favorites": true },
              "advancedScores": { "Story": 8 },
              "startedAt": { "year": 2026, "month": 9, "day": 1 },
              "completedAt": { "year": null, "month": null, "day": null },
              "updatedAt": 123456789
            }
          }
        }
        """;

        var entry = AniListAccountService.ParseListEntryResponse(json, "MediaList");

        Assert.IsNotNull(entry);
        Assert.AreEqual(123, entry.Id);
        Assert.AreEqual(42, entry.UserId);
        Assert.AreEqual(999, entry.MediaId);
        Assert.AreEqual("CURRENT", entry.Status);
        Assert.AreEqual(4, entry.Progress);
        Assert.AreEqual(7.5, entry.Score);
        Assert.AreEqual("keep me", entry.Notes);
        Assert.IsTrue(entry.Private);
        Assert.IsTrue(entry.HiddenFromStatusLists);
        Assert.IsTrue(JsonNode.DeepEquals(
            JsonNode.Parse("""{"Favorites":true}"""),
            entry.CustomLists));
    }

    private static AniListRemoteListEntry Remote(
        string status,
        int progress) =>
        new(
            Id: 123,
            UserId: 42,
            MediaId: 999,
            Status: status,
            Progress: progress,
            Score: 7.0,
            Repeat: 1,
            Priority: 2,
            Private: true,
            Notes: "do not touch",
            HiddenFromStatusLists: true,
            CustomLists: JsonNode.Parse("""{"Favorites":true}"""),
            AdvancedScores: JsonNode.Parse("""{"Story":8}"""),
            StartedAt: new AniListFuzzyDate(2026, 9, 1),
            CompletedAt: null,
            UpdatedAt: 123456789);
}
