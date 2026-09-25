using System.Text.Json.Serialization;

namespace AniLingo.Web.Features.PlaybackSessions;

public sealed record PlaybackSessionToken(
    string Surface,
    Guid? TermId,
    string? Canonical,
    string? Reading,
    string? Meaning,
    string State);

public sealed record PlaybackSessionState(
    Guid SessionId,
    string OwnerProfileId,
    Guid EpisodeId,
    string AnimeTitle,
    string EpisodeTitle,
    long PositionMs,
    long? DurationMs,
    bool IsPlaying,
    double PlaybackRate,
    string? AudioTrackId,
    string? SubtitleTrackId,
    long? CurrentCueId,
    string? CurrentCueText,
    IReadOnlyList<PlaybackSessionToken> CurrentCueTokens,
    Guid? SelectedTermId,
    long Revision,
    DateTimeOffset UpdatedAtUtc);

public sealed record PlaybackSessionPublicState(
    Guid SessionId,
    Guid EpisodeId,
    string AnimeTitle,
    string EpisodeTitle,
    long PositionMs,
    long? DurationMs,
    bool IsPlaying,
    double PlaybackRate,
    string? AudioTrackId,
    string? SubtitleTrackId,
    long? CurrentCueId,
    string? CurrentCueText,
    IReadOnlyList<PlaybackSessionToken> CurrentCueTokens,
    Guid? SelectedTermId,
    long Revision,
    DateTimeOffset UpdatedAtUtc);

public sealed record PlaybackSessionUpdate(
    string AnimeTitle,
    string EpisodeTitle,
    long PositionMs,
    long? DurationMs,
    bool IsPlaying,
    double PlaybackRate,
    string? AudioTrackId,
    string? SubtitleTrackId,
    long? CurrentCueId,
    string? CurrentCueText,
    IReadOnlyList<PlaybackSessionToken> CurrentCueTokens,
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

public sealed record PlaybackSessionCreateRequest(
    Guid EpisodeId,
    PlaybackSessionUpdate State);

public sealed record PlaybackSessionOwnerResponse(
    PlaybackSessionPublicState State,
    string HubUrl);

public sealed record PlaybackSessionStateUpdateRequest(
    long ExpectedRevision,
    PlaybackSessionUpdate State);

public sealed record PlaybackPairingResponse(
    Guid SessionId,
    string Code,
    string Token,
    string CompanionUrl,
    DateTimeOffset ExpiresAtUtc);

public sealed record PlaybackPairRequest(
    string? Token,
    string? Code);

public sealed record PlaybackParticipantResponse(
    Guid SessionId,
    string AccessToken,
    PlaybackSessionPublicState State,
    string HubUrl);

public sealed record PlaybackCommandApiResult(
    string Acceptance,
    PlaybackSessionPublicState? State,
    PlaybackSessionCommand? Command);

public sealed record PlaybackCommandRequest(
    Guid CommandId,
    long ExpectedRevision,
    string Type,
    IReadOnlyDictionary<string, string>? Payload = null);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlaybackParticipantAccessKind
{
    Owner,
    Companion
}


public static class PlaybackSessionMappings
{
    public static PlaybackSessionPublicState ToPublic(
        PlaybackSessionState state) =>
        new(
            state.SessionId,
            state.EpisodeId,
            state.AnimeTitle,
            state.EpisodeTitle,
            state.PositionMs,
            state.DurationMs,
            state.IsPlaying,
            state.PlaybackRate,
            state.AudioTrackId,
            state.SubtitleTrackId,
            state.CurrentCueId,
            state.CurrentCueText,
            state.CurrentCueTokens,
            state.SelectedTermId,
            state.Revision,
            state.UpdatedAtUtc);
}
