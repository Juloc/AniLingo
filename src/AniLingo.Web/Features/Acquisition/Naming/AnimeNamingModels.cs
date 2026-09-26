namespace AniLingo.Web.Features.Acquisition.Naming;

// Numbering values match Sonarr's MultiEpisodeStyle so exported Sonarr settings map 1:1.
public enum AnimeMultiEpisodeStyle
{
    Extend = 0,
    Duplicate = 1,
    Repeat = 2,
    Scene = 3,
    Range = 4,
    PrefixedRange = 5
}

// Numbering values match Sonarr's ColonReplacementFormat.
public enum AnimeColonReplacement
{
    Delete = 0,
    Dash = 1,
    SpaceDash = 2,
    SpaceDashSpace = 3,
    Smart = 4,
    Custom = 5
}

// Decides which episode template applies (Sonarr semantics): Anime uses the anime template when
// every episode has an absolute number, Daily uses the daily template when an air date is known,
// everything else falls back to the standard template.
public enum AnimeSeriesType
{
    Anime = 0,
    Standard = 1,
    Daily = 2
}

public sealed record AnimeNamingProfile(
    string Id,
    string Name,
    string SeriesFolderFormat,
    string SeasonFolderFormat,
    string SpecialsFolderFormat,
    string StandardEpisodeFormat,
    string DailyEpisodeFormat,
    string AnimeEpisodeFormat,
    bool UseSeasonFolders,
    AnimeMultiEpisodeStyle MultiEpisodeStyle,
    bool ReplaceIllegalCharacters,
    AnimeColonReplacement ColonReplacement,
    string CustomColonReplacement = "");

public sealed record AnimeNamingAssignment(
    string? ProfileId,
    AnimeSeriesType SeriesType = AnimeSeriesType.Anime);

// Canonical persisted naming configuration. Library assignments are keyed by LibraryRoot ID and
// anime assignments by Anime ID (both "D" formatted GUIDs); the effective profile is
// anime assignment > library assignment > default.
public sealed record AnimeNamingState(
    int Version,
    string DefaultProfileId,
    AnimeNamingProfile[] Profiles,
    Dictionary<string, string> LibraryProfileAssignments,
    Dictionary<string, AnimeNamingAssignment> AnimeAssignments);

public sealed record AnimeNamingResolution(
    AnimeNamingProfile Profile,
    AnimeSeriesType SeriesType,
    string Source);

// Provider IDs are metadata/mapping facts only: they can appear in names but never decide the
// folder structure, so one folder may span several AniList entries (seasons/parts).
public sealed record AnimeNamingSeries(
    string Title,
    int? Year = null,
    AnimeSeriesType SeriesType = AnimeSeriesType.Anime,
    string? AniListId = null,
    string? MyAnimeListId = null,
    string? TvdbId = null,
    string? TmdbId = null,
    string? ImdbId = null);

public sealed record AnimeNamingEpisode(
    int SeasonNumber,
    int EpisodeNumber,
    int? AbsoluteEpisodeNumber,
    string Title,
    DateOnly? AirDate = null);

// Release-derived tokens (quality, group, codecs, languages, Proper/Repack/version) always come
// from the shared AnimeReleaseParser result; the naming module never parses release names itself.
public sealed record AnimeNamingRequest(
    AnimeNamingSeries Series,
    IReadOnlyList<AnimeNamingEpisode> Episodes,
    AnimeReleaseInfo? Release = null);
