namespace AniLingo.Web.Features.PlaybackSessions;

public sealed record PlaybackSessionState(
    Guid SessionId,
    Guid OwnerProfileId,
    Guid EpisodeId,
    long PositionMs,
    long? DurationMs,
    bool IsPlaying,
    double PlaybackRate,
    string? AudioTrackId,
    string? SubtitleTrackId,
    long? CurrentCueId,
    string? CurrentCueText,
    Guid? SelectedTermId,
    long Revision,
    DateTimeOffset UpdatedAtUtc);

public sealed record PlaybackSessionUpdate(
    long PositionMs,
    long? DurationMs,
    bool IsPlaying,
    double PlaybackRate,
    string? AudioTrackId,
    string? SubtitleTrackId,
    long? CurrentCueId,
    string? CurrentCueText,
    Guid? SelectedTermId);

public sealed record PlaybackPairingMaterial(
    Guid SessionId,
    string Code,
    string Token,
    DateTimeOffset ExpiresAtUtc);

public sealed record PlaybackParticipantGrant(
    Guid SessionId,
    string AccessToken,
    PlaybackSessionState State);

public sealed record PlaybackSessionCommand(
    Guid CommandId,
    Guid SessionId,
    long ExpectedRevision,
    string Type,
    IReadOnlyDictionary<string, string> Payload,
    DateTimeOffset SentAtUtc);

public enum PlaybackCommandAcceptance
{
    Accepted,
    Duplicate,
    StaleRevision,
    SessionNotFound,
    Unauthorized
}

public sealed record PlaybackCommandResult(
    PlaybackCommandAcceptance Acceptance,
    PlaybackSessionState? State,
    PlaybackSessionCommand? Command)
{
    public bool Accepted => Acceptance == PlaybackCommandAcceptance.Accepted;
}

public enum PlaybackPairingFailure
{
    None,
    InvalidOrExpired,
    RateLimited
}

public sealed record PlaybackPairingResult(
    PlaybackPairingFailure Failure,
    PlaybackParticipantGrant? Grant)
{
    public bool Success => Failure == PlaybackPairingFailure.None && Grant is not null;
}
