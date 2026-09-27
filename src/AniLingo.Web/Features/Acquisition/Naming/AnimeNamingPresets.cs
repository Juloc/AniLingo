namespace AniLingo.Web.Features.Acquisition.Naming;

// Presets are starting points only: creating a profile copies the preset, and every stored
// profile is plain data that can be changed without code changes.
public static class AnimeNamingPresets
{
    public const string SonarrDefaultId = "sonarr-default";
    public const string SonarrMediaInfoId = "sonarr-mediainfo";
    public const string AnimeDetailedId = "anime-detailed";

    // Reproduces the Sonarr naming examples Jularr's release parser was built against (#294):
    //   The Series Title's! - S01E01 - Episode Title (1) WEBDL-1080p Proper AVC DTS[DE] [EN+DE] RlsGrp tt12345
    //   The Series Title's! - S01E01-E03 - Episode Title WEBDL-1080p Proper AVC DTS[DE] [EN+DE] RlsGrp tt12345
    // It is the default profile so imports match libraries already named that way.
    public static AnimeNamingProfile SonarrMediaInfo() =>
        new(
            SonarrMediaInfoId,
            "Sonarr with media info",
            SeriesFolderFormat: "{Series Title}",
            SeasonFolderFormat: "Season {season}",
            SpecialsFolderFormat: "Specials",
            StandardEpisodeFormat: "{Series Title} - S{season:00}E{episode:00} - {Episode Title} {Quality Full} {MediaInfo Full} {Release Group} {ImdbId}",
            DailyEpisodeFormat: "{Series Title} - {Air-Date} - {Episode Title} {Quality Full} {MediaInfo Full} {Release Group} {ImdbId}",
            AnimeEpisodeFormat: "{Series Title} - S{season:00}E{episode:00} - {Episode Title} {Quality Full} {MediaInfo Full} {Release Group} {ImdbId}",
            UseSeasonFolders: true,
            MultiEpisodeStyle: AnimeMultiEpisodeStyle.PrefixedRange,
            ReplaceIllegalCharacters: true,
            ColonReplacement: AnimeColonReplacement.Smart);

    // Sonarr v4 NamingConfig.Default: episode formats, "{Series Title}" series folders,
    // "Season {season}" season folders, "Specials", Prefixed Range multi-episode style and
    // Smart colon replacement with illegal-character replacement enabled.
    public static AnimeNamingProfile SonarrDefault() =>
        new(
            SonarrDefaultId,
            "Sonarr default",
            SeriesFolderFormat: "{Series Title}",
            SeasonFolderFormat: "Season {season}",
            SpecialsFolderFormat: "Specials",
            StandardEpisodeFormat: "{Series Title} - S{season:00}E{episode:00} - {Episode Title} {Quality Full}",
            DailyEpisodeFormat: "{Series Title} - {Air-Date} - {Episode Title} {Quality Full}",
            AnimeEpisodeFormat: "{Series Title} - S{season:00}E{episode:00} - {Episode Title} {Quality Full}",
            UseSeasonFolders: true,
            MultiEpisodeStyle: AnimeMultiEpisodeStyle.PrefixedRange,
            ReplaceIllegalCharacters: true,
            ColonReplacement: AnimeColonReplacement.Smart);

    // Common anime layout (absolute number, release details and group) built only from
    // release-derived tokens Jularr can fill without probing the media file.
    public static AnimeNamingProfile AnimeDetailed() =>
        new(
            AnimeDetailedId,
            "Anime detailed",
            SeriesFolderFormat: "{Series TitleYear}",
            SeasonFolderFormat: "Season {season:00}",
            SpecialsFolderFormat: "Specials",
            StandardEpisodeFormat: "{Series TitleYear} - S{season:00}E{episode:00} - {Episode CleanTitle} [{Quality Full}]{[MediaInfo VideoDynamicRangeType]}{[MediaInfo VideoCodec]}{[MediaInfo AudioCodec]}{MediaInfo AudioLanguages}{-Release Group}",
            DailyEpisodeFormat: "{Series TitleYear} - {Air-Date} - {Episode CleanTitle} [{Quality Full}]{-Release Group}",
            AnimeEpisodeFormat: "{Series TitleYear} - S{season:00}E{episode:00} - {absolute:000} - {Episode CleanTitle} [{Quality Full}]{[MediaInfo VideoDynamicRangeType]}{[MediaInfo VideoCodec]}{[MediaInfo AudioCodec]}{MediaInfo AudioLanguages}{-Release Group}",
            UseSeasonFolders: true,
            MultiEpisodeStyle: AnimeMultiEpisodeStyle.PrefixedRange,
            ReplaceIllegalCharacters: true,
            ColonReplacement: AnimeColonReplacement.Smart);

    public static IReadOnlyList<AnimeNamingProfile> All { get; } = [SonarrMediaInfo(), SonarrDefault(), AnimeDetailed()];

    public static AnimeNamingState CreateDefaultState() =>
        new(
            Version: 1,
            DefaultProfileId: SonarrMediaInfoId,
            Profiles: [SonarrMediaInfo(), SonarrDefault()],
            LibraryProfileAssignments: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            AnimeAssignments: new Dictionary<string, AnimeNamingAssignment>(StringComparer.OrdinalIgnoreCase));
}
