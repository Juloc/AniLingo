using AniLingo.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Learning;

public sealed class LearningService(
    AppDbContext db,
    IReviewScheduler scheduler)
{
    private LearningPreferencesSnapshot? preferencesCache;
    public async Task SetStateAsync(Guid termId, UserTermState state, CancellationToken cancellationToken)
    {
        var item = await db.UserTerms.SingleOrDefaultAsync(
            x => x.ProfileId == LearningProfile.DefaultId && x.TermId == termId,
            cancellationToken);

        var now = DateTime.UtcNow;

        if (item is null)
        {
            item = new UserTerm
            {
                ProfileId = LearningProfile.DefaultId,
                TermId = termId,
                State = state,
                UpdatedAt = now
            };
            db.UserTerms.Add(item);
        }

        if (state == UserTermState.Known)
        {
            item.State = UserTermState.Known;
            item.NextReviewAt = null;
            item.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        item.State = UserTermState.Learning;
        item.UpdatedAt = now;

        if (item.LearningStartedAt is null)
        {
            item.NextReviewAt = null;
            item.QueuePosition ??= await GetNextQueuePositionAsync(cancellationToken);
        }
        else
        {
            item.NextReviewAt ??= now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddToLearningAsync(
        IReadOnlyCollection<Guid> termIds,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<Guid>();
        var ids = termIds.Where(seen.Add).ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        var existing = await db.UserTerms
            .Where(x => x.ProfileId == LearningProfile.DefaultId && ids.Contains(x.TermId))
            .ToDictionaryAsync(x => x.TermId, cancellationToken);

        var nextQueuePosition = await GetCurrentMaxQueuePositionAsync(cancellationToken);
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
                item.NextReviewAt = null;
                item.LearningStartedAt = null;
                item.QueuePosition = ++nextQueuePosition;
                item.UpdatedAt = now;
                continue;
            }

            db.UserTerms.Add(new UserTerm
            {
                ProfileId = LearningProfile.DefaultId,
                TermId = termId,
                State = UserTermState.Learning,
                NextReviewAt = null,
                LearningStartedAt = null,
                QueuePosition = ++nextQueuePosition,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<DueReviewItem>> GetDueAsync(CancellationToken cancellationToken)
    {
        var preferences = await GetPreferencesAsync(cancellationToken);
        var now = DateTime.UtcNow;

        var dueStartedCount = await db.UserTerms
            .AsNoTracking()
            .CountAsync(
                x => x.ProfileId == LearningProfile.DefaultId
                    && x.State == UserTermState.Learning
                    && x.NextReviewAt != null
                    && x.NextReviewAt <= now,
                cancellationToken);

        var batchSlotsForNew = Math.Max(
            0,
            preferences.ReviewBatchSize - Math.Min(dueStartedCount, preferences.ReviewBatchSize));

        var dayStart = now.Date;
        var dayEnd = dayStart.AddDays(1);
        var startedToday = await db.UserTerms
            .AsNoTracking()
            .CountAsync(
                x => x.ProfileId == LearningProfile.DefaultId
                    && x.LearningStartedAt != null
                    && x.LearningStartedAt >= dayStart
                    && x.LearningStartedAt < dayEnd,
                cancellationToken);

        var dailySlotsForNew = Math.Max(0, preferences.NewWordsPerDay - startedToday);
        var activateCount = Math.Min(batchSlotsForNew, dailySlotsForNew);

        if (activateCount > 0)
        {
            var queued = await db.UserTerms
                .Where(x =>
                    x.ProfileId == LearningProfile.DefaultId
                    && x.State == UserTermState.Learning
                    && x.LearningStartedAt == null)
                .OrderBy(x => x.QueuePosition ?? long.MaxValue)
                .ThenBy(x => x.UpdatedAt)
                .ThenBy(x => x.TermId)
                .Take(activateCount)
                .ToListAsync(cancellationToken);

            foreach (var item in queued)
            {
                item.LearningStartedAt = now;
                item.NextReviewAt = now;
                item.UpdatedAt = now;
            }

            if (queued.Count > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return await (
            from userTerm in db.UserTerms.AsNoTracking()
            join term in db.Terms.AsNoTracking() on userTerm.TermId equals term.Id
            where userTerm.ProfileId == LearningProfile.DefaultId
                && userTerm.State == UserTermState.Learning
                && userTerm.NextReviewAt != null
                && userTerm.NextReviewAt <= now
            orderby userTerm.NextReviewAt, userTerm.QueuePosition, term.Canonical
            select new DueReviewItem(term.Id, term.Canonical, term.Reading, term.Meaning, userTerm.IntervalDays))
            .Take(preferences.ReviewBatchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<LearningPreferencesSnapshot> GetPreferencesAsync(
        CancellationToken cancellationToken)
    {
        if (preferencesCache is not null)
        {
            return preferencesCache;
        }

        var row = await db.LearningPreferences
            .AsNoTracking()
            .Where(x => x.ProfileId == LearningProfile.DefaultId)
            .Select(x => new LearningPreferencesSnapshot(
                x.DesiredRetention,
                x.ReviewBatchSize,
                x.NewWordsPerDay))
            .SingleOrDefaultAsync(cancellationToken);

        preferencesCache = row ?? LearningPreferencesSnapshot.Default;
        return preferencesCache;
    }

    public async Task SavePreferencesAsync(
        double desiredRetention,
        int reviewBatchSize,
        int newWordsPerDay,
        CancellationToken cancellationToken)
    {
        if (desiredRetention is < 0.80 or > 0.97)
        {
            throw new ArgumentOutOfRangeException(
                nameof(desiredRetention),
                "Desired retention must be between 0.80 and 0.97.");
        }

        if (reviewBatchSize is < 5 or > 200)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reviewBatchSize),
                "Review batch size must be between 5 and 200.");
        }

        if (newWordsPerDay is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newWordsPerDay),
                "New words per day must be between 0 and 100.");
        }

        var row = await db.LearningPreferences
            .SingleOrDefaultAsync(
                x => x.ProfileId == LearningProfile.DefaultId,
                cancellationToken);

        if (row is null)
        {
            row = new LearningPreferences
            {
                ProfileId = LearningProfile.DefaultId
            };
            db.LearningPreferences.Add(row);
        }

        row.DesiredRetention = desiredRetention;
        row.ReviewBatchSize = reviewBatchSize;
        row.NewWordsPerDay = newWordsPerDay;
        await db.SaveChangesAsync(cancellationToken);

        preferencesCache = new LearningPreferencesSnapshot(
            desiredRetention,
            reviewBatchSize,
            newWordsPerDay);
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
        var preferences = await GetPreferencesAsync(cancellationToken);
        var schedules = scheduler.Preview(
            termId,
            now,
            history,
            preferences.DesiredRetention);

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
        var preferences = await GetPreferencesAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var schedule = scheduler.Schedule(
            termId,
            now,
            history,
            rating,
            preferences.DesiredRetention);

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

    private async Task<long> GetNextQueuePositionAsync(CancellationToken cancellationToken) =>
        (await GetCurrentMaxQueuePositionAsync(cancellationToken)) + 1;

    private async Task<long> GetCurrentMaxQueuePositionAsync(CancellationToken cancellationToken) =>
        await db.UserTerms
            .Where(x => x.ProfileId == LearningProfile.DefaultId)
            .MaxAsync(x => (long?)x.QueuePosition, cancellationToken)
        ?? 0;

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
