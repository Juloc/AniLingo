using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AniLingo.Web.Features.PlaybackSessions;

public sealed class PlaybackSessionStore
{
    public static readonly TimeSpan PairingLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ManualAttemptWindow = TimeSpan.FromMinutes(1);
    public const int MaxManualAttemptsPerWindow = 6;
    private const int RememberedCommandIds = 256;

    private readonly object gate = new();
    private readonly TimeProvider timeProvider;
    private readonly Dictionary<Guid, SessionEntry> sessions = [];
    private readonly Dictionary<string, ManualAttemptBucket> manualAttempts =
        new(StringComparer.Ordinal);

    public PlaybackSessionStore(TimeProvider? timeProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public PlaybackSessionState Create(
        Guid ownerProfileId,
        Guid episodeId,
        PlaybackSessionUpdate initial)
    {
        ValidateUpdate(initial);

        lock (gate)
        {
            var now = timeProvider.GetUtcNow();
            var sessionId = Guid.NewGuid();
            var state = ToState(
                sessionId,
                ownerProfileId,
                episodeId,
                revision: 1,
                initial,
                now);
            sessions.Add(sessionId, new SessionEntry(state));
            return state;
        }
    }

    public PlaybackSessionState? Get(Guid sessionId)
    {
        lock (gate)
        {
            return sessions.TryGetValue(sessionId, out var entry)
                ? entry.State
                : null;
        }
    }

    public PlaybackSessionState? Update(
        Guid sessionId,
        Guid ownerProfileId,
        long expectedRevision,
        PlaybackSessionUpdate update)
    {
        ValidateUpdate(update);

        lock (gate)
        {
            if (!sessions.TryGetValue(sessionId, out var entry) ||
                entry.State.OwnerProfileId != ownerProfileId ||
                entry.State.Revision != expectedRevision)
            {
                return null;
            }

            entry.State = ToState(
                entry.State.SessionId,
                entry.State.OwnerProfileId,
                entry.State.EpisodeId,
                entry.State.Revision + 1,
                update,
                timeProvider.GetUtcNow());
            return entry.State;
        }
    }

    public PlaybackPairingMaterial? CreatePairing(
        Guid sessionId,
        Guid ownerProfileId)
    {
        lock (gate)
        {
            if (!sessions.TryGetValue(sessionId, out var entry) ||
                entry.State.OwnerProfileId != ownerProfileId)
            {
                return null;
            }

            var code = CreateUniqueCode();
            var token = CreateToken();
            var pairing = new PairingEntry(
                Code: code,
                TokenHash: HashToken(token),
                ExpiresAtUtc: timeProvider.GetUtcNow() + PairingLifetime);
            entry.Pairing = pairing;

            return new PlaybackPairingMaterial(
                sessionId,
                code,
                token,
                pairing.ExpiresAtUtc);
        }
    }

    public PlaybackPairingResult PairWithToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return InvalidPairing();
        }

        var tokenHash = HashToken(token);

