namespace AniLingo.Web.Features.Acquisition.Naming;

// Mirrors Sonarr's naming sample (series "The Series Title's!" (2010, tt12345), episodes
// "Episode Title (1)".."(3)" aired 2013-10-30, a Proper WEBDL-1080p AVC/DTS release by RlsGrp
// with German audio and English/German subtitles) so previews can be compared with Sonarr's
// settings page. The release name goes through the shared AnimeReleaseParser.
public static class AnimeNamingSamples
{
    public const string ReleaseName =
        "The Series Title's! - S01E01 - Episode Title (1) WEBDL-1080p Proper AVC DTS[DE] [EN+DE] RlsGrp tt12345";

    public static AnimeNamingSeries Series(AnimeSeriesType seriesType) =>
        new(
            "The Series Title's!",
            2010,
            seriesType,
            AniListId: "98765",
            MyAnimeListId: "54321",
            TvdbId: "12345",
            TmdbId: "11223",
            ImdbId: "tt12345");

    public static AnimeNamingRequest Single(AnimeSeriesType seriesType) =>
        new(Series(seriesType), [Episode(1)], AnimeReleaseParser.Parse(ReleaseName));

    public static AnimeNamingRequest Multi(AnimeSeriesType seriesType) =>
        new(Series(seriesType), [Episode(1), Episode(2), Episode(3)], AnimeReleaseParser.Parse(ReleaseName));

    public static IReadOnlyList<AnimeNamingPreviewLine> Preview(AnimeNamingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var anime = Series(AnimeSeriesType.Anime);
        return
        [
            new("Series folder", AnimeNamingFormatter.BuildSeriesFolderName(profile, anime)),
            new("Season folder", AnimeNamingFormatter.BuildSeasonFolderName(profile, anime, 1) ?? "(episodes stay in the series folder)"),
            new("Specials folder", AnimeNamingFormatter.BuildSeasonFolderName(profile, anime, 0) ?? "(episodes stay in the series folder)"),
            new("Standard episode", AnimeNamingFormatter.BuildEpisodeFileName(profile, Single(AnimeSeriesType.Standard)) + ".mkv"),
            new("Multi-episode", AnimeNamingFormatter.BuildEpisodeFileName(profile, Multi(AnimeSeriesType.Standard)) + ".mkv"),
            new("Daily episode", AnimeNamingFormatter.BuildEpisodeFileName(profile, Single(AnimeSeriesType.Daily)) + ".mkv"),
            new("Anime episode", AnimeNamingFormatter.BuildEpisodeFileName(profile, Single(AnimeSeriesType.Anime)) + ".mkv"),
            new("Anime multi-episode", AnimeNamingFormatter.BuildEpisodeFileName(profile, Multi(AnimeSeriesType.Anime)) + ".mkv")
        ];
    }

    private static AnimeNamingEpisode Episode(int number) =>
        new(1, number, number, $"Episode Title ({number})", new DateOnly(2013, 10, 30));
}

public sealed record AnimeNamingPreviewLine(string Label, string Value);
