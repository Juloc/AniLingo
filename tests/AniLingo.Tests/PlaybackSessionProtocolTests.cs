using System.Text.Json;
using AniLingo.Web.Features.PlaybackSessions;

namespace AniLingo.Tests;

[TestClass]
public sealed class PlaybackSessionProtocolTests
{
    [TestMethod]
    public void PublicStateNeverContainsProfileIdentity()
    {
        var internalState = new PlaybackSessionState(
            Guid.NewGuid(),
            "owner",
            Guid.NewGuid(),
            "Anime",
            "Episode 1",
            1200,
            60000,
            true,
            1.0,
            "audio:jpn",
            "subtitle:jpn",
            4,
            "猫です。",
            [
                new PlaybackSessionToken(
                    "猫",
                    Guid.NewGuid(),
                    "猫",
                    "ねこ",
                    "cat",
                    "learning")
            ],
            null,
            2,
            DateTimeOffset.UtcNow);

        var publicState = PlaybackSessionMappings.ToPublic(internalState);
        var json = JsonSerializer.Serialize(publicState);

        Assert.IsFalse(
            json.Contains("OwnerProfileId", StringComparison.Ordinal));
        Assert.AreEqual(internalState.SessionId, publicState.SessionId);
        Assert.AreEqual("猫です。", publicState.CurrentCueText);
        Assert.AreEqual("cat", publicState.CurrentCueTokens[0].Meaning);
    }

    [TestMethod]
    public void CommandAllowListRejectsUnknownActions()
    {
        Assert.IsTrue(PlaybackSessionProtocol.IsSupportedCommand("playPause"));
        Assert.IsTrue(PlaybackSessionProtocol.IsSupportedCommand("repeatCurrentCue"));
        Assert.IsTrue(PlaybackSessionProtocol.IsSupportedCommand("openWord"));
        Assert.IsFalse(PlaybackSessionProtocol.IsSupportedCommand("formatDisk"));
        Assert.IsFalse(PlaybackSessionProtocol.IsSupportedCommand(""));
        Assert.IsFalse(PlaybackSessionProtocol.IsSupportedCommand(null));
    }
}
