using AniLingo.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Learning;

public sealed class LearningService(
    AppDbContext db,
    IReviewScheduler scheduler)
{
    public async Task SetStateAsync(Guid termId, UserTermState state, CancellationToken cancellationToken)
    {
        var item = await db.UserTerms.SingleOrDefaultAsync(
            x => x.ProfileId == LearningProfile.DefaultId && x.TermId == termId,
            cancellationToken);

        if (item is null)
        {
            item = new UserTerm
            {
                ProfileId = LearningProfile.DefaultId,
                TermId = termId
            };
            db.UserTerms.Add(item);
        }

        item.State = state;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        item.NextReviewAt = state == UserTermState.Learning
            ? item.NextReviewAt ?? DateTimeOffset.UtcNow
            : null;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddToLearningAsync(
        IReadOnlyCollection<Guid> termIds,
        CancellationToken cancellationToken)
    {
        var ids = termIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        var existing = await db.UserTerms
            .Where(x => x.ProfileId == LearningProfile.DefaultId && ids.Contains(x.TermId))
            .ToDictionaryAsync(x => x.TermId, cancellationToken);

        var now = DateTimeOffset.UtcNow;

        foreach (var termId in ids)
        {
            if (existing.TryGetValue(termId, out var item))
            {
                if (item.State is UserTermState.Known or UserTermState.Learning)
                {
                    continue;
                }

                item.State = UserTermState.Learning;
                item.NextReviewAt ??= now;
                item.UpdatedAt = now;
                continue;
            }

            db.UserTerms.Add(new UserTerm
            {
                ProfileId = LearningProfile.DefaultId,
                TermId = termId,
                State = UserTermState.Learning,
                NextReviewAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<List<DueReviewItem>> GetDueAsync(int limit, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        return (
            from userTerm in db.UserTerms.AsNoTracking()
            join term in db.Terms.AsNoTracking() on userTerm.TermId equals term.Id
            where userTerm.ProfileId == LearningProfile.DefaultId
                && userTerm.State == UserTermState.Learning
                && userTerm.NextReviewAt != null
                && userTerm.NextReviewAt <= now
            orderby userTerm.NextReviewAt
            select new DueReviewItem(term.Id, term.Canonical, term.Reading, term.Meaning, userTerm.IntervalDays))
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReviewOption>> GetReviewOptionsAsync(
        Guid termId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var history = await GetHistoryAsync(termId, cancellationToken);
        var schedules = scheduler.Preview(termId, now, history);

        return Enum.GetValues<ReviewRating>()
            .Select(rating => new ReviewOption(
                rating,
                schedules[rating].NextReviewAt,
                FormatInterval(now, schedules[rating].NextReviewAt)))
            .ToArray();
    }

    public async Task ReviewAsync(Guid termId, ReviewRating rating, CancellationToken cancellationToken)
    {
        var userTerm = await db.UserTerms.SingleAsync(
            x => x.ProfileId == LearningProfile.DefaultId
                && x.TermId == termId
                && x.State == UserTermState.Learning,
            cancellationToken);

        var history = await GetHistoryAsync(termId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var schedule = scheduler.Schedule(termId, now, history, rating);

        userTerm.IntervalDays = schedule.IntervalDays;
        userTerm.NextReviewAt = schedule.NextReviewAt;
        userTerm.UpdatedAt = now;

        db.Reviews.Add(new Review
        {
            ProfileId = LearningProfile.DefaultId,
            TermId = termId,
            Rating = rating,
            ReviewedAt = now,
            NextReviewAt = schedule.NextReviewAt
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<ReviewHistoryItem>> GetHistoryAsync(
        Guid termId,
        CancellationToken cancellationToken)
    {
        var rows = await db.Reviews
            .AsNoTracking()
            .Where(x => x.ProfileId == LearningProfile.DefaultId && x.TermId == termId)
            .OrderBy(x => x.ReviewedAt)
            .Select(x => new { x.Rating, x.ReviewedAt })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new ReviewHistoryItem(x.Rating, x.ReviewedAt))
            .ToArray();
    }

    private static string FormatInterval(DateTimeOffset now, DateTimeOffset due)
    {
        var interval = due - now;

        if (interval <= TimeSpan.FromMinutes(1))
        {
            return "1m";
        }

        if (interval < TimeSpan.FromHours(1))
        {
            return $"{Math.Ceiling(interval.TotalMinutes):0}m";
        }

        if (interval < TimeSpan.FromDays(1))
        {
            return $"{Math.Ceiling(interval.TotalHours):0}h";
        }

        if (interval < TimeSpan.FromDays(60))
        {
            return $"{Math.Round(interval.TotalDays):0}d";
        }

        if (interval < TimeSpan.FromDays(730))
        {
            return $"{interval.TotalDays / 30.44:0.#}mo";
        }

        return $"{interval.TotalDays / 365.25:0.#}y";
    }
}
