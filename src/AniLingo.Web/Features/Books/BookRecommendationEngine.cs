using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Books;

/// <summary>
/// Deterministic, explainable content-based Books recommendations.
/// Inputs are the local library plus the current profile's own reading
/// progress; candidates come from the existing catalog search providers.
/// There is no cross-profile signal, no learned model and no persisted state.
/// </summary>
public static partial class BookRecommendationEngine
{
    public const int SameAuthorScore = 100;
    public const int SubjectOverlapScore = 15;
    public const int MaxCountedSubjectOverlap = 4;

    private const string PopularQueryKey = "";

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

    private static readonly HashSet<string> SubjectStopTokens = new(
        [
            "fiction",
            "general",
            "literature",
            "novel",
            "novels",
            "book",
            "books",
            "and",
            "the",
            "of",
            "in",
            "a",
            "an"
        ],
        StringComparer.Ordinal);

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
            .Select(x => AuthorTokens(x.Book.Author))
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
            .Select(x => new OwnedBook(
                Identity(x.Title, x.Author),
                x.CatalogId))
            .ToArray();
        var used = new UsedBooks();
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
        IReadOnlyList<ProfileSubject> profileSubjects,
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
            var tokens = AuthorTokens(author);
            if (tokens.Count == 0
                || !authorKeys.Add(AuthorKey(tokens)))
            {
                continue;
            }

