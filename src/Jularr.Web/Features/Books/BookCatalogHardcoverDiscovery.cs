using System.Text.Json;

namespace Jularr.Web.Features.Books;

/// <summary>
/// Optional Hardcover rating enrichment for Books discovery (#371), distinct from
/// <see cref="BookCatalogService.EnrichHardcoverStatesAsync"/> (a signed-in profile's personal
/// reading-list state). This uses the owner-configured discovery API key (Settings → Books,
/// <see cref="BookDiscoverySettings"/>) to attach a community rating to items Hardcover
/// confidently matches by title/author. Any Hardcover failure, timeout or schema mismatch leaves
/// items unchanged: Books discovery never depends on Hardcover being reachable or configured.
/// </summary>
public sealed partial class BookCatalogService
{
    private const int HardcoverRatingCandidateLimit = 5;
    private static readonly TimeSpan HardcoverRatingEnrichmentTimeout = TimeSpan.FromSeconds(6);

    private async Task<IReadOnlyList<BookCatalogItem>> EnrichHardcoverRatingsAsync(
        IReadOnlyList<BookCatalogItem> items,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return items;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HardcoverRatingEnrichmentTimeout);
        using var gate = new SemaphoreSlim(4, 4);

        var tasks = items.Select(async item =>
        {
            await gate.WaitAsync(timeout.Token);
            try
            {
                var rating = await FindHardcoverRatingAsync(
                    apiKey,
                    item.Title,
                    item.Author,
                    timeout.Token);

                return rating is null ? item : item with { Rating = rating };
            }
            catch (Exception exception) when (
                !cancellationToken.IsCancellationRequested
                && exception is HttpRequestException
                    or TaskCanceledException
                    or InvalidOperationException
                    or JsonException)
            {
                return item;
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        return await Task.WhenAll(tasks);
    }

    private async Task<double?> FindHardcoverRatingAsync(
        string apiKey,
        string title,
        string? author,
        CancellationToken cancellationToken)
    {
        var trimmedTitle = title.Trim();
        if (trimmedTitle.Length == 0)
        {
            return null;
        }

        using var document = await SendHardcoverAsync(
            apiKey,
            """
            query JularrBookRating($pattern: String!, $limit: Int!) {
              books(where: { title: { _ilike: $pattern } }, limit: $limit) {
                title
                rating
                cached_contributors
              }
            }
            """,
            new Dictionary<string, object>
            {
                ["pattern"] = "%" + trimmedTitle + "%",
                ["limit"] = HardcoverRatingCandidateLimit
            },
            cancellationToken);

        if (!document.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("books", out var books)
            || books.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var candidate in books.EnumerateArray())
        {
            if (candidate.ValueKind != JsonValueKind.Object
                || !candidate.TryGetProperty("title", out var titleNode)
                || titleNode.ValueKind != JsonValueKind.String
                || titleNode.GetString() is not { } candidateTitle
                || string.IsNullOrWhiteSpace(candidateTitle))
            {
                continue;
            }

            if (!BookWorkSearch.SameWork(
                    trimmedTitle,
                    author,
                    candidateTitle,
                    HardcoverContributorAuthor(candidate)))
            {
                continue;
            }

            if (candidate.TryGetProperty("rating", out var ratingNode)
                && ratingNode.ValueKind == JsonValueKind.Number
                && ratingNode.TryGetDouble(out var rating)
                && rating is > 0 and <= 5)
            {
                return Math.Round(rating, 2);
            }
        }

        return null;
    }

    /// <summary>The first contributor without an explicit non-author role, the same convention
    /// <see cref="ParseHardcoverBook"/> uses for a signed-in profile's personal list.</summary>
    private static string? HardcoverContributorAuthor(JsonElement book)
    {
        if (!book.TryGetProperty("cached_contributors", out var contributors)
            || contributors.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return contributors.EnumerateArray()
            .Where(contributor => contributor.ValueKind == JsonValueKind.Object)
            .OrderBy(contributor =>
                contributor.TryGetProperty("contribution", out var role)
                && role.ValueKind == JsonValueKind.String
                    ? 1
                    : 0)
            .Select(contributor =>
                contributor.TryGetProperty("author", out var authorNode)
                && authorNode.ValueKind == JsonValueKind.Object
                && authorNode.TryGetProperty("name", out var nameNode)
                && nameNode.ValueKind == JsonValueKind.String
                    ? nameNode.GetString()
                    : null)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
    }
}
