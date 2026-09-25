using AniLingo.Web.Features.PlaybackSessions;

namespace AniLingo.Tests;

[TestClass]
public sealed class PlaybackSessionStoreTests
{
    [TestMethod]
    public void PairingTokenIsSingleUseAndSessionCanHaveMultipleParticipants()
    {
        var store = new PlaybackSessionStore();
        var owner = "owner";
        var state = store.Create(owner, Guid.NewGuid(), Initial());
        var firstPairing = store.CreatePairing(state.SessionId, owner)!;

        var first = store.PairWithToken(firstPairing.Token);
        var reused = store.PairWithToken(firstPairing.Token);
        var secondPairing = store.CreatePairing(state.SessionId, owner)!;
        var second = store.PairWithCode(secondPairing.Code, "phone-2");

        Assert.IsTrue(first.Success);
        Assert.AreEqual(
            PlaybackPairingFailure.InvalidOrExpired,
            reused.Failure);
        Assert.IsTrue(second.Success);
        Assert.IsNotNull(store.GetForParticipant(
            state.SessionId,
            first.Grant!.AccessToken));
        Assert.IsNotNull(store.GetForParticipant(
            state.SessionId,
            second.Grant!.AccessToken));
    }

    [TestMethod]
    public void PairingExpiresAfterFiveMinutes()
    {
        var time = new TestTimeProvider(
            new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));
        var store = new PlaybackSessionStore(time);
        var owner = "owner";
        var state = store.Create(owner, Guid.NewGuid(), Initial());
        var pairing = store.CreatePairing(state.SessionId, owner)!;

        time.Advance(TimeSpan.FromMinutes(5));

        var result = store.PairWithToken(pairing.Token);

        Assert.AreEqual(
            PlaybackPairingFailure.InvalidOrExpired,
            result.Failure);
    }

    [TestMethod]
    public void ManualCodeAttemptsAreBoundedPerAttemptKey()
    {
        var store = new PlaybackSessionStore();

        for (var index = 0;
             index < PlaybackSessionStore.MaxManualAttemptsPerWindow;
             index++)
        {
            var attempt = store.PairWithCode("999999", "same-client");
            Assert.AreEqual(
                PlaybackPairingFailure.InvalidOrExpired,
                attempt.Failure);
        }

        var limited = store.PairWithCode("999999", "same-client");
        Assert.AreEqual(
            PlaybackPairingFailure.RateLimited,
            limited.Failure);

        var anotherClient = store.PairWithCode("999999", "another-client");
        Assert.AreEqual(
            PlaybackPairingFailure.InvalidOrExpired,
            anotherClient.Failure);
    }

    [TestMethod]
    public void StateRevisionMustMatchAndIncrementsMonotonically()
    {
        var store = new PlaybackSessionStore();
        var owner = "owner";
        var state = store.Create(owner, Guid.NewGuid(), Initial());

        var updated = store.Update(
            state.SessionId,
            owner,
            expectedRevision: state.Revision,
            Initial(positionMs: 42000, isPlaying: true));

        var stale = store.Update(
            state.SessionId,
            owner,
            expectedRevision: state.Revision,
            Initial(positionMs: 50000, isPlaying: true));

        Assert.IsNotNull(updated);
        Assert.AreEqual(state.Revision + 1, updated.Revision);
        Assert.AreEqual(42000L, updated.PositionMs);
        Assert.IsNull(stale);
    }

    [TestMethod]
    public void CommandsRejectUnauthorizedDuplicateAndStaleRequests()
    {
        var store = new PlaybackSessionStore();
        var owner = "owner";
        var state = store.Create(owner, Guid.NewGuid(), Initial());
        var pairing = store.CreatePairing(state.SessionId, owner)!;
        var participant = store.PairWithToken(pairing.Token).Grant!;
        var commandId = Guid.NewGuid();
        var acceptedCommand = new PlaybackSessionCommand(
            commandId,
            state.SessionId,
            state.Revision,
            "playPause",
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow);

        var unauthorized = store.AcceptCommand(
            acceptedCommand,
            "not-a-participant");
        var accepted = store.AcceptCommand(
            acceptedCommand,
            participant.AccessToken);
        var duplicate = store.AcceptCommand(
            acceptedCommand,
            participant.AccessToken);

        var updated = store.Update(
            state.SessionId,
            owner,
            state.Revision,
            Initial(positionMs: 1000, isPlaying: true))!;

        var stale = store.AcceptCommand(
            new PlaybackSessionCommand(
                Guid.NewGuid(),
                state.SessionId,
                state.Revision,
                "seekForward10",
                new Dictionary<string, string>(),
                DateTimeOffset.UtcNow),
            participant.AccessToken);

        Assert.AreEqual(
            PlaybackCommandAcceptance.Unauthorized,
            unauthorized.Acceptance);
        Assert.AreEqual(
            PlaybackCommandAcceptance.Accepted,
            accepted.Acceptance);
        Assert.AreEqual(
            PlaybackCommandAcceptance.Duplicate,
            duplicate.Acceptance);
        Assert.AreEqual(state.Revision + 1, updated.Revision);
        Assert.AreEqual(
            PlaybackCommandAcceptance.StaleRevision,
            stale.Acceptance);
        Assert.AreEqual(updated.Revision, stale.State!.Revision);
    }

    [TestMethod]
    public void RevokingParticipantsImmediatelyInvalidatesAccess()
    {
        var store = new PlaybackSessionStore();
        var owner = "owner";
        var state = store.Create(owner, Guid.NewGuid(), Initial());
        var pairing = store.CreatePairing(state.SessionId, owner)!;
        var participant = store.PairWithToken(pairing.Token).Grant!;

        Assert.IsTrue(store.RevokeParticipants(state.SessionId, owner));
        Assert.IsNull(store.GetForParticipant(
            state.SessionId,
            participant.AccessToken));
    }

    [TestMethod]
    public void EndingSessionRemovesEphemeralState()
    {
        var store = new PlaybackSessionStore();
        var owner = "owner";
        var state = store.Create(owner, Guid.NewGuid(), Initial());

        Assert.IsTrue(store.End(state.SessionId, owner));
        Assert.IsNull(store.Get(state.SessionId));
    }

    private static PlaybackSessionUpdate Initial(
        long positionMs = 0,
        bool isPlaying = false) =>
        new(
            "Anime",
            "Episode 1",
            positionMs,
            60000,
            isPlaying,
            1.0,
            "audio:jpn",
            "subtitle:jpn",
            12,
            "日本語です。",
            [],
            null);

    private sealed class TestTimeProvider(DateTimeOffset now)
        : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan value)
        {
            current += value;
        }
    }
}