            candidates.Add(new PlannedShelf(
                BookRecommendationShelfKind.MoreByAuthor,
                candidates.Count,
                new SearchQuery(NormalizeForMatch(author!), author!),
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
        IReadOnlyList<ProfileSubject> profileSubjects,
        IReadOnlyList<IReadOnlySet<string>> seedAuthors,
        bool historyIsThin,
        IReadOnlyList<OwnedBook> owned,
        UsedBooks used,
        BookRecommendationOptions options)
    {
        if (candidates is null || candidates.Count == 0)
        {
            return null;
        }

        Func<BookCatalogItem, ScoredReason?> scorer;
        string title;
        string explanation;

        switch (planned.Kind)
        {
            case BookRecommendationShelfKind.BecauseYouRead:
                var seed = planned.Seed!;
                var seedSubjects = UsefulSubjects(seed.Book.Subjects);
                var seedAuthor = AuthorTokens(seed.Book.Author);
                scorer = item => ScoreAgainstSeed(
                    item,
                    seedAuthor,
                    seedSubjects);
                title = $"Because you read {seed.Book.Title}";
                explanation = seed.Reason == BookRecommendationSeedReason.RecentReading
                    ? "Matched to the author and subjects of a book you are reading on this profile."
                    : "Matched to the author and subjects of a book recently added to the library.";
                break;

            case BookRecommendationShelfKind.MoreByAuthor:
                var author = AuthorTokens(planned.Author);
                scorer = item => ScoreSameAuthor(
                    item,
                    author,
                    profileSubjects);
                title = $"More by {planned.Author}";
                explanation = "Other books by an author from your recent books.";
                break;

            case BookRecommendationShelfKind.SimilarSubjects:
                var subject = planned.Subject!;
                scorer = item => ScoreSimilarSubject(
                    item,
                    subject,
                    profileSubjects,
                    seedAuthors);
                title = $"Similar themes: {subject.Display}";
                explanation = "Books sharing subjects with your recent books.";
                break;

            default:
                scorer = item => ScorePopular(
                    item,
                    profileSubjects);
                title = "Popular free books";
                explanation = historyIsThin
                    ? "A starting point while this profile has little reading history."
                    : "Popular free books to round out your suggestions.";
                break;
        }

        var items = Rank(
            candidates,
            scorer,
            owned,
            used,
            options.MaxItemsPerShelf);

        return items.Count == 0
            ? null
            : new BookRecommendationShelf(
                planned.Kind,
                title,
                explanation,
                items);
    }

    private static IReadOnlyList<BookRecommendationItem> Rank(
        IReadOnlyList<BookCatalogItem> candidates,
        Func<BookCatalogItem, ScoredReason?> scorer,
        IReadOnlyList<OwnedBook> owned,
        UsedBooks used,
        int maxItems)
    {
        var ordered = candidates
            .Select((item, rank) => (
                Item: item,
                Rank: rank,
                Identity: Identity(item.Title, item.Author),
                Scored: scorer(item)))
            .Where(x =>
                x.Scored is not null
                && !IsOwned(x.Item, x.Identity, owned))
            .OrderByDescending(x => x.Scored!.Score)
            .ThenBy(x => x.Rank)
            .ThenBy(x => x.Item.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Item.Id, StringComparer.Ordinal);

        var picked = new List<BookRecommendationItem>();
        foreach (var candidate in ordered)
        {
            if (picked.Count >= maxItems)
            {
                break;
            }

            if (!used.TryAdd(candidate.Item.Id, candidate.Identity))
            {
                continue;
            }

            picked.Add(new BookRecommendationItem(
                candidate.Item,
                candidate.Scored!.Score,
                candidate.Scored.Reason));
        }

        return picked;
    }

    private static ScoredReason? ScoreAgainstSeed(
        BookCatalogItem item,
        IReadOnlySet<string> seedAuthor,
        IReadOnlyList<ProfileSubject> seedSubjects)
    {
        var sameAuthor = AuthorsMatch(
            seedAuthor,
            AuthorTokens(item.Author));
        var shared = SharedSubjects(
            seedSubjects,
            item.Subjects);

        return Score(
            sameAuthor,
            shared,
            requireSignal: true,
            fallbackReason: "");
    }

    private static ScoredReason? ScoreSameAuthor(
        BookCatalogItem item,
        IReadOnlySet<string> author,
        IReadOnlyList<ProfileSubject> profileSubjects)
    {
        if (!AuthorsMatch(author, AuthorTokens(item.Author)))
        {
            return null;
        }

        return Score(
            sameAuthor: true,
            SharedSubjects(profileSubjects, item.Subjects),
            requireSignal: true,
            fallbackReason: "");
    }

    private static ScoredReason? ScoreSimilarSubject(
        BookCatalogItem item,
        ProfileSubject subject,
        IReadOnlyList<ProfileSubject> profileSubjects,
        IReadOnlyList<IReadOnlySet<string>> seedAuthors)
    {
        var shared = SharedSubjects(
            profileSubjects,
            item.Subjects);
        if (!shared.Any(x => x.Key == subject.Key))
        {
            return null;
        }

        var itemAuthor = AuthorTokens(item.Author);
        var sameAuthor = seedAuthors.Any(x => AuthorsMatch(x, itemAuthor));

        return Score(
            sameAuthor,
            shared,
            requireSignal: true,
            fallbackReason: "");
    }

    private static ScoredReason? ScorePopular(
        BookCatalogItem item,
        IReadOnlyList<ProfileSubject> profileSubjects) =>
        Score(
            sameAuthor: false,
            SharedSubjects(profileSubjects, item.Subjects),
            requireSignal: false,
            fallbackReason: "Popular free book");

    private static ScoredReason? Score(
        bool sameAuthor,
        IReadOnlyList<ProfileSubject> shared,
        bool requireSignal,
        string fallbackReason)
    {
        var score =
            (sameAuthor ? SameAuthorScore : 0)
            + Math.Min(shared.Count, MaxCountedSubjectOverlap)
                * SubjectOverlapScore;

        if (requireSignal && score == 0)
        {
            return null;
        }

        var reasons = new List<string>(2);
        if (sameAuthor)
        {
            reasons.Add("Same author");
        }

        if (shared.Count > 0)
        {
            reasons.Add(
                "Shares "
                + string.Join(
                    ", ",
                    shared.Take(2).Select(x => x.Display)));
        }

        return new ScoredReason(
            score,
            reasons.Count == 0
                ? fallbackReason
                : string.Join(" · ", reasons));
    }

    /// <summary>
    /// Returns the profile subjects (in profile order) matched by any of the
    /// candidate's subjects.
    /// </summary>
    private static IReadOnlyList<ProfileSubject> SharedSubjects(
        IReadOnlyList<ProfileSubject> profile,
        IReadOnlyList<string> candidateSubjects)
    {
        if (profile.Count == 0 || candidateSubjects.Count == 0)
        {
            return [];
        }

        var candidates = UsefulSubjects(candidateSubjects);
        return profile
            .Where(subject => candidates.Any(candidate =>
                SubjectsMatch(subject.Tokens, candidate.Tokens)))
            .ToArray();
    }

    private static IReadOnlyList<ProfileSubject> BuildSubjectProfile(
        IReadOnlyList<BookRecommendationSeed> seeds)
    {
        var weights = new Dictionary<string, (ProfileSubject Subject, int Weight, int FirstSeen)>(
            StringComparer.Ordinal);
        var position = 0;

        foreach (var seed in seeds)
        {
            foreach (var subject in UsefulSubjects(seed.Book.Subjects))
            {
                weights[subject.Key] = weights.TryGetValue(
                    subject.Key,
                    out var current)
                    ? current with { Weight = current.Weight + 1 }
                    : (subject, 1, position++);
            }
        }

        return weights.Values
            .OrderByDescending(x => x.Weight)
            .ThenBy(x => x.FirstSeen)
            .Select(x => x.Subject)
            .ToArray();
    }

    private static IReadOnlyList<ProfileSubject> UsefulSubjects(
        IReadOnlyList<string> subjects)
    {
        var result = new List<ProfileSubject>();
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in subjects)
        {
            var display = raw?.Trim();
            if (string.IsNullOrWhiteSpace(display)
                || display.Length > 80
                || display.Contains(':')
                || display.Contains('='))
            {
                continue;
            }

            var key = NormalizeForMatch(display);
            if (key.Length < 3
                || GenericSubjects.Contains(key)
                || key.StartsWith("reading level", StringComparison.Ordinal)
                || !keys.Add(key))
            {
                continue;
            }

            var tokens = key
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(x => x.Length >= 2 && !SubjectStopTokens.Contains(x))
                .ToHashSet(StringComparer.Ordinal);

            if (tokens.Count > 0)
            {
                result.Add(new ProfileSubject(key, display, tokens));
            }
        }

        return result;
    }

