using Jularr.Web.Data;
using Jularr.Web.Features.Library;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Storage;

// What the owner should look at for one root. Built from the database only (the last
// reconciliation and media analysis), so reading it never touches or wakes the storage.
// Missing and moved files are handled by the reconciliation itself: missing media is removed
// only while the root is readable, confident moves are relinked, uncertain ones are logged
// on the scan as "Needs attention".
public sealed record StorageIntegritySummary(
    int DuplicateEpisodes,
    int EmptyFiles,
    int UnreadableFiles)
{
    public static readonly StorageIntegritySummary Clean = new(0, 0, 0);

    public bool NeedsAttention =>
        DuplicateEpisodes > 0 || EmptyFiles > 0 || UnreadableFiles > 0;
}

public sealed class StorageIntegrityService(AppDbContext db)
{
    public async Task<IReadOnlyDictionary<Guid, StorageIntegritySummary>> SummarizeAsync(
        CancellationToken cancellationToken)
    {
        var duplicates = await db.MediaFiles
            .AsNoTracking()
            .GroupBy(x => new { x.LibraryRootId, x.EpisodeId })
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.LibraryRootId)
            .ToListAsync(cancellationToken);

        var empty = await db.MediaFiles
            .AsNoTracking()
            .Where(x => x.SizeBytes == 0)
            .GroupBy(x => x.LibraryRootId)
            .Select(group => new { RootId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        // ffprobe rejected the file: the existing media probing is the readability check.
        var unreadable = await db.MediaFiles
            .AsNoTracking()
            .Join(
                db.MediaAnalyses.Where(x => x.Status == MediaAnalysisStatus.Failed),
                media => media.Id,
                analysis => analysis.MediaFileId,
                (media, _) => media.LibraryRootId)
            .GroupBy(rootId => rootId)
            .Select(group => new { RootId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return duplicates
            .Concat(empty.Select(x => x.RootId))
            .Concat(unreadable.Select(x => x.RootId))
            .Distinct()
            .ToDictionary(
                rootId => rootId,
                rootId => new StorageIntegritySummary(
                    duplicates.Count(x => x == rootId),
                    empty.FirstOrDefault(x => x.RootId == rootId)?.Count ?? 0,
                    unreadable.FirstOrDefault(x => x.RootId == rootId)?.Count ?? 0));
    }
}
