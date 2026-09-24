using System.Reflection;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Playback;

namespace AniLingo.Web.Features.ClientApi;

public static class ClientApiContract
{
    public const int ApiVersion = 1;
    public const int MinimumSupportedApiVersion = 1;
    public const string BasePath = "/api/client/v1";

    public static ClientCapabilitiesResponse Capabilities()
    {
        var assembly = typeof(ClientApiContract).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var version = string.IsNullOrWhiteSpace(informational)
            ? assembly.GetName().Version?.ToString() ?? "unknown"
            : informational.Split('+', 2)[0];

        return new ClientCapabilitiesResponse(
            ApiVersion,
            MinimumSupportedApiVersion,
            version,
            new ClientFeatureFlags(
                Library: true,
                NativePlayerBootstrap: true,
                DirectPlayback: true,
                HttpRangeRequests: true,
                MediaTrackMetadata: true,
                NormalizedLearningCues: true,
                LearningStateMutation: true,
                LiveMp4Fallback: true,
                HlsFallback: false,
                PlaybackSessions: false,
                CompanionPairing: false,
                CompanionControl: false));
    }
}

public static class ClientApiRoutes
{
    public static bool IsClientApi(PathString path) =>
        path.StartsWithSegments(
            new PathString(ClientApiContract.BasePath),
            StringComparison.OrdinalIgnoreCase);

    public static string Anime(Guid animeId) =>
        $"{ClientApiContract.BasePath}/anime/{animeId:D}";

    public static string Episode(Guid episodeId) =>
        $"{ClientApiContract.BasePath}/episodes/{episodeId:D}";

    public static string Player(Guid episodeId) =>
        $"{Episode(episodeId)}/player";

    public static string Cues(Guid episodeId) =>
        $"{Episode(episodeId)}/cues";

    public static string DirectContent(Guid mediaFileId) =>
        $"{ClientApiContract.BasePath}/media/{mediaFileId:D}/content";

    public static string Fallback(Guid episodeId) =>
        $"{Episode(episodeId)}/fallback";
}

public sealed record ClientCapabilitiesResponse(
    int ApiVersion,
    int MinimumSupportedApiVersion,
    string ServerVersion,
    ClientFeatureFlags Features);

public sealed record ClientFeatureFlags(
    bool Library,
    bool NativePlayerBootstrap,
    bool DirectPlayback,
    bool HttpRangeRequests,
    bool MediaTrackMetadata,
    bool NormalizedLearningCues,
    bool LearningStateMutation,
    bool LiveMp4Fallback,
    bool HlsFallback,
    bool PlaybackSessions,
    bool CompanionPairing,
    bool CompanionControl);

public sealed record ClientErrorResponse(
    string Code,
    string Message);

public sealed record ClientAccountResponse(
    string ProfileId,
    string? UserName,
    string Role);

public sealed record ClientLibraryResponse(
    IReadOnlyList<ClientAnimeSummary> Anime);

public sealed record ClientAnimeSummary(
    Guid Id,
    string Title,
    string LocalTitle,
    string? NativeTitle,
    string? CoverImageUrl,
    string? BannerImageUrl,
    int EpisodeCount,
    int SeasonCount,
    int? SeasonYear,
    string? Format);

public sealed record ClientAnimeDetail(
    Guid Id,
    string Title,
    string LocalTitle,
    string? NativeTitle,
    string? Description,
    string? CoverImageUrl,
    string? BannerImageUrl,
    int? SeasonYear,
    string? Format,
    IReadOnlyList<ClientSeason> Seasons);

public sealed record ClientSeason(
    int Number,
    IReadOnlyList<ClientEpisodeSummary> Episodes);

public sealed record ClientEpisodeSummary(
    Guid Id,
    int SeasonNumber,
    int Number,
    string Title,
    bool HasMedia,
    bool HasJapaneseLearningSubtitle);

public sealed record ClientEpisodeDetail(
    Guid Id,
    Guid AnimeId,
    string AnimeTitle,
    string Title,
    int SeasonNumber,
    int Number,
    bool HasMedia,
    Guid? ActiveLearningSubtitleTrackId,
    int LearningCueCount,
    ClientLearningCoverage Learning);