    private static bool SubjectsMatch(
        IReadOnlySet<string> left,
        IReadOnlySet<string> right) =>
        left.Count > 0
        && right.Count > 0
        && (left.IsSubsetOf(right) || right.IsSubsetOf(left));

    private static bool HasRecommendationSignal(
        BookRecommendationLibraryBook book) =>
        AuthorTokens(book.Author).Count > 0
        || UsefulSubjects(book.Subjects).Count > 0;

    private static IReadOnlySet<string> AuthorTokens(string? author)
    {
        if (string.IsNullOrWhiteSpace(author))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return NormalizeForMatch(author)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Length >= 2 && !x.All(char.IsDigit))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string AuthorKey(IReadOnlySet<string> tokens) =>
        string.Join(
            " ",
            tokens.OrderBy(x => x, StringComparer.Ordinal));

    private static bool AuthorsMatch(
        IReadOnlySet<string> left,
        IReadOnlySet<string> right) =>
        left.Count > 0
        && right.Count > 0
        && (left.IsSubsetOf(right) || right.IsSubsetOf(left));

    private static BookIdentity Identity(
        string title,
        string? author) =>
        new(
            TitleKey(title),
            AuthorTokens(author));

    private static string TitleKey(string title)
    {
        var main = title;
        var cut = main.IndexOfAny([':', '(', ';', '[']);
        if (cut > 0)
        {
            main = main[..cut];
        }

        var key = NormalizeForMatch(main);
        foreach (var article in (string[])["the ", "a ", "an "])
        {
            if (key.StartsWith(article, StringComparison.Ordinal)
                && key.Length > article.Length)
            {
                return key[article.Length..];
            }
        }

        return key;
    }

    /// <summary>
    /// Same normalized main title and compatible authors. A missing author
    /// on either side is treated as compatible so provider records without
    /// author metadata cannot slip past duplicate/owned suppression.
    /// </summary>
    private static bool SameBook(
        BookIdentity left,
        BookIdentity right) =>
        left.TitleKey.Length > 0
        && left.TitleKey == right.TitleKey
        && (left.AuthorTokens.Count == 0
            || right.AuthorTokens.Count == 0
            || AuthorsMatch(left.AuthorTokens, right.AuthorTokens));

    private static bool IsOwned(
        BookCatalogItem item,
        BookIdentity identity,
        IReadOnlyList<OwnedBook> owned) =>
        owned.Any(book =>
            (!string.IsNullOrWhiteSpace(book.CatalogId)
                && string.Equals(
                    book.CatalogId,
                    item.Id,
                    StringComparison.OrdinalIgnoreCase))
            || SameBook(book.Identity, identity));

    private static string NormalizeForMatch(string value) =>
        NonAlphanumeric()
            .Replace(value.ToLowerInvariant(), " ")
            .Trim();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonAlphanumeric();

    private sealed record SearchQuery(
        string Key,
        string? Text);

    private sealed record PlannedShelf(
        BookRecommendationShelfKind Kind,
        int Order,
        SearchQuery Query,
        BookRecommendationSeed? Seed,
        string? Author,
        ProfileSubject? Subject);

    private sealed record ProfileSubject(
        string Key,
        string Display,
        IReadOnlySet<string> Tokens);

    private sealed record ScoredReason(
        int Score,
        string Reason);

    private sealed record BookIdentity(
        string TitleKey,
        IReadOnlySet<string> AuthorTokens);

    private sealed record OwnedBook(
        BookIdentity Identity,
        string? CatalogId);

    /// <summary>
    /// Candidates already shown on an earlier shelf, used for cross-shelf and
    /// in-shelf duplicate suppression.
    /// </summary>
    private sealed class UsedBooks
    {
        private readonly HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<BookIdentity> identities = [];

        public bool TryAdd(
            string id,
            BookIdentity identity)
        {
            if (ids.Contains(id)
                || identities.Any(x => SameBook(x, identity)))
            {
                return false;
            }

            ids.Add(id);
            identities.Add(identity);
            return true;
        }
    }
}
