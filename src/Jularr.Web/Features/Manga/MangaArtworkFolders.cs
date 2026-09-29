using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Import;
using Jularr.Web.Features.Artwork;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Manga;

/// <summary>
/// Where a Manga series keeps its cover beside its media (issue #581): its series folder on the
/// configured Manga NAS library root, derived from the series' canonical source path
/// (<c>MangaSeries.SourcePath</c>, the series folder Jularr places completed downloads in or the
/// file it was imported from). A series read in place, outside the library root (no root
/// configured, or imported from somewhere else), has no beside-media folder and keeps its provider
/// cover URL.
/// </summary>
public static class MangaArtworkFolders
{
    public static async Task<string?> ResolveAsync(
        AppDbContext db,
        AnimeImportSettingsState settings,
        Guid seriesId,
        CancellationToken cancellationToken)
    {
        if (settings.LibraryFor(MediaAcquisitionKind.Manga) is null)
        {
            return null;
        }

        var id = seriesId.ToString();
        var sourcePath = await db.Database
            .SqlQuery<string>($"""SELECT "SourcePath" AS "Value" FROM "MangaSeries" WHERE "Id" = {id}""")
            .FirstOrDefaultAsync(cancellationToken);
        if (sourcePath is null)
        {
            return null;
        }

        // A series whose only chapter is a folder of loose page images reads every image directly
        // in that folder as a page (MangaImportService), so a cover.* written there would become
        // page zero. Such a series keeps its provider cover.
        var folderIsChapter = await db.Database
            .SqlQuery<int>($"""
                SELECT COUNT(*)::int AS "Value" FROM "MangaChapters"
                WHERE "SeriesId" = {id} AND "SourceKind" = 'directory' AND "SourcePath" = {sourcePath}
                """)
            .FirstAsync(cancellationToken) > 0;
        return folderIsChapter
            ? null
            : LibrarySeriesFolder.Resolve(settings, MediaAcquisitionKind.Manga, sourcePath);
    }
}
