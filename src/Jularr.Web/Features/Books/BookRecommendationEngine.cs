using Jularr.Web.Features.Recommendations;

namespace Jularr.Web.Features.Books;

/// <summary>
/// Deterministic, explainable content-based Books recommendations.
/// Inputs are the local library plus the current profile's own reading
/// progress; candidates come from the existing catalog search providers.
/// There is no cross-profile signal, no learned model and no persisted state.
/// </summary>
/// <remarks>
/// Since #428 the scoring, normalization and de-duplication live in the shared, media-neutral
/// <see cref="RecommendationScoring"/> framework; this class keeps the Books-specific orchestration
/// (catalog searches, book shelves and the exact reason copy) and delegates every scoring decision to
/// that framework so Books and the cross-media engine can never diverge.
/// </remarks>
public static class BookRecommendationEngine
{
    public const int SameAuthorScore = RecommendationScoring.SameCreatorScore;
    public const int SubjectOverlapScore = RecommendationScoring.SubjectOverlapScore;
    public const int MaxCountedSubjectOverlap = RecommendationScoring.MaxCountedSubjectOverlap;

    private const string PopularQueryKey = "";

    /// <summary>Book-specific subject noise (catalog shelf tags, "fiction", …) dropped before matching.</summary>
    private static readonly HashSet<string> GenericSubjects = new(
        [
            "fiction",
            "general",
            "fiction general",
            "general fiction",
            "literature",
            "literary",
            "literary fiction",
            "english fiction",
            "english literature",
            "novel",
            "novels",
            "book",
            "books",
            "ebook",
            "ebooks",
            "e books",
            "accessible book",
            "protected daisy",
            "in library",
            "lending library",
            "large type books",
            "overdrive",
            "open library staff picks",
            "wikisource"
        ],
        StringComparer.Ordinal);

