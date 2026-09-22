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
        item.UpdatedAt = DateTime.UtcNow;
        item.NextReviewAt = state == UserTermState.Learning
            ? item.NextReviewAt ?? DateTime.UtcNow
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

        var now = DateTime.UtcNow;

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
        var now = DateTime.UtcNow;

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

    public async Task<ReviewAnimeContext?> GetReviewContextAsync(
        Guid termId,
        CancellationToken cancellationToken)
    {
        var source = await (
            from episodeTerm in db.EpisodeTerms.AsNoTracking()
            join episode in db.Episodes.AsNoTracking() on episodeTerm.EpisodeId equals episode.Id
            join anime in db.Anime.AsNoTracking() on episode.AnimeId equals anime.Id
            where episodeTerm.TermId == termId
            orderby episodeTerm.Occurrences descending,
                anime.Title,
                episode.SeasonNumber,
                episode.Number,
                episode.Id
            select new
            {
                episode.Id,
                AnimeTitle = anime.Title,
                episode.SeasonNumber,
                EpisodeNumber = episode.Number,
                EpisodeTitle = episode.Title,
                episodeTerm.FirstCueStartMs
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (source is null)
        {
            return null;
        }

        var trackId = await db.SubtitleTracks
            .AsNoTracking()
            .Where(x => x.EpisodeId == source.Id && x.Language == "ja")
            .OrderByDescending(x => x.ImportedAt)
            .ThenBy(x => x.Id)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (trackId is null)
        {
            return null;
        }

        var sentence = await db.SubtitleCues
            .AsNoTracking()
            .Where(x =>
                x.SubtitleTrackId == trackId.Value &&
                x.StartMs == source.FirstCueStartMs)
            .Select(x => x.Text)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(sentence))
        {
            return null;
        }

        return new ReviewAnimeContext(
            source.Id,
            source.AnimeTitle,
            source.SeasonNumber,
            source.EpisodeNumber,
            source.EpisodeTitle,
            source.FirstCueStartMs,
            sentence);
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
        userTerm.NextReviewAt = schedule.NextReviewAt.UtcDateTime;
        userTerm.UpdatedAt = now.UtcDateTime;

        db.Reviews.Add(new Review
        {
            ProfileId = LearningProfile.DefaultId,
            TermId = termId,
            Rating = rating,
            ReviewedAt = now.UtcDateTime,
            NextReviewAt = schedule.NextReviewAt.UtcDateTime
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
            .Select(x => new ReviewHistoryItem(
                x.Rating,
                new DateTimeOffset(DateTime.SpecifyKind(x.ReviewedAt, DateTimeKind.Utc))))
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
