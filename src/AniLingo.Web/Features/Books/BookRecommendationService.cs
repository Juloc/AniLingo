using AniLingo.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Books;

/// <summary>
/// Builds the Books "For You" page. The local library and the current
/// profile's reading progress are read from SQLite; external catalog
/// searches happen only here, i.e. only when For You is explicitly opened.
/// </summary>
public sealed class BookRecommendationService(
    BookCatalogService books,
    AppDbContext db)
{
    public async Task<BookRecommendationResult> GetForProfileAsync(
        string profileId,
        string targetLanguage,
        CancellationToken cancellationToken,
        BookRecommendationOptions? options = null)
    {
        var library = await LoadLibraryAsync(
            profileId,
            targetLanguage,
            cancellationToken);

        return await BookRecommendationEngine.BuildAsync(
            library,
            books.SearchAsync,
            options ?? BookRecommendationOptions.Default,
            cancellationToken);
    }

    /// <summary>
    /// Loads the shared local library with only <paramref name="profileId"/>'s
    /// reading progress attached. No external request is made.
    /// </summary>
    public async Task<IReadOnlyList<BookRecommendationLibraryBook>> LoadLibraryAsync(
        string profileId,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var library = await books.GetLibraryAsync(
            profileId,
            targetLanguage,
            cancellationToken);

        if (library.Count == 0)
        {
            return [];
        }

        var workIds = library
            .Select(x => x.WorkId)
            .ToArray();

        var works = await db.NovelWorks
            .AsNoTracking()
            .Where(x => workIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.ImportedAt,
                x.MetadataExternalId
            })
            .ToDictionaryAsync(
                x => x.Id,
                cancellationToken);

        var readWorkIds = library
            .Where(x => x.CurrentChapterId is not null)
            .Select(x => x.WorkId)
            .ToArray();

        Dictionary<Guid, Guid[]> chapterOrder = readWorkIds.Length == 0
            ? []
            : (await db.NovelChapters
                    .AsNoTracking()
                    .Where(x => readWorkIds.Contains(x.WorkId))
                    .Select(x => new
                    {
                        x.Id,
                        x.WorkId,
                        x.Number
                    })
                    .ToListAsync(cancellationToken))
                .GroupBy(x => x.WorkId)
                .ToDictionary(
                    x => x.Key,
                    x => x
                        .OrderBy(chapter => chapter.Number)
                        .Select(chapter => chapter.Id)
                        .ToArray());

        return library
            .Select(book =>
            {
                works.TryGetValue(book.WorkId, out var work);

                return new BookRecommendationLibraryBook(
                    book.WorkId,
                    book.Title,
                    book.Author,
                    book.CoverImageUrl,
                    book.Subjects,
                    work?.MetadataExternalId,
                    work?.ImportedAt ?? DateTime.MinValue,
                    ToProgress(book, chapterOrder));
            })
            .ToArray();
    }

    private static BookRecommendationProgress? ToProgress(
        BookLibraryItem book,
        IReadOnlyDictionary<Guid, Guid[]> chapterOrder)
    {
        if (book.CurrentChapterId is not Guid chapterId
            || book.LastReadAt is not DateTime updatedAt)
        {
            return null;
        }

        var chapters = chapterOrder.GetValueOrDefault(book.WorkId) ?? [];
        var index = Array.IndexOf(chapters, chapterId);

        return new BookRecommendationProgress(
            Math.Max(0, index),
            chapters.Length,
            book.ProgressPermille,
            updatedAt);
    }
}
