using Jularr.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Audiobooks;

/// <summary>
/// Canonical per-profile listening state of one audiobook (#440): the resume position
/// (<see cref="PositionMs"/>), the chapter the listener is on (<see cref="ChapterNumber"/>) and the
/// finished flag (<see cref="IsCompleted"/>). One row per profile and audiobook, mirroring the episode
/// and novel progress patterns.
/// </summary>
public sealed class AudiobookProgress
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProfileId { get; set; } = "";
    public Guid AudiobookId { get; set; }

    /// <summary>Resume position within the audiobook (or within the current file) in milliseconds.</summary>
    public long PositionMs { get; set; }

    public long? DurationMs { get; set; }

    /// <summary>1-based chapter/part the listener is on; 0 when the audiobook has no chapter structure.</summary>
    public int ChapterNumber { get; set; }

    public bool IsCompleted { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed record AudiobookProgressSnapshot(
    Guid AudiobookId,
    long PositionMs,
    long? DurationMs,
    int ChapterNumber,
    bool IsCompleted,
    DateTime? UpdatedAt)
{
    public int Percent =>
        IsCompleted
            ? 100
            : DurationMs is > 0
                ? Math.Clamp((int)Math.Round(PositionMs * 100d / DurationMs.Value), 0, 100)
                : 0;
}

/// <summary>
/// Canonical per-profile audiobook listening progress. Reuses the established progress pattern
/// (<c>NovelProgressService</c> takes the profile explicitly; completion is sticky like
/// <c>EpisodeProgressService</c>): once an audiobook is finished a later partial checkpoint updates the
/// resume position but never flips it back to unfinished — only <see cref="SetCompletedAsync"/> can.
/// </summary>
public sealed class AudiobookProgressService(AppDbContext db)
{
    /// <summary>Listening at or beyond this share of the duration marks the audiobook finished.</summary>
    public const double CompletionThreshold = 0.95;

    public async Task<AudiobookProgressSnapshot?> GetAsync(
        string profileId,
        Guid audiobookId,
        CancellationToken cancellationToken)
    {
        var progress = await db.Set<AudiobookProgress>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProfileId == profileId && x.AudiobookId == audiobookId, cancellationToken);
        return progress is null ? null : ToSnapshot(progress);
    }

    /// <summary>
    /// Stores a listening checkpoint. Reaching the completion threshold marks the audiobook finished and
    /// clears the resume position; watched/finished state is sticky.
    /// </summary>
    public async Task<AudiobookProgressSnapshot> SaveAsync(
        string profileId,
        Guid audiobookId,
        long positionMs,
        long? durationMs,
        int chapterNumber,
        bool completed,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        var normalizedDuration = durationMs is > 0 ? durationMs : null;
        var position = Math.Max(0, positionMs);
        if (normalizedDuration is { } duration)
        {
            position = Math.Min(position, duration);
        }

        var progress = await db.Set<AudiobookProgress>()
            .SingleOrDefaultAsync(x => x.ProfileId == profileId && x.AudiobookId == audiobookId, cancellationToken);

        var finalDuration = normalizedDuration ?? progress?.DurationMs;
        var reachedEnd = completed ||
            (finalDuration is { } known && position >= known * CompletionThreshold);

        if (progress is null)
        {
            progress = new AudiobookProgress
            {
                ProfileId = profileId,
                AudiobookId = audiobookId
            };
            db.Add(progress);
        }

        progress.DurationMs = finalDuration;
        progress.ChapterNumber = Math.Max(0, chapterNumber);
        progress.PositionMs = reachedEnd ? 0 : position;
        progress.IsCompleted = progress.IsCompleted || reachedEnd;
        progress.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return ToSnapshot(progress);
    }

    /// <summary>Explicit mark finished / unfinished. Both clear the resume position.</summary>
    public async Task<AudiobookProgressSnapshot> SetCompletedAsync(
        string profileId,
        Guid audiobookId,
        bool completed,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        var progress = await db.Set<AudiobookProgress>()
            .SingleOrDefaultAsync(x => x.ProfileId == profileId && x.AudiobookId == audiobookId, cancellationToken);
        if (progress is null)
        {
            progress = new AudiobookProgress
            {
                ProfileId = profileId,
                AudiobookId = audiobookId
            };
            db.Add(progress);
        }

        progress.IsCompleted = completed;
        progress.PositionMs = 0;
        progress.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return ToSnapshot(progress);
    }

    private static AudiobookProgressSnapshot ToSnapshot(AudiobookProgress progress) =>
        new(
            progress.AudiobookId,
            progress.PositionMs,
            progress.DurationMs,
            progress.ChapterNumber,
            progress.IsCompleted,
            progress.UpdatedAt);
}
