using AniLingo.Web.Data;
using AniLingo.Web.Features.Playback;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AniLingo.Web.Features.MediaSegments;

public enum SegmentDetectionOutcome
{
    NoMedia,
    Skipped,
    Completed
}

public sealed record SegmentDetectionRun(
    SegmentDetectionOutcome Outcome,
    int SegmentCount);

// Canonical owner of episode segment markers and generated navigation assets.
// Web, native and TV clients consume the same resolved data.
public sealed class MediaSegmentService(
    AppDbContext db,
    IOptions<MediaSegmentOptions> options,
    IMediaSegmentDetector detector,
    TrickplayGenerator trickplay)
{
    public double SkipConfidenceThreshold =>
        MediaSegmentPolicy.ClampConfidence(options.Value.SkipConfidenceThreshold);

    public async Task<IReadOnlyList<EpisodeMediaSegment>> ListAsync(
        Guid episodeId,
        CancellationToken cancellationToken) =>
        await db.EpisodeMediaSegments
            .AsNoTracking()
            .Where(x => x.EpisodeId == episodeId)
            .OrderBy(x => x.Kind)
            .ThenBy(x => x.Source)
            .ToListAsync(cancellationToken);

    public async Task<EpisodeSegmentDescriptor> GetSegmentsAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var rows = await ListAsync(episodeId, cancellationToken);
        return new EpisodeSegmentDescriptor(
            SkipConfidenceThreshold,
            MediaSegmentPolicy.Resolve(rows, SkipConfidenceThreshold));
    }

    // Manual markers are corrections: confidence 1 and highest precedence.
    public async Task<EpisodeMediaSegment?> SaveManualAsync(
        Guid episodeId,
        MediaSegmentKind kind,
        long startMs,
        long endMs,
        CancellationToken cancellationToken)
    {
        if (!MediaSegmentPolicy.IsValidRange(startMs, endMs))
        {
            throw new ArgumentException(
                $"A segment must end at least {MediaSegmentPolicy.MinimumLengthMs / 1000} second after it starts.");
        }

        if (!await db.Episodes.AnyAsync(x => x.Id == episodeId, cancellationToken))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var row = await db.EpisodeMediaSegments.SingleOrDefaultAsync(
            x => x.EpisodeId == episodeId &&
                 x.Kind == kind &&
                 x.Source == MediaSegmentSource.Manual,
            cancellationToken);

        if (row is null)
        {
            row = new EpisodeMediaSegment
            {
                EpisodeId = episodeId,
                Kind = kind,
                Source = MediaSegmentSource.Manual,
                Method = MediaSegmentPolicy.ManualMethod,
                Version = "1",
                Confidence = 1,
                CreatedAt = now
            };
            db.EpisodeMediaSegments.Add(row);
        }

        row.StartMs = startMs;
        row.EndMs = endMs;
        row.Confidence = 1;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return row;
    }

    public async Task<bool> RemoveManualAsync(
        Guid episodeId,
        MediaSegmentKind kind,
        CancellationToken cancellationToken)
    {
        var removed = await db.EpisodeMediaSegments
            .Where(x =>
                x.EpisodeId == episodeId &&
                x.Kind == kind &&
                x.Source == MediaSegmentSource.Manual)
            .ExecuteDeleteAsync(cancellationToken);
        return removed > 0;
    }

    // Runs the configured detector and stores its result with method/version/confidence.
    // Skips the (potentially expensive) analysis while media identity and detector
    // version match the stored detector markers.
    public async Task<SegmentDetectionRun> RunDetectorAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var media = await GetMediaRowAsync(episodeId, cancellationToken);
        if (media is null)
        {
            return new SegmentDetectionRun(SegmentDetectionOutcome.NoMedia, 0);
        }

        var identity = MediaIdentity.Compute(media.Id, media.SizeBytes, media.LastWriteTimeUtc);
        var existing = await db.EpisodeMediaSegments
            .Where(x => x.EpisodeId == episodeId && x.Source == MediaSegmentSource.Detector)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0 &&
            existing.All(x =>
                string.Equals(x.Method, detector.Method, StringComparison.Ordinal) &&
                string.Equals(x.Version, detector.Version, StringComparison.Ordinal) &&
                string.Equals(x.MediaIdentity, identity, StringComparison.Ordinal)))
        {
            return new SegmentDetectionRun(SegmentDetectionOutcome.Skipped, existing.Count);
        }

        var detected = await detector.DetectAsync(
            new MediaSegmentDetectionRequest(
                episodeId,
                media.AnimeId,
                media.Path,
                identity,
                null),
            cancellationToken);

        var now = DateTime.UtcNow;
        var results = detected
            .Where(x => MediaSegmentPolicy.IsValidRange(x.StartMs, x.EndMs))
            .GroupBy(x => x.Kind)
            .Select(group => group.OrderByDescending(x => x.Confidence).First())
            .ToArray();

        db.EpisodeMediaSegments.RemoveRange(existing);
        foreach (var result in results)
        {
            db.EpisodeMediaSegments.Add(new EpisodeMediaSegment
            {
                EpisodeId = episodeId,
                Kind = result.Kind,
                StartMs = result.StartMs,
                EndMs = result.EndMs,
                Source = MediaSegmentSource.Detector,
                Method = detector.Method,
                Version = detector.Version,
                Confidence = MediaSegmentPolicy.ClampConfidence(result.Confidence),
                MediaIdentity = identity,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return new SegmentDetectionRun(SegmentDetectionOutcome.Completed, results.Length);
    }

    // Player descriptors for one episode. Queues trickplay generation when the
    // media is playable and no cache exists; the response never waits for it.
    public async Task<EpisodePlayerNavigation> GetPlayerNavigationAsync(
        Guid episodeId,
        PlaybackMedia? media,
        CancellationToken cancellationToken)
    {
        var segments = await GetSegmentsAsync(episodeId, cancellationToken);
        var request = await BuildTrickplayRequestAsync(episodeId, media, cancellationToken);
        if (request is null)
        {
            return new EpisodePlayerNavigation(segments, TrickplayDescriptor.Unavailable);
        }

        if (media is { HasReadyOption: true } &&
            media.Storage is null or { IsAvailable: true })
        {
            await trickplay.EnsureQueuedAsync(request, cancellationToken);
        }

        return new EpisodePlayerNavigation(
            segments,
            trickplay.Describe(request.MediaIdentity));
    }

    public async Task<TrickplayDescriptor> GetTrickplayAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var media = await GetMediaRowAsync(episodeId, cancellationToken);
        return media is null
            ? TrickplayDescriptor.Unavailable
            : trickplay.Describe(MediaIdentity.Compute(media.Id, media.SizeBytes, media.LastWriteTimeUtc));
    }

    public async Task<TrickplayAsset?> GetTrickplayAssetAsync(
        Guid episodeId,
        string fileName,
        CancellationToken cancellationToken)
    {
        if (!TrickplayGenerator.IsAllowedAssetName(fileName))
        {
            return null;
        }

        var media = await GetMediaRowAsync(episodeId, cancellationToken);
        return media is null
            ? null
            : trickplay.GetAsset(
                MediaIdentity.Compute(media.Id, media.SizeBytes, media.LastWriteTimeUtc),
                fileName);
    }

    public async Task<bool> RegenerateTrickplayAsync(
        Guid episodeId,
        PlaybackMedia media,
        CancellationToken cancellationToken)
    {
        var request = await BuildTrickplayRequestAsync(episodeId, media, cancellationToken);
        return request is not null &&
               await trickplay.RegenerateAsync(request, cancellationToken);
    }

    private async Task<TrickplayRequest?> BuildTrickplayRequestAsync(
        Guid episodeId,
        PlaybackMedia? media,
        CancellationToken cancellationToken)
    {
        var row = await GetMediaRowAsync(episodeId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var duration = media?.DurationSeconds ?? 0;
        return new TrickplayRequest(
            episodeId,
            MediaIdentity.Compute(row.Id, row.SizeBytes, row.LastWriteTimeUtc),
            row.Path,
            duration,
            Path.GetFileName(row.Path));
    }

    private async Task<MediaRow?> GetMediaRowAsync(
        Guid episodeId,
        CancellationToken cancellationToken) =>
        await (
            from media in db.MediaFiles.AsNoTracking()
            join episode in db.Episodes.AsNoTracking() on media.EpisodeId equals episode.Id
            where media.EpisodeId == episodeId
            orderby media.Path
            select new MediaRow(
                media.Id,
                episode.AnimeId,
                media.Path,
                media.SizeBytes,
                media.LastWriteTimeUtc))
            .FirstOrDefaultAsync(cancellationToken);

    private sealed record MediaRow(
        Guid Id,
        Guid AnimeId,
        string Path,
        long SizeBytes,
        DateTime LastWriteTimeUtc);
}
