using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Admin;

public sealed record AdminEpisodeProgressSummary(
    Guid EpisodeId,
    string AnimeTitle,
    int SeasonNumber,
    int EpisodeNumber,
    string EpisodeTitle,
    long PositionMs,
    long? DurationMs,
    bool IsCompleted,
    DateTime UpdatedAt)
{
    public int Percent =>
        IsCompleted
            ? 100
            : DurationMs is > 0
                ? Math.Clamp((int)Math.Round(PositionMs * 100d / DurationMs.Value), 0, 100)
                : 0;
}

public sealed record AdminNovelProgressSummary(
    Guid WorkId,
    string WorkTitle,
    int ChapterNumber,
    string ChapterTitle,
    int PositionPermille,
    DateTime UpdatedAt)
{
    public int Percent =>
        Math.Clamp((int)Math.Round(PositionPermille / 10d), 0, 100);
}

public sealed record AdminLearningProgressSummary(
    int KnownTerms,
    int LearningTerms,
    int Reviews,
    DateTime? LastReviewAt);

public sealed record AdminUserProgressSummary(
    LocalAccountSummary Account,
    AdminEpisodeProgressSummary? CurrentAnime,
    int EpisodesStarted,
    int EpisodesCompleted,
    AdminNovelProgressSummary? CurrentNovel,
    int NovelsStarted,
    AdminLearningProgressSummary Learning,
    DateTime? LastActivityAt);

public sealed class AdminUserProgressService(AppDbContext db)
{
    public async Task<IReadOnlyList<AdminUserProgressSummary>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var accounts = await db.OwnerAccounts
            .AsNoTracking()
            .OrderBy(x => x.Role)
            .ThenBy(x => x.UserName)
            .Select(x => new LocalAccountSummary(
                x.Id,
                x.UserName,
                x.Role,
                x.IsEnabled,
                x.CreatedAt))
            .ToListAsync(cancellationToken);

        var episodeRows = await (
            from progress in db.EpisodeProgress.AsNoTracking()
            join episode in db.Episodes.AsNoTracking()
                on progress.EpisodeId equals episode.Id
            join anime in db.Anime.AsNoTracking()
                on episode.AnimeId equals anime.Id
            select new
            {
                progress.ProfileId,
                Summary = new AdminEpisodeProgressSummary(
                    episode.Id,
                    anime.Title,
                    episode.SeasonNumber,
                    episode.Number,
                    episode.Title,
                    progress.PositionMs,
                    progress.DurationMs,
                    progress.IsCompleted,
                    progress.UpdatedAt)
            })
            .ToListAsync(cancellationToken);

        var novelRows = await (
            from progress in db.NovelProgress.AsNoTracking()
            join work in db.NovelWorks.AsNoTracking()
                on progress.WorkId equals work.Id
            join chapter in db.NovelChapters.AsNoTracking()
                on progress.ChapterId equals chapter.Id
            select new
            {
                progress.ProfileId,
                Summary = new AdminNovelProgressSummary(
                    work.Id,
                    work.MetadataTitle ?? work.Title,
                    chapter.Number,
                    chapter.Title,
                    progress.PositionPermille,
                    progress.UpdatedAt)
            })
            .ToListAsync(cancellationToken);

        var termRows = await db.UserTerms
            .AsNoTracking()
            .GroupBy(x => x.ProfileId)
            .Select(group => new
            {
                ProfileId = group.Key,
                KnownTerms = group.Count(x => x.State == UserTermState.Known),
                LearningTerms = group.Count(x => x.State == UserTermState.Learning),
                LastUpdatedAt = group.Max(x => (DateTime?)x.UpdatedAt)
            })
            .ToListAsync(cancellationToken);

        var reviewRows = await db.Reviews
            .AsNoTracking()
            .GroupBy(x => x.ProfileId)
            .Select(group => new
            {
                ProfileId = group.Key,
                Reviews = group.Count(),
                LastReviewAt = group.Max(x => (DateTime?)x.ReviewedAt)
            })
            .ToListAsync(cancellationToken);

        var episodesByProfile = episodeRows
            .GroupBy(x => x.ProfileId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => x.Summary).ToArray());
        var novelsByProfile = novelRows
            .GroupBy(x => x.ProfileId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => x.Summary).ToArray());
        var termsByProfile = termRows.ToDictionary(x => x.ProfileId);
        var reviewsByProfile = reviewRows.ToDictionary(x => x.ProfileId);

        return accounts
            .Select(account =>
            {
                episodesByProfile.TryGetValue(account.Id, out var episodes);
                novelsByProfile.TryGetValue(account.Id, out var novels);
                termsByProfile.TryGetValue(account.Id, out var terms);
                reviewsByProfile.TryGetValue(account.Id, out var reviews);

                episodes ??= [];
                novels ??= [];

                var currentAnime = episodes
                    .OrderByDescending(x => x.UpdatedAt)
                    .FirstOrDefault();
                var currentNovel = novels
                    .OrderByDescending(x => x.UpdatedAt)
                    .FirstOrDefault();

                var learning = new AdminLearningProgressSummary(
                    terms?.KnownTerms ?? 0,
                    terms?.LearningTerms ?? 0,
                    reviews?.Reviews ?? 0,
                    reviews?.LastReviewAt);

                var lastActivity = MaxDate(
                    currentAnime?.UpdatedAt,
                    currentNovel?.UpdatedAt,
                    terms?.LastUpdatedAt,
                    reviews?.LastReviewAt);

                return new AdminUserProgressSummary(
                    account,
                    currentAnime,
                    episodes.Length,
                    episodes.Count(x => x.IsCompleted),
                    currentNovel,
                    novels.Length,
                    learning,
                    lastActivity);
            })
            .ToArray();
    }

    private static DateTime? MaxDate(params DateTime?[] values) =>
        values
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .DefaultIfEmpty()
            .Max() is var maximum && maximum != default
                ? maximum
                : null;
}
