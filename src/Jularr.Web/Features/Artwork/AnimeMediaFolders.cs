using Jularr.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Artwork;

/// <summary>
/// An anime's media folders as the library knows them: the series folder (first path segment
/// below the library root, exactly what MediaPathParser discovered the anime from) and each
/// season's own folder (null when its episodes sit directly in the series folder).
/// </summary>
public sealed record AnimeMediaFolder(
    Guid RootId,
    string RootName,
    string FolderName,
    string SeriesDirectory,
    IReadOnlyDictionary<int, string?> SeasonDirectories);

public static class AnimeMediaFolders
{
    /// <summary>Folders of the given anime (all anime when null) from their known media files.</summary>
    public static async Task<IReadOnlyDictionary<Guid, AnimeMediaFolder>> ResolveAsync(
        AppDbContext db,
        IReadOnlyCollection<Guid>? animeIds,
        CancellationToken cancellationToken)
    {
        var mediaQuery =
            from media in db.MediaFiles.AsNoTracking()
            join episode in db.Episodes.AsNoTracking() on media.EpisodeId equals episode.Id
            select new { episode.AnimeId, episode.SeasonNumber, media.LibraryRootId, media.Path };
        if (animeIds is not null)
        {
            mediaQuery = mediaQuery.Where(x => animeIds.Contains(x.AnimeId));
        }

        var mediaFiles = await mediaQuery.ToListAsync(cancellationToken);
        var roots = await db.LibraryRoots
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var folders = new Dictionary<Guid, AnimeMediaFolder>();
        foreach (var anime in mediaFiles.GroupBy(x => x.AnimeId))
        {
            var ordered = anime.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
            var first = ordered[0];
            if (!roots.TryGetValue(first.LibraryRootId, out var root))
            {
                continue;
            }

            var rootPath = Path.GetFullPath(root.Path);
            var folderName = FirstSegment(rootPath, first.Path);
            if (folderName is null)
            {
                continue;
            }

            var seriesDirectory = Path.GetFullPath(Path.Combine(rootPath, folderName));
            var seasons = new Dictionary<int, string?>();
            foreach (var file in ordered.Where(x => x.LibraryRootId == root.Id))
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(file.Path));
                if (string.Equals(directory, seriesDirectory, StringComparison.Ordinal))
                {
                    seasons.TryAdd(file.SeasonNumber, null);
                }
                else if (directory is not null &&
                         directory.StartsWith(seriesDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    seasons.TryAdd(file.SeasonNumber, directory);
                }
            }

            folders[anime.Key] = new AnimeMediaFolder(root.Id, root.Name, folderName, seriesDirectory, seasons);
        }

        return folders;
    }

    private static string? FirstSegment(string rootPath, string mediaPath)
    {
        var parts = Path.GetRelativePath(rootPath, mediaPath).Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2 || parts[0] == ".." ? null : parts[0];
    }
}
