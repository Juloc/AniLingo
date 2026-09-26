using System.Globalization;
using AniLingo.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Tracking;

/// <summary>
/// Newest canonical local progress checkpoint of one work for one profile.
/// <see cref="CompletionMarker"/> identifies the furthest completed unit
/// (episode, chapter) using the same completion rules as the canonical
/// progress resolver; it changes only when a unit is finished.
/// </summary>
public sealed record AniListSyncCheckpoint(
    string MediaKind,
    Guid LocalId,
    string Title,
    DateTime UpdatedAt,
    string? CompletionMarker);

/// <summary>
/// Read-only view of the canonical local progress tables (EpisodeProgress,
/// MangaProgress, NovelProgress) for automatic AniList sync. It never writes
/// progress and never decides AniList values; those always come from
/// <see cref="AniListAccountService"/>.
/// </summary>
public static class AniListSyncCheckpoints
{
    public const string Anime = "anime";
    public const string Manga = "manga";
    public const string Novel = "novel";

    // Mirrors the completedThreshold the canonical novel resolver uses in
    // AniListAccountService (a chapter counts as read from 95% on).
    private const int NovelChapterCompletedPermille = 950;

    /// <summary>Checkpoints of the profile changed after <paramref name="sinceUtc"/>.</summary>
    public static async Task<IReadOnlyList<AniListSyncCheckpoint>> LoadChangedAsync(
        AppDbContext db,
        string profileId,
        DateTime sinceUtc,
        CancellationToken cancellationToken)
    {
        var checkpoints = new List<AniListSyncCheckpoint>();
        checkpoints.AddRange(await LoadAnimeAsync(db, profileId, sinceUtc, cancellationToken));
        checkpoints.AddRange(await LoadMangaAsync(db, profileId, sinceUtc, cancellationToken));
        checkpoints.AddRange(await LoadNovelsAsync(db, profileId, sinceUtc, cancellationToken));
        return checkpoints;
    }

    private static async Task<IEnumerable<AniListSyncCheckpoint>> LoadAnimeAsync(
        AppDbContext db,
        string profileId,
        DateTime sinceUtc,
        CancellationToken cancellationToken)
    {
        // Specials never drive the canonical anime position, so only regular
        // episodes are considered.
        var rows = await (
            from progress in db.EpisodeProgress.AsNoTracking()
            join episode in db.Episodes.AsNoTracking()
                on progress.EpisodeId equals episode.Id
            join anime in db.Anime.AsNoTracking()
                on episode.AnimeId equals anime.Id
            where progress.ProfileId == profileId &&
                  episode.SeasonNumber > 0 &&
                  episode.Number > 0
            select new
            {
                episode.AnimeId,
                anime.Title,
                progress.UpdatedAt,
                progress.IsCompleted,
                episode.SeasonNumber,
                episode.Number
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.AnimeId)
            .Select(group =>
            {
                var latestCompleted = group
                    .Where(x => x.IsCompleted)
                    .OrderByDescending(x => x.SeasonNumber)
                    .ThenByDescending(x => x.Number)
                    .FirstOrDefault();
                return new AniListSyncCheckpoint(
                    Anime,
                    group.Key,
                    group.First().Title,
                    AsUtc(group.Max(x => x.UpdatedAt)),
                    latestCompleted is null
                        ? null
                        : $"S{latestCompleted.SeasonNumber}E{latestCompleted.Number}");
            })
            .Where(x => x.UpdatedAt > sinceUtc);
    }

    private static async Task<IEnumerable<AniListSyncCheckpoint>> LoadMangaAsync(
        AppDbContext db,
        string profileId,
        DateTime sinceUtc,
        CancellationToken cancellationToken)
    {
        // Manga progress is owned by MangaRepository (raw SQL tables). One row
        // per profile and series holds the current reader position.
        var rows = await db.Database
            .SqlQuery<MangaCheckpointRow>(
                $"""
                SELECT
                    p."SeriesId" AS "SeriesId",
                    COALESCE(s."MetadataTitle", s."Title") AS "Title",
                    c."Number" AS "ChapterNumber",
                    p."PageIndex" AS "PageIndex",
                    c."PageCount" AS "PageCount",
                    p."UpdatedAt" AS "UpdatedAt"
                FROM "MangaProgress" p
                JOIN "MangaSeries" s ON s."Id" = p."SeriesId"
                JOIN "MangaChapters" c ON c."Id" = p."ChapterId" AND c."SeriesId" = p."SeriesId"
                WHERE p."ProfileId" = {profileId}
                """)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row =>
            {
                var completedChapter = row.PageIndex >= Math.Max(0, row.PageCount - 1)
                    ? row.ChapterNumber
                    : row.ChapterNumber - 1;
                return new AniListSyncCheckpoint(
                    Manga,
                    Guid.Parse(row.SeriesId),
                    row.Title,
                    DateTime.Parse(
                        row.UpdatedAt,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                    completedChapter > 0
                        ? completedChapter.ToString("0.###", CultureInfo.InvariantCulture)
                        : null);
            })
            .Where(x => x.UpdatedAt > sinceUtc);
    }

    private static async Task<IEnumerable<AniListSyncCheckpoint>> LoadNovelsAsync(
        AppDbContext db,
        string profileId,
        DateTime sinceUtc,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from progress in db.NovelProgress.AsNoTracking()
            join chapter in db.NovelChapters.AsNoTracking()
                on progress.ChapterId equals chapter.Id
            join work in db.NovelWorks.AsNoTracking()
                on progress.WorkId equals work.Id
            where progress.ProfileId == profileId &&
                  chapter.WorkId == progress.WorkId &&
                  progress.UpdatedAt > sinceUtc
            select new
            {
                progress.WorkId,
                Title = work.MetadataTitle ?? work.Title,
                chapter.Number,
                progress.PositionPermille,
                progress.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row =>
        {
            var completedChapter = row.PositionPermille >= NovelChapterCompletedPermille
                ? row.Number
                : row.Number - 1;
            return new AniListSyncCheckpoint(
                Novel,
                row.WorkId,
                row.Title,
                AsUtc(row.UpdatedAt),
                completedChapter > 0
                    ? completedChapter.ToString(CultureInfo.InvariantCulture)
                    : null);
        });
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed class MangaCheckpointRow
    {
        public string SeriesId { get; set; } = "";
        public string Title { get; set; } = "";
        public double ChapterNumber { get; set; }
        public int PageIndex { get; set; }
        public int PageCount { get; set; }
        public string UpdatedAt { get; set; } = "";
    }
}
