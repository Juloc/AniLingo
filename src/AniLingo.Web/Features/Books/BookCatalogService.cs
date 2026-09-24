using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Books;

public sealed record BookCatalogItem(
    int Id,
    string Title,
    string? Author,
    string? Summary,
    string? CoverImageUrl,
    IReadOnlyList<string> Subjects,
    int DownloadCount,
    string? TextUrl)
{
    public bool CanRead => !string.IsNullOrWhiteSpace(TextUrl);
}

public sealed class BookCatalogService(HttpClient httpClient)
{
    private const int SearchLimit = 24;
    private const int DefaultSampleCharacters = 5500;

    public async Task<IReadOnlyList<BookCatalogItem>> SearchAsync(
        string? query,
        CancellationToken cancellationToken)
    {
        var path = "books?languages=en&sort=popular";
        if (!string.IsNullOrWhiteSpace(query))
        {
            path += "&search=" + Uri.EscapeDataString(query.Trim());
        }

        var response = await httpClient.GetFromJsonAsync<GutendexListResponse>(
            path,
            cancellationToken)
            ?? throw new InvalidOperationException("The book catalog returned no data.");

        return response.Results
            .Select(Map)
            .Where(x => x.CanRead)
            .Take(SearchLimit)
            .ToArray();
    }

    public async Task<BookCatalogItem?> GetAsync(
        int id,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            $"books/{id}",
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var book = await response.Content.ReadFromJsonAsync<GutendexBook>(
            cancellationToken: cancellationToken);

        return book is null ? null : Map(book);
    }

    public async Task<string> GetReadableSampleAsync(
        BookCatalogItem book,
        CancellationToken cancellationToken,
        int maxCharacters = DefaultSampleCharacters)
    {
        if (string.IsNullOrWhiteSpace(book.TextUrl))
        {
            throw new InvalidOperationException(
                "This catalog entry does not provide a readable plain-text edition.");
        }

        var raw = await httpClient.GetStringAsync(
            new Uri(book.TextUrl, UriKind.Absolute),
            cancellationToken);

        return ExtractReadableSample(raw, maxCharacters);
    }

    public static string ExtractReadableSample(
        string rawText,
        int maxCharacters = DefaultSampleCharacters)
    {
        if (maxCharacters < 500)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        }

        var text = rawText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var startMarker = text.IndexOf(
            "*** START OF",
            StringComparison.OrdinalIgnoreCase);
        if (startMarker >= 0)
        {
            var contentStart = text.IndexOf('\n', startMarker);
            if (contentStart >= 0)
            {
                text = text[(contentStart + 1)..];
            }
        }

        var endMarker = text.IndexOf(
            "*** END OF",
            StringComparison.OrdinalIgnoreCase);
        if (endMarker >= 0)
        {
            text = text[..endMarker];
        }

        text = text.Trim();

        var chapterMatches = Regex.Matches(
            text,
            @"(?im)^[ \t]*chapter\s+(?:[ivxlcdm]+|\d+|one|two|three|four|five|six|seven|eight|nine|ten)\b[^\n]*");

        for (var index = 0; index < chapterMatches.Count; index++)
        {
            var current = chapterMatches[index];
            var nextIndex = index + 1 < chapterMatches.Count
                ? chapterMatches[index + 1].Index
                : text.Length;

            // A dense run of chapter headings is normally a table of contents.
            if (nextIndex - current.Index >= 900)
            {
                text = text[current.Index..].TrimStart();
                break;
            }
        }

        if (text.Length <= maxCharacters)
        {
            return text;
        }

        var candidate = text[..maxCharacters];
        var paragraphBreak = candidate.LastIndexOf(
            "\n\n",
            StringComparison.Ordinal);

        if (paragraphBreak >= maxCharacters / 2)
        {
            candidate = candidate[..paragraphBreak];
        }
        else
        {
            var sentenceBreak = candidate.LastIndexOfAny(['.', '!', '?']);
            if (sentenceBreak >= maxCharacters / 2)
            {
                candidate = candidate[..(sentenceBreak + 1)];
            }
        }

        return candidate.Trim();
    }

    private static BookCatalogItem Map(GutendexBook book)
    {
        var author = string.Join(
            ", ",
            book.Authors
                .Select(x => x.Name?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x)));

        var cover = book.Formats
            .Where(x => x.Key.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Key.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

        var textUrl = book.Formats
            .Where(x => x.Key.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Key.Contains("utf-8", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

        var summary = book.Summaries
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            ?.Trim();

        return new BookCatalogItem(
            book.Id,
            book.Title.Trim(),
            string.IsNullOrWhiteSpace(author) ? null : author,
            summary,
            cover,
            book.Subjects
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Take(8)
                .ToArray(),
            book.DownloadCount,
            textUrl);
    }

    private sealed record GutendexListResponse(
        int Count,
        GutendexBook[] Results);

    private sealed record GutendexBook(
        int Id,
        string Title,
        string[] Subjects,
        GutendexPerson[] Authors,
        string[] Summaries,
        Dictionary<string, string> Formats,
        [property: JsonPropertyName("download_count")] int DownloadCount);

    private sealed record GutendexPerson(string? Name);
}