public sealed record ClientLearningCoverage(
    int TotalTerms,
    int KnownTerms,
    int LearningTerms,
    int NewTerms);

public sealed record ClientPlayerBootstrap(
    int ApiVersion,
    ClientPlayerEpisode Episode,
    ClientPlayerMedia? Media,
    IReadOnlyList<ClientMediaTrack> AudioTracks,
    IReadOnlyList<ClientMediaTrack> SubtitleTracks,
    IReadOnlyList<ClientLearningSubtitle> LearningSubtitles,
    Guid? ActiveLearningSubtitleTrackId,
    string? DefaultAudioTrackId,
    string? DefaultSubtitleTrackId,
    ClientCompatibilityFallback Fallback);

public sealed record ClientPlayerEpisode(
    Guid Id,
    Guid AnimeId,
    string AnimeTitle,
    string Title,
    int SeasonNumber,
    int Number);

public sealed record ClientPlayerMedia(
    Guid MediaFileId,
    string FileName,
    string ContentType,
    long? SizeBytes,
    long? DurationMs,
    string? VideoCodec,
    string? PixelFormat,
    string? AudioCodec,
    string DirectContentUrl,
    bool SupportsRangeRequests,
    ClientPlaybackOption Device,
    ClientPlaybackOption Server);

public sealed record ClientPlaybackOption(
    string Availability,
    string Message,
    bool UsesLiveStream);

public sealed record ClientMediaTrack(
    string Id,
    int StreamIndex,
    string Kind,
    string? Codec,
    string? Language,
    string? Title,
    bool IsDefault,
    bool IsForced,
    bool IsText);

public sealed record ClientLearningSubtitle(
    Guid TrackId,
    string Language,
    string Format,
    bool IsActive,
    string CuesUrl);

public sealed record ClientCompatibilityFallback(
    bool Available,
    string? Kind,
    bool SeekableWithinStream,
    bool CanRestartAtPosition,
    string? Url);

public sealed record ClientCueResponse(
    Guid? TrackId,
    int? FromMs,
    int? ToMs,
    IReadOnlyList<ClientCue> Cues);

public sealed record ClientCue(
    long Id,
    int StartMs,
    int EndMs,
    string Text,
    IReadOnlyList<ClientCueToken> Tokens);

public sealed record ClientCueToken(
    string Surface,
    Guid? TermId,
    string? Canonical,
    string? Reading,
    string? Meaning,
    string State);

public sealed record ClientTermDetail(
    Guid Id,
    string Canonical,
    string? Reading,
    string? Meaning,
    string State);

public sealed record ClientTermStateUpdate(string State);

public sealed record ClientTermStateResult(
    Guid TermId,
    string State);

public static class ClientApiMappings
{
    public static ClientMediaTrack ToClientTrack(PlaybackMediaTrack track) =>
        new(
            $"stream:{track.StreamIndex}",
            track.StreamIndex,
            track.Kind == PlaybackTrackKind.Audio ? "audio" : "subtitle",
            track.Codec,
            track.Language,
            track.Title,
            track.IsDefault,
            track.IsForced,
            track.IsText);

    public static ClientPlaybackOption ToClientOption(PlaybackOption option) =>
        new(
            option.Availability.ToString().ToLowerInvariant(),
            option.StatusMessage,
            option.UsesLiveStream);

    public static string StateName(UserTermState? state) =>
        state switch
        {
            UserTermState.Known => "known",
            UserTermState.Learning => "learning",
            _ => "new"
        };

    public static ClientCue ToClientCue(PlaybackCue cue)
    {
        var tokens = cue.Tokens
            .Select(token => new ClientCueToken(
                token.Surface,
                token.TermId,
                token.Canonical,
                token.Reading,
                token.Meaning,
                string.IsNullOrWhiteSpace(token.State)
                    ? "new"
                    : token.State.ToLowerInvariant()))
            .ToArray();

        return new ClientCue(
            cue.CueId,
            cue.StartMs,
            cue.EndMs,
            string.Concat(cue.Tokens.Select(x => x.Surface)),
            tokens);
    }
}
