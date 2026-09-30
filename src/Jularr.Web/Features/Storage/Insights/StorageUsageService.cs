using Jularr.Web.Data;
using Jularr.Web.Features.Library;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Storage.Insights;

// Storage usage from what the library already knows. Sizes come from the media inventory the
// reconciliation keeps (MediaFile.SizeBytes), so no filesystem scan is needed and a sleeping NAS
// is never touched: a root's state is read from the cached availability, and a root that could be
// a sleeping Wake-on-LAN NAS whose state was never observed is simply reported as not checked.
// Only a root without Wake-on-LAN, which nothing here could wake, is observed once when unknown.
public sealed class StorageUsageService(
    AppDbContext db,
    LibraryRootAvailabilityService availability,
    StorageIntegrityService integrity)
{
    public const int DefaultLargestItems = 15;

    public async Task<StorageUsageReport> GetAsync(
        int largestItems,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(largestItems, 1, 100);

        var roots = await db.LibraryRoots
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var perRoot = (await db.MediaFiles
                .AsNoTracking()
                .GroupBy(x => x.LibraryRootId)
                .Select(group => new
                {
                    RootId = group.Key,
                    Files = group.Count(),
                    Bytes = group.Sum(x => x.SizeBytes)
                })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.RootId);

        var integritySummaries = await integrity.SummarizeAsync(cancellationToken);

        var rootUsage = new List<StorageRootUsage>(roots.Count);
        var healthByRoot = new Dictionary<Guid, StorageHealthState?>();
        foreach (var root in roots)
        {
            var snapshot = await ObserveAsync(root, cancellationToken);
            var health = snapshot?.Health;
            healthByRoot[root.Id] = health;
            perRoot.TryGetValue(root.Id, out var totals);
            rootUsage.Add(new StorageRootUsage(
                root.Id,
                root.Name,
                root.Path,
                health,
                totals?.Files ?? 0,
                totals?.Bytes ?? 0,
                snapshot is { IsAvailable: true } ? snapshot.FreeSpaceBytes : null,
                integritySummaries.TryGetValue(root.Id, out var summary) ? summary.DuplicateEpisodes : 0,
                root.LastScannedAt));
        }

        var mediaTypes = new List<StorageMediaTypeUsage>
        {
            new(
                StorageMediaKind.Episodes,
                rootUsage.Sum(x => x.FileCount),
                rootUsage.Sum(x => x.Bytes)),
            new(
                StorageMediaKind.Audiobooks,
                await db.AudiobookFiles.AsNoTracking().LongCountAsync(cancellationToken),
                await db.AudiobookFiles.AsNoTracking().SumAsync(x => (long?)x.SizeBytes, cancellationToken) ?? 0),
            new(
                StorageMediaKind.Books,
                await db.BookFiles.AsNoTracking().LongCountAsync(cancellationToken),
                await db.BookFiles.AsNoTracking().SumAsync(x => (long?)x.SizeBytes, cancellationToken) ?? 0)
        };

        var rootNames = roots.ToDictionary(x => x.Id, x => x.Name);
        var largest = await (
                from file in db.MediaFiles.AsNoTracking()
                join episode in db.Episodes.AsNoTracking() on file.EpisodeId equals episode.Id
                join anime in db.Anime.AsNoTracking() on episode.AnimeId equals anime.Id
                orderby file.SizeBytes descending, file.Id
                select new
                {
                    file.Id,
                    anime.Title,
                    episode.SeasonNumber,
                    EpisodeNumber = episode.Number,
                    file.LibraryRootId,
                    file.SizeBytes
                })
            .Take(limit)
            .ToListAsync(cancellationToken);

        return new StorageUsageReport(
            rootUsage,
            mediaTypes,
            [
                .. largest.Select(x => new StorageLargestItem(
                    x.Id,
                    x.Title,
                    x.SeasonNumber,
                    x.EpisodeNumber,
                    x.LibraryRootId,
                    rootNames.GetValueOrDefault(x.LibraryRootId, ""),
                    x.SizeBytes,
                    healthByRoot.GetValueOrDefault(x.LibraryRootId)))
            ]);
    }

    private async Task<LibraryRootAvailabilitySnapshot?> ObserveAsync(
        LibraryRoot root,
        CancellationToken cancellationToken)
    {
        var cached = availability.GetCached(root);
        if (cached is not null)
        {
            return cached;
        }

        // Nothing observed yet. A Wake-on-LAN root may be asleep on purpose; leave it alone.
        var wakeConfigured =
            root.WakeOnLanEnabled &&
            WakeOnLanService.TryNormalizeMacAddress(root.WakeMacAddress, out _);
        return wakeConfigured
            ? null
            : await availability.CheckAsync(root.Id, force: false, cancellationToken);
    }
}
