using AniLingo.Web.Features.Acquisition.Pipeline;

namespace AniLingo.Web.Features.Acquisition.Import;

/// <summary>
/// Naming seam for imported anime files. The configurable naming profiles of #299 replace this
/// class; until then an imported file keeps its original name behind an "Anime - SxxEyy - " prefix
/// so the library scanner (MediaPathParser) maps it to the requested local episode and the release
/// parser can still read its quality for later upgrade decisions.
/// </summary>
public static class AnimeImportDestination
{
    private static readonly char[] InvalidCharacters =
        Path.GetInvalidFileNameChars().Concat([':', '*', '?', '"', '<', '>', '|', '/', '\\']).Distinct().ToArray();

    public static string ResolveDirectory(
        AnimeLibraryLocation location,
        int seasonNumber)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (location.AnimeDirectory is null)
        {
            throw new InvalidOperationException("The anime has no library folder yet.");
        }

        return location.UsesSeasonFolders
            ? Path.Combine(location.AnimeDirectory, $"Season {seasonNumber:00}")
            : location.AnimeDirectory;
    }

    public static string BuildFileName(
        string animeTitle,
        IReadOnlyList<RequestedAnimeEpisode> targets,
        string sourceFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(animeTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);
        if (targets.Count == 0)
        {
            throw new ArgumentException("At least one target episode is required.", nameof(targets));
        }

        var first = targets.MinBy(target => target.EpisodeNumber)!;
        var last = targets.MaxBy(target => target.EpisodeNumber)!;
        var tag = last.EpisodeNumber == first.EpisodeNumber
            ? $"S{first.SeasonNumber:00}E{first.EpisodeNumber:00}"
            : $"S{first.SeasonNumber:00}E{first.EpisodeNumber:00}-E{last.EpisodeNumber:00}";

        var stem = Path.GetFileNameWithoutExtension(sourceFileName);
        var extension = Path.GetExtension(sourceFileName);
        return Sanitize($"{animeTitle} - {tag} - {stem}") + extension.ToLowerInvariant();
    }

    // A sidecar keeps everything after the video stem (for example ".ja.ass") behind the new stem.
    public static string BuildSidecarName(
        string videoSourceName,
        string sidecarSourceName,
        string importedVideoName)
    {
        var videoStem = Path.GetFileNameWithoutExtension(videoSourceName);
        var suffix = sidecarSourceName.StartsWith(videoStem, StringComparison.OrdinalIgnoreCase)
            ? sidecarSourceName[videoStem.Length..]
            : Path.GetExtension(sidecarSourceName);

        return Path.GetFileNameWithoutExtension(importedVideoName) + suffix;
    }

    public static string Sanitize(string value)
    {
        var characters = value
            .Select(character => Array.IndexOf(InvalidCharacters, character) >= 0 ? ' ' : character)
            .ToArray();
        var collapsed = string.Join(' ', new string(characters).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Trim(' ', '.');
    }
}
