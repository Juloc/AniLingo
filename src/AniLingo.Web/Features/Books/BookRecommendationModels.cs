namespace AniLingo.Web.Features.Books;

/// <summary>
/// Catalog search used by the recommendation engine. A <c>null</c> query
/// requests the provider's popular/free browse list.
/// </summary>
public delegate Task<IReadOnlyList<BookCatalogItem>> BookRecommendationSearch(
    string? query,
    CancellationToken cancellationToken);

/// <summary>
/// A local library book as seen by the recommendation engine. Progress is
/// always the current profile's own reading state; other profiles' progress
/// is never loaded.
/// </summary>
public sealed record BookRecommendationLibraryBook(
    Guid WorkId,
    string Title,
    string? Author,
    string? CoverImageUrl,
    IReadOnlyList<string> Subjects,
    string? CatalogId,
    DateTime ImportedAt,
    BookRecommendationProgress? Progress);

/// <summary>
/// Profile-scoped reading position. <see cref="ChapterIndex"/> is the zero-based
/// position of the current chapter in reading order.
/// </summary>
public sealed record BookRecommendationProgress(
    int ChapterIndex,
    int ChapterCount,
    int PositionPermille,
    DateTime UpdatedAt)
{
    public const int MeaningfulPositionPermille = 50;
    public const int FinishedPositionPermille = 950;

    /// <summary>
    /// The profile has read past the opening of the book, so it is a useful
    /// recommendation seed rather than an accidental open.
    /// </summary>
    public bool IsMeaningful =>
        ChapterIndex > 0
        || PositionPermille >= MeaningfulPositionPermille;

    public bool IsFinished =>
        ChapterCount > 0
        && ChapterIndex >= ChapterCount - 1
        && PositionPermille >= FinishedPositionPermille;
}

public enum BookRecommendationSeedReason
{
    RecentReading,
    RecentlyAdded
}

public sealed record BookRecommendationSeed(
    BookRecommendationLibraryBook Book,
    BookRecommendationSeedReason Reason);

public enum BookRecommendationShelfKind
{
    BecauseYouRead,
    MoreByAuthor,
    SimilarSubjects,
    Popular
}

public sealed record BookRecommendationItem(
    BookCatalogItem Book,
    int Score,
    string Reason);

public sealed record BookRecommendationShelf(
    BookRecommendationShelfKind Kind,
    string Title,
    string Explanation,
    IReadOnlyList<BookRecommendationItem> Items);

public sealed record BookRecommendationResult(
    IReadOnlyList<BookRecommendationLibraryBook> ContinueReading,
    IReadOnlyList<BookRecommendationSeed> Seeds,
    IReadOnlyList<BookRecommendationShelf> Shelves,
    int ProviderSearchCount,
    int FailedProviderSearchCount)
{
    public static BookRecommendationResult Empty { get; } =
        new([], [], [], 0, 0);

    public bool HasProviderFailures => FailedProviderSearchCount > 0;
}

public sealed class BookRecommendationOptions
{
    public static BookRecommendationOptions Default { get; } = new();

    /// <summary>Maximum number of library books used as seeds.</summary>
    public int MaxSeeds { get; init; } = 3;

    public int MaxAuthorShelves { get; init; } = 2;

    public int MaxSubjectShelves { get; init; } = 2;

    /// <summary>
    /// Hard cap on distinct catalog searches per page load, including the
    /// popular fallback.
    /// </summary>
    public int MaxProviderSearches { get; init; } = 6;

    public int MaxConcurrentSearches { get; init; } = 3;

    public int MaxItemsPerShelf { get; init; } = 12;

    public int MaxContinueReading { get; init; } = 12;

    /// <summary>
    /// When the personalised shelves hold fewer items than this, the popular
    /// free-books shelf is added as a fallback.
    /// </summary>
    public int MinimumRecommendationsBeforeFallback { get; init; } = 8;

    /// <summary>
    /// Overall time budget for all catalog searches. Each provider inside a
    /// search additionally keeps its own shorter timeout.
    /// </summary>
    public TimeSpan SearchBudget { get; init; } = TimeSpan.FromSeconds(15);
}
