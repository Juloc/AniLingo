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

    public async Task ReviewAsync(Guid termId, ReviewRating rating, CancellationToken cancellationToken)
    {
        var userTerm = await db.UserTerms.SingleAsync(
            x => x.ProfileId == LearningProfile.DefaultId
                && x.TermId == termId
                && x.State == UserTermState.Learning,
            cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var schedule = scheduler.Schedule(now, userTerm.IntervalDays, rating);

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
}
