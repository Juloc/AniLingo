using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Import;
using Jularr.Web.Features.Artwork;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Novels;

/// <summary>
/// Where a Light Novel series keeps its cover beside its media (issue #581): the series folder on
/// the configured Light Novel NAS library root that holds its EPUB volumes
/// (<see cref="NovelVolume.SourceStoragePath"/>, recorded when a completed download or the inbox is
/// placed on the library root, #389/#545). A series without a NAS-resolved volume (no library root
/// configured, a web novel, or not yet imported onto one) has no beside-media folder and simply
/// keeps its provider cover URL.
/// </summary>
public static class NovelArtworkFolders
{
    public static async Task<string?> ResolveAsync(
        AppDbContext db,
        AnimeImportSettingsState settings,
        Guid workId,
        CancellationToken cancellationToken)
    {
        if (settings.LibraryFor(MediaAcquisitionKind.LightNovel) is null)
        {
            return null;
        }

        // Volume order keeps the choice stable as later volumes are imported.
        var storagePaths = await db.NovelVolumes
            .AsNoTracking()
            .Where(volume => volume.WorkId == workId &&
                             volume.Kind == NovelVolumeKinds.Epub &&
                             volume.SourceStoragePath != null)
            .OrderBy(volume => volume.Number)
            .ThenBy(volume => volume.SourceStoragePath)
            .Select(volume => volume.SourceStoragePath!)
            .ToListAsync(cancellationToken);

        return storagePaths
            .Select(path => LibrarySeriesFolder.Resolve(settings, MediaAcquisitionKind.LightNovel, path))
            .FirstOrDefault(folder => folder is not null);
    }
}
