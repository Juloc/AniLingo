using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Import;

namespace Jularr.Web.Features.Artwork;

/// <summary>
/// The one rule for where a series-level work keeps its artwork on a media type's NAS library
/// root (issue #581): its series folder, the first folder below the configured library root --
/// exactly the folder Jularr's own placement creates for a Light Novel or Manga series, and the
/// same "series folder" convention <see cref="AnimeMediaFolders"/> uses for anime. Media directly
/// in the library root has no series folder of its own (a shared root would make every series
/// fight over one <c>cover.*</c>), so it has no beside-media artwork.
/// </summary>
public static class LibrarySeriesFolder
{
    /// <summary>
    /// The series folder of <paramref name="storedPath"/> on the library root configured for
    /// <paramref name="kind"/>, or null when no root is configured, the path is not below it, or the
    /// folder does not exist (an unavailable NAS). A path that is not below the root as stored is
    /// translated through the media type's remote path mappings first
    /// (<see cref="AnimeImportSettingsState.TranslatePath"/>, the single path resolver), so a path
    /// recorded in the form an external system reported it still finds its folder.
    /// </summary>
    public static string? Resolve(AnimeImportSettingsState settings, MediaAcquisitionKind kind, string? storedPath)
    {
        var libraryRoot = settings.LibraryFor(kind)?.LibraryRoot;
        if (string.IsNullOrWhiteSpace(libraryRoot) || string.IsNullOrWhiteSpace(storedPath))
        {
            return null;
        }

        return Resolve(libraryRoot, storedPath)
               ?? Resolve(libraryRoot, settings.TranslatePath(kind, storedPath));
    }

    /// <summary>The existing first folder below <paramref name="libraryRoot"/> that contains (or is) <paramref name="mediaPath"/>.</summary>
    public static string? Resolve(string? libraryRoot, string? mediaPath)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot) || string.IsNullOrWhiteSpace(mediaPath))
        {
            return null;
        }

        string root;
        string relative;
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(libraryRoot));
            relative = Path.GetRelativePath(root, Path.GetFullPath(mediaPath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        // A path on another drive comes back rooted; one outside the root starts with "..".
        if (Path.IsPathRooted(relative))
        {
            return null;
        }

        var segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments[0] is "." or "..")
        {
            return null;
        }

        var folder = Path.Combine(root, segments[0]);
        return Directory.Exists(folder) ? folder : null;
    }
}
