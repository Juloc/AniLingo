using AniLingo.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Acquisition.History;

/// <summary>Persists and reads per-episode acquisition history (P1 item 6).</summary>
public sealed class AcquisitionHistoryService(AppDbContext db)
{
    public const int MaxEntriesPerAnime = 500;

    public async Task RecordAsync(AcquisitionHistoryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        db.AcquisitionHistory.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AcquisitionHistoryEntry>> ForAnimeAsync(
        Guid animeId,
        int limit,
        CancellationToken cancellationToken) =>
        await db.AcquisitionHistory
            .AsNoTracking()
            .Where(entry => entry.AnimeId == animeId)
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Take(Math.Clamp(limit, 1, MaxEntriesPerAnime))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AcquisitionHistoryEntry>> RecentAsync(
        int limit,
        CancellationToken cancellationToken) =>
        await db.AcquisitionHistory
            .AsNoTracking()
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);
}
