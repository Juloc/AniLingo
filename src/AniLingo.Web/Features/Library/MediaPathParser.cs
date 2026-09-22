using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Library;

public sealed record MediaDescriptor(string AnimeTitle, string AnimeKey, int SeasonNumber, int EpisodeNumber, string EpisodeTitle);

public static partial class MediaPathParser
{
    [GeneratedRegex(@"S(?<season>\d{1,2})E(?<episode>\d{1,3})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonEpisodeRegex();

    [GeneratedRegex(@"Season[ ._-]*(?<season>\d{1,2})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonDirectoryRegex();

    [GeneratedRegex(@"(?:^|[ ._-])(?:E(?:P)?[ ._-]*)?(?<episode>\d{1,3})(?=(?:v\d+)?(?:[ ._\-\[]|$))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EpisodeRegex();

    [GeneratedRegex(@"^\[[^\]]+\][ ._-]*", RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseGroupRegex();

    public static bool TryParse(string rootPath, string filePath, out MediaDescriptor descriptor)
    {
        descriptor = default!;

        var relative = Path.GetRelativePath(rootPath, filePath);
        if (relative.StartsWith("..", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var seasonEpisode = SeasonEpisodeRegex().Match(fileName);

        var season = 1;
        int episode;

        if (seasonEpisode.Success)
        {
            season = int.Parse(seasonEpisode.Groups["season"].Value);
            episode = int.Parse(seasonEpisode.Groups["episode"].Value);
        }
        else
        {
            var parent = parts.Length > 1 ? parts[^2] : "";
            var seasonDirectory = SeasonDirectoryRegex().Match(parent);
            if (seasonDirectory.Success)
            {
                season = int.Parse(seasonDirectory.Groups["season"].Value);
            }

            var episodeMatch = EpisodeRegex().Match(fileName);
            if (!episodeMatch.Success)
            {
                return false;
            }

            episode = int.Parse(episodeMatch.Groups["episode"].Value);
        }

        var animeTitle = parts.Length > 1
            ? CleanTitle(parts[0])
            : CleanTitle(SeasonEpisodeRegex().Replace(fileName, ""));

        if (string.IsNullOrWhiteSpace(animeTitle))
        {
            return false;
        }

        var episodeTitle = CleanTitle(SeasonEpisodeRegex().Replace(fileName, ""));
        if (episodeTitle.Equals(animeTitle, StringComparison.OrdinalIgnoreCase))
        {
            episodeTitle = $"Episode {episode}";
        }

        descriptor = new MediaDescriptor(
            animeTitle,
            NormalizeKey(animeTitle),
            season,
            episode,
            episodeTitle);

        return true;
    }

    private static string CleanTitle(string value)
    {
        var withoutGroup = ReleaseGroupRegex().Replace(value, "");
        return Regex.Replace(withoutGroup, @"[._]+", " ").Trim(' ', '-', '_', '.');
    }

    private static string NormalizeKey(string value) =>
        Regex.Replace(value.Normalize().Trim().ToLowerInvariant(), @"\s+", " ");
}