        lock (gate)
        {
            CleanupExpiredPairings();
            var match = sessions.Values.FirstOrDefault(
                x => x.Pairing is not null &&
                    CryptographicOperations.FixedTimeEquals(
                        x.Pairing.TokenHash,
                        tokenHash));

            return match is null
                ? InvalidPairing()
                : CompletePairing(match);
        }
    }

    public PlaybackPairingResult PairWithCode(
        string code,
        string attemptKey)
    {
        var normalizedCode = NormalizeCode(code);
        if (normalizedCode is null)
        {
            return InvalidPairing();
        }

        lock (gate)
        {
            if (!AllowManualAttempt(attemptKey))
            {
                return new PlaybackPairingResult(
                    PlaybackPairingFailure.RateLimited,
                    null);
            }

            CleanupExpiredPairings();
            var match = sessions.Values.FirstOrDefault(
                x => x.Pairing?.Code == normalizedCode);

            return match is null
                ? InvalidPairing()
                : CompletePairing(match);
        }
    }

    public PlaybackSessionState? GetForParticipant(
        Guid sessionId,
        string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        var tokenHash = HashToken(accessToken);
        lock (gate)
        {
            if (!sessions.TryGetValue(sessionId, out var entry))
            {
                return null;
            }

            return entry.ParticipantTokenHashes.Any(
                hash => CryptographicOperations.FixedTimeEquals(hash, tokenHash))
                ? entry.State
                : null;
        }
    }

    public PlaybackCommandResult AcceptCommand(
        PlaybackSessionCommand command,
        string accessToken)
    {
        if (command.CommandId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.Type) ||
            string.IsNullOrWhiteSpace(accessToken))
        {
            return new PlaybackCommandResult(
                PlaybackCommandAcceptance.Unauthorized,
                null,
                null);
        }

        var tokenHash = HashToken(accessToken);

        lock (gate)
        {
            if (!sessions.TryGetValue(command.SessionId, out var entry))
            {
                return new PlaybackCommandResult(
                    PlaybackCommandAcceptance.SessionNotFound,
                    null,
                    null);
            }

            if (!entry.ParticipantTokenHashes.Any(
                    hash => CryptographicOperations.FixedTimeEquals(
                        hash,
                        tokenHash)))
            {
                return new PlaybackCommandResult(
                    PlaybackCommandAcceptance.Unauthorized,
                    entry.State,
                    null);
            }

            if (entry.CommandIds.Contains(command.CommandId))
            {
                return new PlaybackCommandResult(
                    PlaybackCommandAcceptance.Duplicate,
                    entry.State,
                    null);
            }

            if (command.ExpectedRevision != entry.State.Revision)
            {
                return new PlaybackCommandResult(
                    PlaybackCommandAcceptance.StaleRevision,
                    entry.State,
                    null);
            }

            entry.CommandIds.Add(command.CommandId);
            entry.CommandOrder.Enqueue(command.CommandId);
            while (entry.CommandOrder.Count > RememberedCommandIds)
            {
                entry.CommandIds.Remove(entry.CommandOrder.Dequeue());
            }

            return new PlaybackCommandResult(
                PlaybackCommandAcceptance.Accepted,
                entry.State,
                command);
        }
    }

    public bool RevokeParticipants(
        Guid sessionId,
        Guid ownerProfileId)
    {
        lock (gate)
        {
            if (!sessions.TryGetValue(sessionId, out var entry) ||
                entry.State.OwnerProfileId != ownerProfileId)
            {
                return false;
            }

            entry.ParticipantTokenHashes.Clear();
            entry.Pairing = null;
            return true;
        }
    }

    public bool End(
        Guid sessionId,
        Guid ownerProfileId)
    {
        lock (gate)
        {
            return sessions.TryGetValue(sessionId, out var entry) &&
                entry.State.OwnerProfileId == ownerProfileId &&
                sessions.Remove(sessionId);
        }
    }

    private PlaybackPairingResult CompletePairing(SessionEntry entry)
    {
        var pairing = entry.Pairing;
        if (pairing is null || pairing.ExpiresAtUtc <= timeProvider.GetUtcNow())
        {
            entry.Pairing = null;
            return InvalidPairing();
        }

        var accessToken = CreateToken();
        entry.ParticipantTokenHashes.Add(HashToken(accessToken));
        entry.Pairing = null;

        return new PlaybackPairingResult(
            PlaybackPairingFailure.None,
            new PlaybackParticipantGrant(
                entry.State.SessionId,
                accessToken,
                entry.State));
    }

    private bool AllowManualAttempt(string attemptKey)
    {
        var key = string.IsNullOrWhiteSpace(attemptKey)
            ? "unknown"
            : attemptKey.Trim();
        var now = timeProvider.GetUtcNow();

        if (!manualAttempts.TryGetValue(key, out var bucket) ||
            now - bucket.WindowStart >= ManualAttemptWindow)
        {
            manualAttempts[key] = new ManualAttemptBucket(now, 1);
            return true;
        }

        if (bucket.Count >= MaxManualAttemptsPerWindow)
        {
            return false;
        }

        manualAttempts[key] = bucket with { Count = bucket.Count + 1 };
        return true;
    }

    private void CleanupExpiredPairings()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var entry in sessions.Values)
        {
            if (entry.Pairing?.ExpiresAtUtc <= now)
            {
                entry.Pairing = null;
            }
        }

        foreach (var key in manualAttempts
                     .Where(x => now - x.Value.WindowStart >= ManualAttemptWindow)
                     .Select(x => x.Key)
                     .ToArray())
        {
            manualAttempts.Remove(key);
        }
    }

    private string CreateUniqueCode()
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var code = RandomNumberGenerator
                .GetInt32(0, 1_000_000)
                .ToString("D6", CultureInfo.InvariantCulture);

            if (sessions.Values.All(x => x.Pairing?.Code != code))
            {
                return code;
            }
        }

        throw new InvalidOperationException(
            "Could not allocate a unique playback pairing code.");
    }

    private static string CreateToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static byte[] HashToken(string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static string? NormalizeCode(string code)
    {
        var value = code?.Trim();
        return value is { Length: 6 } && value.All(char.IsAsciiDigit)
            ? value
            : null;
    }

    private static PlaybackPairingResult InvalidPairing() =>
        new(PlaybackPairingFailure.InvalidOrExpired, null);

    private static void ValidateUpdate(PlaybackSessionUpdate update)
    {
        if (update.PositionMs < 0 ||
            update.DurationMs is < 0 ||
            !double.IsFinite(update.PlaybackRate) ||
            update.PlaybackRate <= 0 ||
            update.PlaybackRate > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(update),
                "Playback state contains invalid timing or playback-rate values.");
        }
    }

    private static PlaybackSessionState ToState(
        Guid sessionId,
        Guid ownerProfileId,
        Guid episodeId,
        long revision,
        PlaybackSessionUpdate update,
        DateTimeOffset now) =>
        new(
            sessionId,
            ownerProfileId,
            episodeId,
            update.PositionMs,
            update.DurationMs,
            update.IsPlaying,
            update.PlaybackRate,
            update.AudioTrackId,
            update.SubtitleTrackId,
            update.CurrentCueId,
            update.CurrentCueText,
            update.SelectedTermId,
            revision,
            now);

    private sealed class SessionEntry(PlaybackSessionState state)
    {
        public PlaybackSessionState State { get; set; } = state;
        public PairingEntry? Pairing { get; set; }
        public List<byte[]> ParticipantTokenHashes { get; } = [];
        public HashSet<Guid> CommandIds { get; } = [];
        public Queue<Guid> CommandOrder { get; } = [];
    }

    private sealed record PairingEntry(
        string Code,
        byte[] TokenHash,
        DateTimeOffset ExpiresAtUtc);

    private sealed record ManualAttemptBucket(
        DateTimeOffset WindowStart,
        int Count);
}
