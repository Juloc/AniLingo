using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Statistics;

public sealed record LearningStatisticsSnapshot(
    int KnownTerms,
    int LearningTerms,
    int DueReviews,
    int TotalReviews,
    int ReviewsLast24Hours,
    int ReviewsLast7Days,
    int PreparedOccurrences,
    int TotalOccurrences)
{
    public int PreparedCoveragePercent => TotalOccurrences == 0
        ? 0
        : (int)Math.Floor((double)PreparedOccurrences / TotalOccurrences * 100);
}

public sealed class LearningStatisticsService(AppDbContext db)
{
    public async Task<LearningStatisticsSnapshot> LoadAsync(
        string profileId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Profile ID is required.", nameof(profileId));
        }

        nowUtc = NormalizeUtc(nowUtc);
        var last24Hours = nowUtc.AddHours(-24);
        var last7Days = nowUtc.AddDays(-7);

        var stateCounts = await LearningQueries.WordCards(db, profileId)
            .AsNoTracking()
            .GroupBy(x => x.State)
            .Select(group => new
            {
                State = group.Key,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        var knownTerms = stateCounts
            .Where(x => x.State == UserTermState.Known)
            .Select(x => x.Count)
            .SingleOrDefault();
        var learningTerms = stateCounts
            .Where(x => x.State == UserTermState.Learning)
            .Select(x => x.Count)
            .SingleOrDefault();

        var dueReviews = await LearningQueries
            .DueCards(db, profileId, nowUtc)
            .CountAsync(cancellationToken);

        var reviewStats = await db.LearningCardReviews
            .AsNoTracking()
            .Where(x => x.ProfileId == profileId)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.Count(),
                Last24Hours = group.Count(x => x.ReviewedAt >= last24Hours),
                Last7Days = group.Count(x => x.ReviewedAt >= last7Days)
            })
            .SingleOrDefaultAsync(cancellationToken);

        var totalOccurrences = await db.EpisodeTerms
            .AsNoTracking()
            .SumAsync(x => (int?)x.Occurrences, cancellationToken)
            ?? 0;

        var preparedOccurrences = await (
            from episodeTerm in db.EpisodeTerms.AsNoTracking()
            join state in LearningQueries.TermStates(db, profileId)
                    .Where(x =>
                        x.State == UserTermState.Known
                        || x.State == UserTermState.Learning)
                on episodeTerm.TermId equals state.TermId
            select (int?)episodeTerm.Occurrences)
            .SumAsync(cancellationToken)
            ?? 0;

        return new LearningStatisticsSnapshot(
            knownTerms,
            learningTerms,
            dueReviews,
            reviewStats?.Total ?? 0,
            reviewStats?.Last24Hours ?? 0,
            reviewStats?.Last7Days ?? 0,
            preparedOccurrences,
            totalOccurrences);
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
