using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Progress;

public sealed class EpisodeProgress
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProfileId { get; set; } = "";
    public Guid EpisodeId { get; set; }
    public long PositionMs { get; set; }
    public long? DurationMs { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed record EpisodeProgressSnapshot(
    Guid EpisodeId,
    long PositionMs,
    long? DurationMs,
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

public sealed record EpisodeProgressUpdate(
    long PositionMs,
    long? DurationMs,
    bool Completed);

public sealed class EpisodeProgressService(
    AppDbContext db,
    CurrentAccountContext currentAccount)
{
    private const double CompletionThreshold = 0.95;

    public async Task<EpisodeProgressSnapshot?> GetAsync(
        Guid episodeId,
        CancellationToken cancellationToken = default)
    {
        var row = await (
            from episode in db.Episodes.AsNoTracking()
            join progressValue in db.EpisodeProgress
                    .AsNoTracking()
                    .Where(x => x.ProfileId == currentAccount.ProfileId)
                on episode.Id equals progressValue.EpisodeId into progressRows
            from progress in progressRows.DefaultIfEmpty()
            where episode.Id == episodeId
            select new
            {
                episode.Id,
                PositionMs = progress == null ? 0L : progress.PositionMs,
                DurationMs = progress == null ? null : progress.DurationMs,
                IsCompleted = progress != null && progress.IsCompleted,
                UpdatedAt = progress == null ? (DateTime?)null : progress.UpdatedAt
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new EpisodeProgressSnapshot(
                row.Id,
                row.PositionMs,
                row.DurationMs,
                row.IsCompleted,
                row.UpdatedAt);
    }

    public async Task<EpisodeProgressSnapshot?> UpdateAsync(
        Guid episodeId,
        EpisodeProgressUpdate update,
        CancellationToken cancellationToken = default)
    {
        var episodeExists = await db.Episodes
            .AsNoTracking()
            .AnyAsync(x => x.Id == episodeId, cancellationToken);
        if (!episodeExists)
        {
            return null;
        }

        var positionMs = Math.Max(0, update.PositionMs);
        long? durationMs = update.DurationMs is > 0
            ? update.DurationMs
            : null;

        if (durationMs is { } duration)
        {
            positionMs = Math.Min(positionMs, duration);
        }

        var completed = update.Completed ||
            (durationMs is { } knownDuration &&
             knownDuration > 0 &&
             positionMs >= knownDuration * CompletionThreshold);

        var progress = await db.EpisodeProgress
            .SingleOrDefaultAsync(
                x => x.ProfileId == currentAccount.ProfileId &&
                     x.EpisodeId == episodeId,
                cancellationToken);

        if (progress is null)
        {
            progress = new EpisodeProgress
            {
                ProfileId = currentAccount.ProfileId,
                EpisodeId = episodeId
            };
            db.EpisodeProgress.Add(progress);
        }

        var finalDurationMs = durationMs ?? progress.DurationMs;
        var finalCompleted = progress.IsCompleted || completed;

        progress.PositionMs = finalCompleted && finalDurationMs is { } completedDuration
            ? completedDuration
            : positionMs;
        progress.DurationMs = finalDurationMs;
        progress.IsCompleted = finalCompleted;
        progress.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return new EpisodeProgressSnapshot(
            progress.EpisodeId,
            progress.PositionMs,
            progress.DurationMs,
            progress.IsCompleted,
            progress.UpdatedAt);
    }
}
