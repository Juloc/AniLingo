using Jularr.Web.Features.Naming;

namespace Jularr.Web.Features.Tv;

/// <summary>
/// TV library naming (#594): the three-level layout <c>Series (Year)/Season 01/Series - S01E02 - Title.ext</c>
/// (season 0 is the <c>Specials</c> folder). Names are sanitised through the shared
/// <see cref="NamingTemplateEngine"/> so they match the rest of Jularr's placement and are safe on every
/// filesystem/SMB share. Configurable naming profiles are a later follow-up; these are the built-in defaults.
/// </summary>
public static class TvNaming
{
    /// <summary>The series folder, e.g. <c>Breaking Bad (2008)</c>.</summary>
    public static string SeriesFolderName(string series, int? year)
    {
        var clean = NamingTemplateEngine.CleanFileName(series ?? "");
        if (clean.Length == 0)
        {
            clean = "Untitled";
        }

        return year is int value and > 0 ? $"{clean} ({value})" : clean;
    }

    /// <summary>The season folder, e.g. <c>Season 01</c>; season 0 is <c>Specials</c>.</summary>
    public static string SeasonFolderName(int seasonNumber) =>
        seasonNumber <= 0 ? "Specials" : $"Season {NamingTemplateEngine.Pad(seasonNumber, 2)}";

    /// <summary>The episode file name, e.g. <c>Breaking Bad - S01E02 - Cat's in the Bag.mkv</c> (source extension kept).</summary>
    public static string EpisodeFileName(
        string series,
        int seasonNumber,
        int episodeNumber,
        string? episodeTitle,
        string extension)
    {
        var seriesClean = NamingTemplateEngine.CleanFileName(series ?? "");
        if (seriesClean.Length == 0)
        {
            seriesClean = "Untitled";
        }

        var tag = $"S{NamingTemplateEngine.Pad(Math.Max(seasonNumber, 0), 2)}E{NamingTemplateEngine.Pad(Math.Max(episodeNumber, 0), 2)}";
        var name = $"{seriesClean} - {tag}";
        if (!string.IsNullOrWhiteSpace(episodeTitle))
        {
            var titleClean = NamingTemplateEngine.CleanFileName(episodeTitle!);
            if (titleClean.Length > 0)
            {
                name += $" - {titleClean}";
            }
        }

        return name + NormalizeExtension(extension);
    }

    private static string NormalizeExtension(string extension)
    {
        var trimmed = (extension ?? "").Trim();
        if (trimmed.Length == 0)
        {
            return "";
        }

        return trimmed.StartsWith('.') ? trimmed : "." + trimmed;
    }
}
