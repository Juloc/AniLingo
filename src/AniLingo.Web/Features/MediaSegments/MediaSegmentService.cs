using AniLingo.Web.Data;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Playback;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AniLingo.Web.Features.MediaSegments;

public enum SegmentDetectionOutcome
{
    NoMedia,
    NotAnalyzed,
    DetectorDisabled,
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

    public bool DetectorEnabled => detector is not NoOpMediaSegmentDetector;

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
    // version match the stored detector markers, unless a rebuild is forced.
    public async Task<SegmentDetectionRun> RunDetectorAsync(
        Guid episodeId,
        bool force,
        CancellationToken cancellationToken)
    {
        if (!DetectorEnabled)
        {
            return new SegmentDetectionRun(SegmentDetectionOutcome.DetectorDisabled, 0);
        }

        var media = await GetMediaSourceAsync(episodeId, cancellationToken);
        if (media is null)
        {
            return new SegmentDetectionRun(SegmentDetectionOutcome.NoMedia, 0);
        }

        if (media.Identity is null)
        {
            return new SegmentDetectionRun(SegmentDetectionOutcome.NotAnalyzed, 0);
        }

        var existing = await db.EpisodeMediaSegments
            .Where(x => x.EpisodeId == episodeId && x.Source == MediaSegmentSource.Detector)
            .ToListAsync(cancellationToken);

        if (!force &&
            existing.Count > 0 &&
            existing.All(x =>
                string.Equals(x.Method, detector.Method, StringComparison.Ordinal) &&
                string.Equals(x.Version, detector.Version, StringComparison.Ordinal) &&
                string.Equals(x.MediaIdentity, media.Identity, StringComparison.Ordinal)))
        {
            return new SegmentDetectionRun(SegmentDetectionOutcome.Skipped, existing.Count);
        }

        var detected = await detector.DetectAsync(
            new MediaSegmentDetectionRequest(
                episodeId,
                media.AnimeId,
                media.Path,
                media.Identity,
                media.DurationSeconds),
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
                MediaIdentity = media.Identity,
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
        var request = await BuildTrickplayRequestAsync(episodeId, cancellationToken);
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
            trickplay.Describe(request.MediaFileId, request.MediaIdentity));
    }

    public async Task<TrickplayDescriptor> GetTrickplayAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var media = await GetMediaSourceAsync(episodeId, cancellationToken);
        return media?.Identity is { } identity
            ? trickplay.Describe(media.MediaFileId, identity)
            : TrickplayDescriptor.Unavailable;
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

        var media = await GetMediaSourceAsync(episodeId, cancellationToken);
        return media?.Identity is { } identity
            ? trickplay.GetAsset(media.MediaFileId, identity, fileName)
            : null;
    }

    // Owner action: drop the current cache generation and queue a fresh one.
    public async Task<bool> RegenerateTrickplayAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var request = await BuildTrickplayRequestAsync(episodeId, cancellationToken);
        return request is not null &&
               await trickplay.RegenerateAsync(request, cancellationToken);
    }

    // Duration and identity come from the canonical media inventory; this path never probes.
    // Without a successful analysis there is no trickplay rather than a guessed timeline.
    private async Task<TrickplayRequest?> BuildTrickplayRequestAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var media = await GetMediaSourceAsync(episodeId, cancellationToken);
        return media is { Identity: { } identity, DurationSeconds: > 0 and var duration }
            ? new TrickplayRequest(
                episodeId,
                media.MediaFileId,
                identity,
                media.Path,
                duration,
                Path.GetFileName(media.Path))
            : null;
    }

    private async Task<MediaSource?> GetMediaSourceAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var media = await (
                from file in db.MediaFiles.AsNoTracking()
                join episode in db.Episodes.AsNoTracking() on file.EpisodeId equals episode.Id
                where file.EpisodeId == episodeId
                orderby file.Path
                select new { file.Id, episode.AnimeId, file.Path })
            .FirstOrDefaultAsync(cancellationToken);

        if (media is null)
        {
            return null;
        }

        var analysis = await db.MediaAnalyses
            .AsNoTracking()
            .Where(x => x.MediaFileId == media.Id && x.Status == MediaAnalysisStatus.Succeeded)
            .Select(x => new
            {
                x.SourceFingerprint,
                x.SourceSizeBytes,
                x.SourceLastWriteTimeUtc,
                x.DurationSeconds
            })
            .SingleOrDefaultAsync(cancellationToken);

        return new MediaSource(
            media.Id,
            media.AnimeId,
            media.Path,
            analysis is null
                ? null
                : MediaIdentity.Compute(
                    media.Id,
                    analysis.SourceFingerprint,
                    analysis.SourceSizeBytes,
                    analysis.SourceLastWriteTimeUtc),
            analysis?.DurationSeconds is > 0 and var seconds && double.IsFinite(seconds)
                ? seconds
                : null);
    }

    private sealed record MediaSource(
        Guid MediaFileId,
        Guid AnimeId,
        string Path,
        string? Identity,
        double? DurationSeconds);
}