    private static readonly HashSet<string> SubjectStopTokens =
        RecommendationScoring.DefaultSubjectStopTokens.ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyList<BookRecommendationLibraryBook> SelectContinueReading(
        IReadOnlyList<BookRecommendationLibraryBook> library,
        int maxItems) =>
        library
            .Where(x => x.Progress is { IsFinished: false })
            .OrderByDescending(x => x.Progress!.UpdatedAt)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.WorkId)
            .Take(Math.Max(0, maxItems))
            .ToArray();

    /// <summary>
    /// Seeds are the profile's most recent meaningful reads first, then the
    /// most recently imported library books as a fallback. Only books with
    /// an author or a useful subject can act as seeds.
    /// </summary>
    public static IReadOnlyList<BookRecommendationSeed> SelectSeeds(
        IReadOnlyList<BookRecommendationLibraryBook> library,
        int maxSeeds)
    {
        if (maxSeeds <= 0)
        {
            return [];
        }

        var usable = library
            .Where(HasRecommendationSignal)
            .ToArray();

        var reading = usable
            .Where(x => x.Progress is { IsMeaningful: true })
            .OrderByDescending(x => x.Progress!.UpdatedAt)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.WorkId)
            .Select(x => new BookRecommendationSeed(
                x,
                BookRecommendationSeedReason.RecentReading))
            .ToArray();

        var readingIds = reading
            .Select(x => x.Book.WorkId)
            .ToHashSet();

        var recentlyAdded = usable
            .Where(x => !readingIds.Contains(x.WorkId))
            .OrderByDescending(x => x.ImportedAt)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.WorkId)
            .Select(x => new BookRecommendationSeed(
                x,
                BookRecommendationSeedReason.RecentlyAdded));

        return reading
            .Concat(recentlyAdded)
            .Take(maxSeeds)
            .ToArray();
    }

    public static async Task<BookRecommendationResult> BuildAsync(
        IReadOnlyList<BookRecommendationLibraryBook> library,
        BookRecommendationSearch search,
        BookRecommendationOptions options,
        CancellationToken cancellationToken)
    {
        var continueReading = SelectContinueReading(
            library,
            options.MaxContinueReading);
        var seeds = SelectSeeds(
            library,
            options.MaxSeeds);
        var profileSubjects = BuildSubjectProfile(seeds);
        var seedAuthors = seeds
            .Select(x => RecommendationScoring.CreatorTokens(x.Book.Author))
            .Where(x => x.Count > 0)
            .ToArray();
        var historyIsThin = seeds.Count < 2;

        var plan = PlanShelves(
            seeds,
            profileSubjects,
            historyIsThin,
            options);

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        budget.CancelAfter(options.SearchBudget);
        using var gate = new SemaphoreSlim(
            Math.Max(1, options.MaxConcurrentSearches));

        var results = await RunSearchesAsync(
            plan.Select(x => x.Query).Distinct(),
            search,
            gate,
            budget.Token,
            cancellationToken);

        var owned = library
            .Select(x => new RecommendationOwnedEntity(
                RecommendationScoring.Identity(x.Title, x.Author),
                x.CatalogId))
            .ToArray();
        var used = new RecommendationUsedSet();
        var shelves = new List<BookRecommendationShelf>();

        foreach (var planned in plan
                     .Where(x => x.Kind != BookRecommendationShelfKind.Popular)
                     .OrderBy(x => x.Kind)
                     .ThenBy(x => x.Order))
        {
            var shelf = BuildShelf(
                planned,
                results.GetValueOrDefault(planned.Query.Key),
                profileSubjects,
                seedAuthors,
                historyIsThin,
                owned,
                used,
                options);

            if (shelf is not null)
            {
                shelves.Add(shelf);
            }
        }

        var popularPlanned = plan.FirstOrDefault(x =>
            x.Kind == BookRecommendationShelfKind.Popular);
        var recommendedCount = shelves.Sum(x => x.Items.Count);

        if (popularPlanned is null
            && recommendedCount < options.MinimumRecommendationsBeforeFallback
            && results.Count < options.MaxProviderSearches
            && !budget.IsCancellationRequested)
        {
            popularPlanned = PopularShelf(plan.Count);
            var fallback = await RunSearchesAsync(
                [popularPlanned.Query],
                search,
                gate,
                budget.Token,
                cancellationToken);

            foreach (var pair in fallback)
            {
                results[pair.Key] = pair.Value;
            }
        }

        if (popularPlanned is not null)
        {
            var shelf = BuildShelf(
                popularPlanned,
                results.GetValueOrDefault(popularPlanned.Query.Key),
                profileSubjects,
                seedAuthors,
                historyIsThin,
                owned,
                used,
                options);

            if (shelf is not null)
            {
                shelves.Add(shelf);
            }
        }

        return new BookRecommendationResult(
            continueReading,
            seeds,
            shelves,
            results.Count,
            results.Values.Count(x => x is null));
    }

    private static List<PlannedShelf> PlanShelves(
        IReadOnlyList<BookRecommendationSeed> seeds,
        IReadOnlyList<RecommendationSubject> profileSubjects,
        bool historyIsThin,
        BookRecommendationOptions options)
    {
        // Candidates are listed in budget priority order. A shelf whose
        // query would exceed the search budget is dropped; shelves that
        // reuse an already planned query cost nothing extra.
        var candidates = new List<PlannedShelf>();
        var seedQueryKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var seed in seeds)
        {
            var subjects = UsefulSubjects(seed.Book.Subjects);
            if (subjects.Count == 0)
            {
                continue;
            }

            var subject = subjects.FirstOrDefault(x =>
                    !seedQueryKeys.Contains(x.Key))
                ?? subjects[0];
            seedQueryKeys.Add(subject.Key);

            candidates.Add(new PlannedShelf(
                BookRecommendationShelfKind.BecauseYouRead,
                candidates.Count,
                new SearchQuery(subject.Key, subject.Display),
                seed,
                null,
                null));
        }

        var authorKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seed in seeds)
        {
            if (authorKeys.Count >= options.MaxAuthorShelves)
            {
                break;
            }

            var author = seed.Book.Author?.Trim();
            var tokens = RecommendationScoring.CreatorTokens(author);
            if (tokens.Count == 0
                || !authorKeys.Add(RecommendationScoring.CreatorKey(tokens)))
            {
                continue;
            }

            candidates.Add(new PlannedShelf(
                BookRecommendationShelfKind.MoreByAuthor,
                candidates.Count,
                new SearchQuery(RecommendationScoring.NormalizeForMatch(author!), author!),
                null,
                author,
                null));
        }

        if (historyIsThin)
        {
            candidates.Add(PopularShelf(candidates.Count));
        }

        foreach (var subject in profileSubjects
                     .Where(x => !seedQueryKeys.Contains(x.Key))
                     .Take(Math.Max(0, options.MaxSubjectShelves)))
        {
            candidates.Add(new PlannedShelf(
                BookRecommendationShelfKind.SimilarSubjects,
                candidates.Count,
                new SearchQuery(subject.Key, subject.Display),
                null,
                null,
                subject));
        }

        var plannedKeys = new HashSet<string>(StringComparer.Ordinal);
        var plan = new List<PlannedShelf>();

        foreach (var candidate in candidates)
        {
            if (!plannedKeys.Contains(candidate.Query.Key))
            {
                if (plannedKeys.Count >= options.MaxProviderSearches)
                {
                    continue;
                }

                plannedKeys.Add(candidate.Query.Key);
            }

            plan.Add(candidate);
        }

        return plan;
    }

    private static PlannedShelf PopularShelf(int order) =>
        new(
            BookRecommendationShelfKind.Popular,
            order,
            new SearchQuery(PopularQueryKey, null),
            null,
            null,
            null);

    private static async Task<Dictionary<string, IReadOnlyList<BookCatalogItem>?>> RunSearchesAsync(
        IEnumerable<SearchQuery> queries,
        BookRecommendationSearch search,
        SemaphoreSlim gate,
        CancellationToken budgetToken,
        CancellationToken requestToken)
    {
        var distinct = queries
            .DistinctBy(x => x.Key)
            .ToArray();

        var outcomes = await Task.WhenAll(
            distinct.Select(query => RunSearchAsync(
                query,
                search,
                gate,
                budgetToken,
                requestToken)));

        return outcomes.ToDictionary(
            x => x.Key,
            x => x.Items,
            StringComparer.Ordinal);
    }

    private static async Task<(string Key, IReadOnlyList<BookCatalogItem>? Items)> RunSearchAsync(
        SearchQuery query,
        BookRecommendationSearch search,
        SemaphoreSlim gate,
        CancellationToken budgetToken,
        CancellationToken requestToken)
    {
        try
        {
            await gate.WaitAsync(budgetToken);
        }
        catch (OperationCanceledException)
            when (!requestToken.IsCancellationRequested)
        {
            return (query.Key, null);
        }

        try
        {
            var items = await search(
                query.Text,
                budgetToken);
            return (query.Key, items ?? []);
        }
        catch (OperationCanceledException)
            when (requestToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Provider failures (timeouts, HTTP errors, malformed payloads)
            // only remove this shelf; the remaining shelves still render.
            return (query.Key, null);
        }
        finally
        {
            gate.Release();
        }
    }

    private static BookRecommendationShelf? BuildShelf(
        PlannedShelf planned,
        IReadOnlyList<BookCatalogItem>? candidates,
        IReadOnlyList<RecommendationSubject> profileSubjects,
        IReadOnlyList<IReadOnlySet<string>> seedAuthors,
        bool historyIsThin,
        IReadOnlyList<RecommendationOwnedEntity> owned,
        RecommendationUsedSet used,
        BookRecommendationOptions options)
    {
        if (candidates is null || candidates.Count == 0)
        {
            return null;
        }

        Func<BookCatalogItem, RecommendationSignal?> scorer;
        string title;
        string explanation;
        string fallbackReason;

        switch (planned.Kind)
        {
            case BookRecommendationShelfKind.BecauseYouRead:
                var seed = planned.Seed!;
                var seedSubjects = UsefulSubjects(seed.Book.Subjects);
                var seedAuthor = RecommendationScoring.CreatorTokens(seed.Book.Author);
                scorer = item => ScoreAgainstSeed(item, seedAuthor, seedSubjects);
                title = $"Because you read {seed.Book.Title}";
                explanation = seed.Reason == BookRecommendationSeedReason.RecentReading
                    ? "Matched to the author and subjects of a book you are reading on this profile."
                    : "Matched to the author and subjects of a book recently added to the library.";
                fallbackReason = "";
                break;

            case BookRecommendationShelfKind.MoreByAuthor:
                var author = RecommendationScoring.CreatorTokens(planned.Author);
                scorer = item => ScoreSameAuthor(item, author, profileSubjects);
                title = $"More by {planned.Author}";
                explanation = "Other books by an author from your recent books.";
                fallbackReason = "";
                break;

            case BookRecommendationShelfKind.SimilarSubjects:
                var subject = planned.Subject!;
                scorer = item => ScoreSimilarSubject(item, subject, profileSubjects, seedAuthors);
                title = $"Similar themes: {subject.Display}";
                explanation = "Books sharing subjects with your recent books.";
                fallbackReason = "";
                break;

            default:
                scorer = item => ScorePopular(item, profileSubjects);
                title = "Popular free books";
                explanation = historyIsThin
                    ? "A starting point while this profile has little reading history."
                    : "Popular free books to round out your suggestions.";
                fallbackReason = "Popular free book";
                break;
        }

        var ranked = RecommendationScoring.Rank(
            candidates,
            item => item.Id,
            item => item.Title,
            item => item.Author,
            scorer,
            owned,
            used,
            options.MaxItemsPerShelf);

        if (ranked.Count == 0)
        {
            return null;
        }

        var items = ranked
            .Select(entry => new BookRecommendationItem(
                entry.Item,
                entry.Signal.Score,
                FormatReason(entry.Signal, fallbackReason)))
            .ToArray();

        return new BookRecommendationShelf(
            planned.Kind,
            title,
            explanation,
            items);
    }

    /// <summary>The exact "Same author · Shares X, Y" reason copy Books has always shown.</summary>
    private static string FormatReason(RecommendationSignal signal, string fallbackReason)
    {
        var reasons = new List<string>(2);
        if (signal.SameCreator)
        {
            reasons.Add("Same author");
        }

        if (signal.SharedSubjects.Count > 0)
        {
            reasons.Add(
                "Shares "
                + string.Join(
                    ", ",
                    signal.SharedSubjects.Take(2).Select(x => x.Display)));
        }

        return reasons.Count == 0
            ? fallbackReason
            : string.Join(" · ", reasons);
    }

    private static RecommendationSignal? ScoreAgainstSeed(
        BookCatalogItem item,
        IReadOnlySet<string> seedAuthor,
        IReadOnlyList<RecommendationSubject> seedSubjects)
    {
        var sameAuthor = RecommendationScoring.CreatorsMatch(
            seedAuthor,
            RecommendationScoring.CreatorTokens(item.Author));
        var shared = RecommendationScoring.SharedSubjects(
            seedSubjects,
            UsefulSubjects(item.Subjects));

        return RecommendationScoring.Score(sameAuthor, shared, requireSignal: true);
    }

    private static RecommendationSignal? ScoreSameAuthor(
        BookCatalogItem item,
        IReadOnlySet<string> author,
        IReadOnlyList<RecommendationSubject> profileSubjects)
    {
        if (!RecommendationScoring.CreatorsMatch(author, RecommendationScoring.CreatorTokens(item.Author)))
        {
            return null;
        }

        return RecommendationScoring.Score(
            sameCreator: true,
            RecommendationScoring.SharedSubjects(profileSubjects, UsefulSubjects(item.Subjects)),
            requireSignal: true);
    }

    private static RecommendationSignal? ScoreSimilarSubject(
        BookCatalogItem item,
        RecommendationSubject subject,
        IReadOnlyList<RecommendationSubject> profileSubjects,
        IReadOnlyList<IReadOnlySet<string>> seedAuthors)
    {
        var shared = RecommendationScoring.SharedSubjects(
            profileSubjects,
            UsefulSubjects(item.Subjects));
        if (!shared.Any(x => x.Key == subject.Key))
        {
            return null;
        }

        var itemAuthor = RecommendationScoring.CreatorTokens(item.Author);
        var sameAuthor = seedAuthors.Any(x => RecommendationScoring.CreatorsMatch(x, itemAuthor));

        return RecommendationScoring.Score(sameAuthor, shared, requireSignal: true);
    }

    private static RecommendationSignal? ScorePopular(
        BookCatalogItem item,
        IReadOnlyList<RecommendationSubject> profileSubjects) =>
        RecommendationScoring.Score(
            sameCreator: false,
            RecommendationScoring.SharedSubjects(profileSubjects, UsefulSubjects(item.Subjects)),
            requireSignal: false);

    private static IReadOnlyList<RecommendationSubject> BuildSubjectProfile(
        IReadOnlyList<BookRecommendationSeed> seeds) =>
        RecommendationScoring.BuildSubjectProfile(
            seeds.Select(seed => UsefulSubjects(seed.Book.Subjects)));

    private static IReadOnlyList<RecommendationSubject> UsefulSubjects(
        IReadOnlyList<string> subjects) =>
        RecommendationScoring.UsefulSubjects(subjects, GenericSubjects, SubjectStopTokens);

    private static bool HasRecommendationSignal(
        BookRecommendationLibraryBook book) =>
        RecommendationScoring.CreatorTokens(book.Author).Count > 0
        || UsefulSubjects(book.Subjects).Count > 0;

    private sealed record SearchQuery(
        string Key,
        string? Text);

    private sealed record PlannedShelf(
        BookRecommendationShelfKind Kind,
        int Order,
        SearchQuery Query,
        BookRecommendationSeed? Seed,
        string? Author,
        RecommendationSubject? Subject);
}
