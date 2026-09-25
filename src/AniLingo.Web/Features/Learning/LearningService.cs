using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Learning;

public sealed class LearningService
{
    private readonly AppDbContext db;
    private readonly IReviewScheduler scheduler;
    private readonly string profileId;
    private static readonly SemaphoreSlim OfflineSyncGate = new(1, 1);

    public LearningService(
        AppDbContext db,
        IReviewScheduler scheduler,
        CurrentAccountContext currentAccount)
        : this(db, scheduler, currentAccount.ProfileId)
    {
    }

    public LearningService(
        AppDbContext db,
        IReviewScheduler scheduler)
        : this(db, scheduler, LearningProfile.DefaultId)
    {
    }

    private LearningService(
        AppDbContext db,
        IReviewScheduler scheduler,
        string profileId)
    {
        this.db = db;
        this.scheduler = scheduler;
        this.profileId = profileId;
    }
    private LearningPreferencesSnapshot? preferencesCache;
    public async Task SetStateAsync(
        Guid termId,
        UserTermState state,
        CancellationToken cancellationToken)
    {
        var item = await db.UserTerms.SingleOrDefaultAsync(
            x => x.ProfileId == profileId && x.TermId == termId,
            cancellationToken);

        var now = DateTime.UtcNow;

        if (item is null)
        {
            item = new UserTerm
            {
                ProfileId = profileId,
                TermId = termId,
                State = state,
                UpdatedAt = now
            };
            db.UserTerms.Add(item);
        }

        item.State = state;
        item.UpdatedAt = now;

        switch (state)
        {
            case UserTermState.Known:
            case UserTermState.Saved:
            case UserTermState.Ignored:
            case UserTermState.Suspended:
                item.NextReviewAt = null;

                if (state is UserTermState.Saved or UserTermState.Ignored)
                {
                    item.LearningStartedAt = null;
                    item.QueuePosition = null;
                }

                await db.SaveChangesAsync(cancellationToken);
                return;

            case UserTermState.Learning:
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
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
    }

    public Task SaveForLaterAsync(
        Guid termId,
        CancellationToken cancellationToken) =>
        SetStateAsync(termId, UserTermState.Saved, cancellationToken);

    public Task IgnoreAsync(
        Guid termId,
        CancellationToken cancellationToken) =>
        SetStateAsync(termId, UserTermState.Ignored, cancellationToken);

    public Task SuspendAsync(
        Guid termId,
        CancellationToken cancellationToken) =>
        SetStateAsync(termId, UserTermState.Suspended, cancellationToken);

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
            .Where(x => x.ProfileId == profileId && ids.Contains(x.TermId))
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
                ProfileId = profileId,
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
                x => x.ProfileId == profileId
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
                x => x.ProfileId == profileId
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
                    x.ProfileId == profileId
                    && x.State == UserTermState.Learning
                    && x.LearningStartedAt == null
                    && x.NextReviewAt == null)
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
            where userTerm.ProfileId == profileId
                && userTerm.State == UserTermState.Learning
                && userTerm.NextReviewAt != null
                && userTerm.NextReviewAt <= now
            orderby userTerm.NextReviewAt, userTerm.QueuePosition, term.Canonical
            select new DueReviewItem(term.Id, term.Canonical, term.Reading, term.Meaning, userTerm.IntervalDays))
            .Take(preferences.ReviewBatchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReviewSessionCard>> GetReviewSessionAsync(
        CancellationToken cancellationToken)
    {
        var due = await GetDueAsync(cancellationToken);
        if (due.Count == 0)
        {
            return [];
        }

        var now = DateTimeOffset.UtcNow;
        var preferences = await GetPreferencesAsync(cancellationToken);
        var termIds = due.Select(x => x.TermId).ToArray();

        var reviewRows = await db.Reviews
            .AsNoTracking()
            .Where(x => x.ProfileId == profileId && termIds.Contains(x.TermId))
            .OrderBy(x => x.TermId)
            .ThenBy(x => x.ReviewedAt)
            .Select(x => new { x.TermId, x.Rating, x.ReviewedAt })
            .ToListAsync(cancellationToken);

        var histories = reviewRows
            .GroupBy(x => x.TermId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ReviewHistoryItem>)group
                    .Select(x => new ReviewHistoryItem(
                        x.Rating,
                        new DateTimeOffset(DateTime.SpecifyKind(
                            x.ReviewedAt,
                            DateTimeKind.Utc))))
                    .ToArray());

        var contextCandidates = await (
            from episodeTerm in db.EpisodeTerms.AsNoTracking()
            join episode in db.Episodes.AsNoTracking()
                on episodeTerm.EpisodeId equals episode.Id
            join anime in db.Anime.AsNoTracking()
                on episode.AnimeId equals anime.Id
            where termIds.Contains(episodeTerm.TermId)
            orderby episodeTerm.TermId,
                episodeTerm.Occurrences descending,
                anime.Title,
                episode.SeasonNumber,
                episode.Number,
                episode.Id
            select new ReviewContextCandidate(
                episodeTerm.TermId,
                episode.Id,
                anime.Title,
                episode.SeasonNumber,
                episode.Number,
                episode.Title,
                episodeTerm.FirstCueStartMs))
            .ToListAsync(cancellationToken);

        var selectedContexts = contextCandidates
            .GroupBy(x => x.TermId)
            .ToDictionary(x => x.Key, x => x.First());

        var episodeIds = selectedContexts.Values
            .Select(x => x.EpisodeId)
            .Distinct()
            .ToArray();

        var trackRows = episodeIds.Length == 0
            ? []
            : await db.SubtitleTracks
                .AsNoTracking()
                .Where(x => episodeIds.Contains(x.EpisodeId) && x.Language == "ja")
                .OrderByDescending(x => x.ImportedAt)
                .ThenBy(x => x.Id)
                .Select(x => new ReviewTrackCandidate(x.EpisodeId, x.Id))
                .ToListAsync(cancellationToken);

        var trackByEpisode = trackRows
            .GroupBy(x => x.EpisodeId)
            .ToDictionary(x => x.Key, x => x.First().TrackId);

        var selectedTrackIds = trackByEpisode.Values.Distinct().ToArray();
        var selectedStarts = selectedContexts.Values
            .Select(x => x.CueStartMs)
            .Distinct()
            .ToArray();

        var cueRows = selectedTrackIds.Length == 0
            ? []
            : await db.SubtitleCues
                .AsNoTracking()
                .Where(x =>
                    selectedTrackIds.Contains(x.SubtitleTrackId)
                    && selectedStarts.Contains(x.StartMs))
                .Select(x => new ReviewCueCandidate(
                    x.SubtitleTrackId,
                    x.StartMs,
                    x.Text))
                .ToListAsync(cancellationToken);

        var cueByKey = cueRows
            .GroupBy(x => (x.TrackId, x.StartMs))
            .ToDictionary(x => x.Key, x => x.First().Text);

        return due
            .Select(item =>
            {
                var history = histories.GetValueOrDefault(
                    item.TermId,
                    Array.Empty<ReviewHistoryItem>());
                var schedules = scheduler.Preview(
                    item.TermId,
                    now,
                    history,
                    preferences.DesiredRetention);
                var intervals = schedules.ToDictionary(
                    x => x.Key,
                    x => FormatInterval(now, x.Value.NextReviewAt));

                ReviewAnimeContext? context = null;
                if (selectedContexts.TryGetValue(item.TermId, out var selected)
                    && trackByEpisode.TryGetValue(selected.EpisodeId, out var trackId)
                    && cueByKey.TryGetValue((trackId, selected.CueStartMs), out var sentence)
                    && !string.IsNullOrWhiteSpace(sentence))
                {
                    context = new ReviewAnimeContext(
                        selected.EpisodeId,
                        selected.AnimeTitle,
                        selected.SeasonNumber,
                        selected.EpisodeNumber,
                        selected.EpisodeTitle,
                        selected.CueStartMs,
                        sentence);
                }

                return new ReviewSessionCard(
                    item.TermId,
                    item.Canonical,
                    item.Reading,
                    item.Meaning,
                    item.IntervalDays,
                    intervals,
                    context);
            })
            .ToArray();
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
            .Where(x => x.ProfileId == profileId)
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
                x => x.ProfileId == profileId,
                cancellationToken);

        if (row is null)
        {
            row = new LearningPreferences
            {
                ProfileId = profileId
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
            x => x.ProfileId == profileId
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
            ProfileId = profileId,
            TermId = termId,
            Rating = rating,
            ReviewedAt = now.UtcDateTime,
            NextReviewAt = schedule.NextReviewAt.UtcDateTime
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<OfflineReviewSyncResult> SyncOfflineReviewsAsync(
        IReadOnlyList<OfflineReviewEvent> events,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        const int maxBatchSize = 100;
        nowUtc = NormalizeUtc(nowUtc);

        var accepted = new List<Guid>();
        var alreadyApplied = new List<Guid>();
        var rejected = new List<Guid>();

        if (events.Count == 0)
        {
            return new OfflineReviewSyncResult(accepted, alreadyApplied, rejected);
        }

        await OfflineSyncGate.WaitAsync(cancellationToken);
        try
        {
            var batch = events.Take(maxBatchSize).ToArray();
            rejected.AddRange(events.Skip(maxBatchSize).Select(x => x.EventId));

            var seenEventIds = new HashSet<Guid>();
            var candidates = new List<OfflineReviewEvent>();

            foreach (var item in batch)
            {
                var reviewedAt = NormalizeUtc(item.ReviewedAtUtc);

                if (item.EventId == Guid.Empty
                    || item.TermId == Guid.Empty
                    || !Enum.IsDefined(item.Rating)
                    || reviewedAt > nowUtc
                    || !seenEventIds.Add(item.EventId))
                {
                    rejected.Add(item.EventId);
                    continue;
                }

                candidates.Add(item with { ReviewedAtUtc = reviewedAt });
            }

            if (candidates.Count == 0)
            {
                return new OfflineReviewSyncResult(
                    accepted.Distinct().ToArray(),
                    alreadyApplied.Distinct().ToArray(),
                    rejected.Distinct().ToArray());
            }

            var eventIds = candidates.Select(x => x.EventId).ToArray();
            var existingEventIds = await db.Reviews
                .AsNoTracking()
                .Where(x =>
                    x.ProfileId == profileId
                    && x.ClientEventId != null
                    && eventIds.Contains(x.ClientEventId.Value))
                .Select(x => x.ClientEventId!.Value)
                .ToListAsync(cancellationToken);

            var existingSet = existingEventIds.ToHashSet();
            alreadyApplied.AddRange(existingEventIds);

            var remaining = candidates
                .Where(x => !existingSet.Contains(x.EventId))
                .ToArray();

            var termIds = remaining.Select(x => x.TermId).Distinct().ToArray();
            var ownedTerms = termIds.Length == 0
                ? new HashSet<Guid>()
                : (await db.UserTerms
                    .AsNoTracking()
                    .Where(x =>
                        x.ProfileId == profileId
                        && x.State == UserTermState.Learning
                        && termIds.Contains(x.TermId))
                    .Select(x => x.TermId)
                    .ToListAsync(cancellationToken))
                    .ToHashSet();

            var toApply = remaining
                .Where(x =>
                {
                    if (ownedTerms.Contains(x.TermId))
                    {
                        return true;
                    }

                    rejected.Add(x.EventId);
                    return false;
                })
                .OrderBy(x => x.ReviewedAtUtc)
                .ThenBy(x => x.EventId)
                .ToArray();

            foreach (var item in toApply)
            {
                db.Reviews.Add(new Review
                {
                    ProfileId = profileId,
                    TermId = item.TermId,
                    Rating = item.Rating,
                    ClientEventId = item.EventId,
                    ReviewedAt = item.ReviewedAtUtc,
                    NextReviewAt = item.ReviewedAtUtc
                });
                accepted.Add(item.EventId);
            }

            if (toApply.Length > 0)
            {
                await db.SaveChangesAsync(cancellationToken);

                var preferences = await GetPreferencesAsync(cancellationToken);
                foreach (var termId in toApply.Select(x => x.TermId).Distinct())
                {
                    await RebuildTermScheduleAsync(
                        termId,
                        preferences.DesiredRetention,
                        cancellationToken);
                }

                await db.SaveChangesAsync(cancellationToken);
            }

            return new OfflineReviewSyncResult(
                accepted.Distinct().ToArray(),
                alreadyApplied.Distinct().ToArray(),
                rejected.Distinct().ToArray());
        }
        finally
        {
            OfflineSyncGate.Release();
        }
    }

    private async Task RebuildTermScheduleAsync(
        Guid termId,
        double desiredRetention,
        CancellationToken cancellationToken)
    {
        var userTerm = await db.UserTerms
            .SingleAsync(
                x => x.ProfileId == profileId
                    && x.TermId == termId
                    && x.State == UserTermState.Learning,
                cancellationToken);

        var reviews = await db.Reviews
            .Where(x => x.ProfileId == profileId && x.TermId == termId)
            .OrderBy(x => x.ReviewedAt)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var history = new List<ReviewHistoryItem>(reviews.Count);
        ReviewSchedule? finalSchedule = null;

        foreach (var review in reviews)
        {
            var reviewedAt = new DateTimeOffset(
                DateTime.SpecifyKind(review.ReviewedAt, DateTimeKind.Utc));
            var schedule = scheduler.Schedule(
                termId,
                reviewedAt,
                history,
                review.Rating,
                desiredRetention);

            review.NextReviewAt = schedule.NextReviewAt.UtcDateTime;
            history.Add(new ReviewHistoryItem(review.Rating, reviewedAt));
            finalSchedule = schedule;
        }

        if (finalSchedule is null)
        {
            return;
        }

        userTerm.IntervalDays = finalSchedule.IntervalDays;
        userTerm.NextReviewAt = finalSchedule.NextReviewAt.UtcDateTime;
        userTerm.UpdatedAt = DateTime.UtcNow;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    private async Task<long> GetNextQueuePositionAsync(CancellationToken cancellationToken) =>
        (await GetCurrentMaxQueuePositionAsync(cancellationToken)) + 1;

    private async Task<long> GetCurrentMaxQueuePositionAsync(CancellationToken cancellationToken) =>
        await db.UserTerms
            .Where(x => x.ProfileId == profileId)
            .MaxAsync(x => (long?)x.QueuePosition, cancellationToken)
        ?? 0;

    private async Task<IReadOnlyList<ReviewHistoryItem>> GetHistoryAsync(
        Guid termId,
        CancellationToken cancellationToken)
    {
        var rows = await db.Reviews
            .AsNoTracking()
            .Where(x => x.ProfileId == profileId && x.TermId == termId)
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
    private sealed record ReviewContextCandidate(
        Guid TermId,
        Guid EpisodeId,
        string AnimeTitle,
        int SeasonNumber,
        int EpisodeNumber,
        string EpisodeTitle,
        int CueStartMs);

    private sealed record ReviewTrackCandidate(Guid EpisodeId, Guid TrackId);
    private sealed record ReviewCueCandidate(Guid TrackId, int StartMs, string Text);

}
