using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning.Courses;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Learning;

/// <summary>
/// Profile-bound owner of Learning card state and FSRS scheduling. Catalog
/// words (subtitle Terms) are addressed through the unit linked to the term in
/// the profile's primary course for the term language; every other operation
/// addresses directional cards directly.
/// </summary>
public sealed class LearningService
{
    private readonly AppDbContext db;
    private readonly IReviewScheduler scheduler;
    private readonly LearningCourseStore courses;
    private readonly string profileId;
    private static readonly SemaphoreSlim OfflineSyncGate = new(1, 1);
    private LearningPreferencesSnapshot? preferencesCache;

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
        courses = new LearningCourseStore(db);
    }

    public string ProfileId => profileId;

    /// <summary>
    /// Sets the state of a catalog word: every card of its unit in the primary
    /// course for the term language, creating unit, course and cards on first use.
    /// </summary>
    public async Task SetStateAsync(
        Guid termId,
        UserTermState state,
        CancellationToken cancellationToken)
    {
        var term = await db.Terms
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == termId, cancellationToken)
            ?? throw new KeyNotFoundException("The term does not exist.");

        var course = await courses.ResolvePrimaryCourseAsync(
            profileId,
            term.Language,
            cancellationToken);
        var unit = await courses.EnsureTermUnitAsync(term, cancellationToken);
        await SetUnitStateAsync(course, unit.Id, state, cancellationToken);
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

    /// <summary>
    /// Queues catalog words for learning in the given order. Cards that are
    /// already Known or Learning keep their state.
    /// </summary>
    public async Task AddToLearningAsync(
        IReadOnlyCollection<Guid> termIds,
        CancellationToken cancellationToken)
    {
        var ids = termIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        var terms = await db.Terms
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var coursesByLanguage = new Dictionary<string, LearningCourse>(StringComparer.Ordinal);
        var targets = new List<(LearningCourse Course, Guid UnitId)>(ids.Length);

        foreach (var id in ids)
        {
            if (!terms.TryGetValue(id, out var term))
            {
                throw new KeyNotFoundException("The term does not exist.");
            }

            if (!coursesByLanguage.TryGetValue(term.Language, out var course))
            {
                course = await courses.ResolvePrimaryCourseAsync(
                    profileId,
                    term.Language,
                    cancellationToken);
                coursesByLanguage[term.Language] = course;
            }

            var unit = await courses.EnsureTermUnitAsync(term, cancellationToken);
            targets.Add((course, unit.Id));
        }

        await QueueAsync(targets, cancellationToken);
    }

    /// <summary>Sets the state of every card of a unit in one of the profile's courses.</summary>
    public async Task SetUnitStateAsync(
        Guid courseId,
        Guid unitId,
        UserTermState state,
        CancellationToken cancellationToken)
    {
        var course = await courses.FindOwnedAsync(profileId, courseId, cancellationToken)
            ?? throw new KeyNotFoundException("Learning course was not found for this profile.");

        await SetUnitStateAsync(course, unitId, state, cancellationToken);
    }

    public async Task AddUnitsToLearningAsync(
        Guid courseId,
        IReadOnlyCollection<Guid> unitIds,
        CancellationToken cancellationToken)
    {
        var course = await courses.FindOwnedAsync(profileId, courseId, cancellationToken)
            ?? throw new KeyNotFoundException("Learning course was not found for this profile.");

        await QueueAsync(
            unitIds.Distinct().Select(unitId => (course, unitId)).ToArray(),
            cancellationToken);
    }

    public async Task<List<DueReviewItem>> GetDueAsync(CancellationToken cancellationToken)
    {
        var preferences = await GetPreferencesAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var scheduled = LearningQueries.ScheduledCards(db, profileId);

        var dueStartedCount = await LearningQueries
            .DueCards(db, profileId, now)
            .CountAsync(cancellationToken);

        var batchSlotsForNew = Math.Max(
            0,
            preferences.ReviewBatchSize - Math.Min(dueStartedCount, preferences.ReviewBatchSize));

        var dayStart = now.Date;
        var dayEnd = dayStart.AddDays(1);
        var startedToday = await scheduled.CountAsync(
            x => x.LearningStartedAt != null
                && x.LearningStartedAt >= dayStart
                && x.LearningStartedAt < dayEnd,
            cancellationToken);

        var dailySlotsForNew = Math.Max(0, preferences.NewWordsPerDay - startedToday);
        var activateCount = Math.Min(batchSlotsForNew, dailySlotsForNew);

        if (activateCount > 0)
        {
            var queued = await scheduled
                .Where(x =>
                    x.State == UserTermState.Learning
                    && x.LearningStartedAt == null
                    && x.NextReviewAt == null)
                .OrderBy(x => x.QueuePosition ?? long.MaxValue)
                .ThenBy(x => x.UpdatedAt)
                .ThenBy(x => x.Id)
                .Take(activateCount)
                .ToListAsync(cancellationToken);

            foreach (var card in queued)
            {
                card.LearningStartedAt = now;
                card.NextReviewAt = now;
                card.UpdatedAt = now;
            }

            if (queued.Count > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        var due = await LearningQueries
            .DueCards(db, profileId, now)
            .AsNoTracking()
            .OrderBy(x => x.NextReviewAt)
            .ThenBy(x => x.QueuePosition)
            .ThenBy(x => x.Id)
            .Take(preferences.ReviewBatchSize)
            .ToListAsync(cancellationToken);

        return await DescribeAsync(due, cancellationToken);
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
        var cardIds = due.Select(x => x.CardId).ToArray();

        var reviewRows = await db.LearningCardReviews
            .AsNoTracking()
            .Where(x => cardIds.Contains(x.CardId))
            .OrderBy(x => x.CardId)
            .ThenBy(x => x.ReviewedAt)
            .Select(x => new { x.CardId, x.Rating, x.ReviewedAt })
            .ToListAsync(cancellationToken);

        var histories = reviewRows
            .GroupBy(x => x.CardId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ReviewHistoryItem>)group
                    .Select(x => new ReviewHistoryItem(x.Rating, AsUtcOffset(x.ReviewedAt)))
                    .ToArray());

        var contexts = await LoadTermContextsAsync(
            due.Where(x => x.TermId is not null).Select(x => x.TermId!.Value).Distinct().ToArray(),
            cancellationToken);

        return due
            .Select(item =>
            {
                var history = histories.GetValueOrDefault(
                    item.CardId,
                    Array.Empty<ReviewHistoryItem>());
                var schedules = scheduler.Preview(
                    item.CardId,
                    now,
                    history,
                    preferences.DesiredRetention);

                return new ReviewSessionCard(
                    item.CardId,
                    item.TermId,
                    item.Mode,
                    item.PromptLanguage,
                    item.AnswerLanguage,
                    item.Prompt,
                    item.PromptReading,
                    item.Answer,
                    item.AnswerReading,
                    item.IntervalDays,
                    schedules.ToDictionary(
                        x => x.Key,
                        x => FormatInterval(now, x.Value.NextReviewAt)),
                    item.TermId is { } termId
                        ? contexts.GetValueOrDefault(termId)
                        : null);
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

    /// <summary>
    /// Anime sentence for a catalog term: the exact first cue of the episode
    /// where the term occurs most often, from a subtitle track in the term language.
    /// </summary>
    public async Task<ReviewAnimeContext?> GetReviewContextAsync(
        Guid termId,
        CancellationToken cancellationToken)
    {
        var contexts = await LoadTermContextsAsync([termId], cancellationToken);
        return contexts.GetValueOrDefault(termId);
    }

    public async Task ReviewAsync(
        Guid cardId,
        ReviewRating rating,
        CancellationToken cancellationToken)
    {
        var card = await db.LearningCards.SingleOrDefaultAsync(
            x => x.Id == cardId
                && x.ProfileId == profileId
                && x.State == UserTermState.Learning,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The card is not in active learning for this profile.");

        var history = await GetHistoryAsync(cardId, cancellationToken);
        var preferences = await GetPreferencesAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var schedule = scheduler.Schedule(
            cardId,
            now,
            history,
            rating,
            preferences.DesiredRetention);

        card.IntervalDays = schedule.IntervalDays;
        card.NextReviewAt = schedule.NextReviewAt.UtcDateTime;
        card.UpdatedAt = now.UtcDateTime;

        db.LearningCardReviews.Add(new LearningCardReview
        {
            ProfileId = profileId,
            CardId = cardId,
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
                var addressesCard = item.CardId is { } cardId && cardId != Guid.Empty;
                var addressesTerm = item.TermId is { } termId && termId != Guid.Empty;

                if (item.EventId == Guid.Empty
                    || (!addressesCard && !addressesTerm)
                    || !Enum.IsDefined(item.Rating)
                    || reviewedAt > nowUtc
                    || !seenEventIds.Add(item.EventId))
                {
                    rejected.Add(item.EventId);
                    continue;
                }

                candidates.Add(item with
                {
                    ReviewedAtUtc = reviewedAt,
                    CardId = addressesCard ? item.CardId : null
                });
            }

            if (candidates.Count == 0)
            {
                return Result(accepted, alreadyApplied, rejected);
            }

            var eventIds = candidates.Select(x => x.EventId).ToArray();
            var existingEventIds = await db.LearningCardReviews
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

            var requestedCardIds = remaining
                .Where(x => x.CardId is not null)
                .Select(x => x.CardId!.Value)
                .Distinct()
                .ToArray();
            var activeCards = requestedCardIds.Length == 0
                ? new HashSet<Guid>()
                : (await db.LearningCards
                    .AsNoTracking()
                    .Where(x =>
                        x.ProfileId == profileId
                        && x.State == UserTermState.Learning
                        && requestedCardIds.Contains(x.Id))
                    .Select(x => x.Id)
                    .ToListAsync(cancellationToken))
                    .ToHashSet();

            var requestedTermIds = remaining
                .Where(x => x.CardId is null)
                .Select(x => x.TermId!.Value)
                .Distinct()
                .ToArray();
            var termCards = requestedTermIds.Length == 0
                ? new Dictionary<Guid, Guid>()
                : await LearningQueries.TermStates(db, profileId)
                    .Where(x =>
                        x.State == UserTermState.Learning
                        && requestedTermIds.Contains(x.TermId))
                    .ToDictionaryAsync(x => x.TermId, x => x.CardId, cancellationToken);

            var toApply = new List<(OfflineReviewEvent Event, Guid CardId)>();
            foreach (var item in remaining)
            {
                if (item.CardId is { } requestedCardId)
                {
                    if (activeCards.Contains(requestedCardId))
                    {
                        toApply.Add((item, requestedCardId));
                        continue;
                    }
                }
                else if (termCards.TryGetValue(item.TermId!.Value, out var termCardId))
                {
                    toApply.Add((item, termCardId));
                    continue;
                }

                rejected.Add(item.EventId);
            }

            foreach (var (item, cardId) in toApply
                         .OrderBy(x => x.Event.ReviewedAtUtc)
                         .ThenBy(x => x.Event.EventId))
            {
                db.LearningCardReviews.Add(new LearningCardReview
                {
                    ProfileId = profileId,
                    CardId = cardId,
                    Rating = item.Rating,
                    ClientEventId = item.EventId,
                    ReviewedAt = item.ReviewedAtUtc,
                    NextReviewAt = item.ReviewedAtUtc
                });
                accepted.Add(item.EventId);
            }

            if (toApply.Count > 0)
            {
                await db.SaveChangesAsync(cancellationToken);

                var preferences = await GetPreferencesAsync(cancellationToken);
                foreach (var cardId in toApply.Select(x => x.CardId).Distinct())
                {
                    await RebuildCardScheduleAsync(
                        cardId,
                        preferences.DesiredRetention,
                        cancellationToken);
                }

                await db.SaveChangesAsync(cancellationToken);
            }

            return Result(accepted, alreadyApplied, rejected);
        }
        finally
        {
            OfflineSyncGate.Release();
        }
    }

    private async Task SetUnitStateAsync(
        LearningCourse course,
        Guid unitId,
        UserTermState state,
        CancellationToken cancellationToken)
    {
        var cards = await courses.EnsureCardsAsync(course, [unitId], cancellationToken);
        var queuePosition = await courses.CurrentMaxQueuePositionAsync(profileId, cancellationToken);
        var now = DateTime.UtcNow;

        foreach (var card in cards.OrderBy(x => x.Mode))
        {
            LearningCardTransitions.Apply(card, state, now, ref queuePosition);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task QueueAsync(
        IReadOnlyList<(LearningCourse Course, Guid UnitId)> targets,
        CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
        {
            return;
        }

        var cards = new List<LearningCard>();
        foreach (var group in targets.GroupBy(x => x.Course.Id))
        {
            cards.AddRange(await courses.EnsureCardsAsync(
                group.First().Course,
                group.Select(x => x.UnitId).Distinct().ToArray(),
                cancellationToken));
        }

        var cardsByTarget = cards.ToLookup(x => (x.CourseId, x.UnitId));
        var queuePosition = await courses.CurrentMaxQueuePositionAsync(profileId, cancellationToken);
        var now = DateTime.UtcNow;

        foreach (var (course, unitId) in targets)
        {
            foreach (var card in cardsByTarget[(course.Id, unitId)].OrderBy(x => x.Mode))
            {
                LearningCardTransitions.Queue(card, now, ref queuePosition);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<DueReviewItem>> DescribeAsync(
        IReadOnlyList<LearningCard> cards,
        CancellationToken cancellationToken)
    {
        if (cards.Count == 0)
        {
            return [];
        }

        var unitIds = cards.Select(x => x.UnitId).Distinct().ToArray();
        var termIds = await db.LearningUnits
            .AsNoTracking()
            .Where(x => unitIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.TermId, cancellationToken);
        var variants = (await db.LearningVariants
                .AsNoTracking()
                .Where(x => unitIds.Contains(x.UnitId))
                .ToListAsync(cancellationToken))
            .ToLookup(x => x.UnitId);

        return cards
            .Select(card =>
            {
                var prompt = PickVariant(variants[card.UnitId], card.PromptLanguage);
                var answer = PickVariant(variants[card.UnitId], card.AnswerLanguage);

                return new DueReviewItem(
                    card.Id,
                    card.UnitId,
                    termIds.GetValueOrDefault(card.UnitId),
                    card.Mode,
                    card.PromptLanguage,
                    card.AnswerLanguage,
                    prompt?.Text ?? "",
                    prompt?.Reading,
                    answer?.Text,
                    answer?.Reading,
                    card.IntervalDays);
            })
            .ToList();
    }

    private static LearningVariant? PickVariant(
        IEnumerable<LearningVariant> variants,
        string languageTag) =>
        variants
            .Where(x => x.LanguageTag == languageTag)
            .OrderBy(x => x.Role == LearningVariantRole.Primary ? 0 : 1)
            .ThenBy(x => x.CreatedAt)
            .ThenBy(x => x.Text, StringComparer.Ordinal)
            .FirstOrDefault();

    private async Task<Dictionary<Guid, ReviewAnimeContext>> LoadTermContextsAsync(
        IReadOnlyCollection<Guid> termIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, ReviewAnimeContext>();
        if (termIds.Count == 0)
        {
            return result;
        }

        var termLanguages = await db.Terms
            .AsNoTracking()
            .Where(x => termIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Language, cancellationToken);

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
        var languages = termLanguages.Values.Distinct().ToArray();

        var trackRows = episodeIds.Length == 0
            ? []
            : await db.SubtitleTracks
                .AsNoTracking()
                .Where(x => episodeIds.Contains(x.EpisodeId) && languages.Contains(x.Language))
                .OrderByDescending(x => x.ImportedAt)
                .ThenBy(x => x.Id)
                .Select(x => new ReviewTrackCandidate(x.EpisodeId, x.Language, x.Id))
                .ToListAsync(cancellationToken);

        var trackByEpisode = trackRows
            .GroupBy(x => (x.EpisodeId, x.Language))
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

        foreach (var (termId, selected) in selectedContexts)
        {
            if (termLanguages.TryGetValue(termId, out var language)
                && trackByEpisode.TryGetValue((selected.EpisodeId, language), out var trackId)
                && cueByKey.TryGetValue((trackId, selected.CueStartMs), out var sentence)
                && !string.IsNullOrWhiteSpace(sentence))
            {
                result[termId] = new ReviewAnimeContext(
                    selected.EpisodeId,
                    selected.AnimeTitle,
                    selected.SeasonNumber,
                    selected.EpisodeNumber,
                    selected.EpisodeTitle,
                    selected.CueStartMs,
                    sentence);
            }
        }

        return result;
    }

    private async Task RebuildCardScheduleAsync(
        Guid cardId,
        double desiredRetention,
        CancellationToken cancellationToken)
    {
        var card = await db.LearningCards.SingleAsync(
            x => x.Id == cardId
                && x.ProfileId == profileId
                && x.State == UserTermState.Learning,
            cancellationToken);

        var reviews = await db.LearningCardReviews
            .Where(x => x.CardId == cardId)
            .OrderBy(x => x.ReviewedAt)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var history = new List<ReviewHistoryItem>(reviews.Count);
        ReviewSchedule? finalSchedule = null;

        foreach (var review in reviews)
        {
            var reviewedAt = AsUtcOffset(review.ReviewedAt);
            var schedule = scheduler.Schedule(
                cardId,
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

        card.IntervalDays = finalSchedule.IntervalDays;
        card.NextReviewAt = finalSchedule.NextReviewAt.UtcDateTime;
        card.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<IReadOnlyList<ReviewHistoryItem>> GetHistoryAsync(
        Guid cardId,
        CancellationToken cancellationToken)
    {
        var rows = await db.LearningCardReviews
            .AsNoTracking()
            .Where(x => x.CardId == cardId)
            .OrderBy(x => x.ReviewedAt)
            .Select(x => new { x.Rating, x.ReviewedAt })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new ReviewHistoryItem(x.Rating, AsUtcOffset(x.ReviewedAt)))
            .ToArray();
    }

    private static OfflineReviewSyncResult Result(
        List<Guid> accepted,
        List<Guid> alreadyApplied,
        List<Guid> rejected) =>
        new(
            accepted.Distinct().ToArray(),
            alreadyApplied.Distinct().ToArray(),
            rejected.Distinct().ToArray());

    private static DateTimeOffset AsUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

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

    private sealed record ReviewTrackCandidate(Guid EpisodeId, string Language, Guid TrackId);
    private sealed record ReviewCueCandidate(Guid TrackId, int StartMs, string Text);
}
