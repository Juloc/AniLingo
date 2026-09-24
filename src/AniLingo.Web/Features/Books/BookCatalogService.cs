using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Books;

public sealed record BookCatalogItem(
    string Id,
    string Title,
    string? Author,
    string? Summary,
    string? CoverImageUrl,
    IReadOnlyList<string> Subjects,
    int? FirstPublishYear,
    string? TextUrl,
    string SourceUrl,
    string SourceName,
    string? TextSourceName)
{
    public bool CanRead => !string.IsNullOrWhiteSpace(TextUrl);
}

public sealed class BookCatalogService(HttpClient httpClient)
{
    private const int SearchLimit = 24;
    private const int DefaultSampleCharacters = 5500;
    private const int MaxRedirects = 5;
    private static readonly TimeSpan CatalogRequestTimeout = TimeSpan.FromSeconds(12);

    public async Task<IReadOnlyList<BookCatalogItem>> SearchAsync(
        string? query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return await SearchGutenbergAsync(null, cancellationToken);
        }

        var normalizedQuery = query.Trim();

        try
        {
            var openLibrary = await SearchOpenLibraryAsync(
                normalizedQuery,
                cancellationToken);

            if (openLibrary.Count > 0)
            {
                return openLibrary;
            }
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException)
        {
            // Fall back to the smaller Gutenberg catalog when Open Library
            // is temporarily unavailable. The caller still gets useful results
            // for public-domain books instead of a hard failure.
        }

        return await SearchGutenbergAsync(normalizedQuery, cancellationToken);
    }

    public async Task<BookCatalogItem?> GetAsync(
        string id,
        CancellationToken cancellationToken)
    {
        if (TryParseGutenbergId(id, out var gutenbergId))
        {
            return await GetGutenbergAsync(gutenbergId, cancellationToken);
        }

        if (TryParseOpenLibraryId(id, out var workKey))
        {
            return await GetOpenLibraryAsync(workKey, cancellationToken);
        }

        return null;
    }

    public async Task<string> GetReadableSampleAsync(
        BookCatalogItem book,
        CancellationToken cancellationToken,
        int maxCharacters = DefaultSampleCharacters)
    {
        if (string.IsNullOrWhiteSpace(book.TextUrl))
        {
            throw new InvalidOperationException(
                "No readable text source is available for this book yet.");
        }

        var raw = await GetTextFollowingRedirectsAsync(
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

    private async Task<IReadOnlyList<BookCatalogItem>> SearchOpenLibraryAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            "https://openlibrary.org/search.json"
            + "?q=" + Uri.EscapeDataString(query)
            + "&fields=key,title,author_name,cover_i,first_publish_year,subject"
            + $"&limit={SearchLimit}");

        var response = await GetJsonAsync<OpenLibrarySearchResponse>(
            uri,
            cancellationToken)
            ?? throw new InvalidOperationException("Open Library returned no data.");

        return response.Docs
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.Key)
                && !string.IsNullOrWhiteSpace(x.Title)
                && x.Key.StartsWith("/works/", StringComparison.Ordinal))
            .Take(SearchLimit)
            .Select(MapOpenLibrarySearch)
            .ToArray();
    }

    private async Task<IReadOnlyList<BookCatalogItem>> SearchGutenbergAsync(
        string? query,
        CancellationToken cancellationToken)
    {
        var path = "books?languages=en&sort=popular";
        if (!string.IsNullOrWhiteSpace(query))
        {
            path += "&search=" + Uri.EscapeDataString(query.Trim());
        }

        var response = await GetJsonAsync<GutendexListResponse>(
            new Uri(httpClient.BaseAddress!, path),
            cancellationToken)
            ?? throw new InvalidOperationException("Project Gutenberg catalog returned no data.");

        return response.Results
            .Select(MapGutenberg)
            .Where(x => x.CanRead)
            .Take(SearchLimit)
            .ToArray();
    }

    private async Task<BookCatalogItem?> GetGutenbergAsync(
        int id,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsyncWithTimeout(
            new HttpRequestMessage(HttpMethod.Get, $"books/{id}"),
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var book = await response.Content.ReadFromJsonAsync<GutendexBook>(
            cancellationToken: cancellationToken);

        return book is null ? null : MapGutenberg(book);
    }

    private async Task<BookCatalogItem?> GetOpenLibraryAsync(
        string workKey,
        CancellationToken cancellationToken)
    {
        var workUri = new Uri($"https://openlibrary.org/works/{Uri.EscapeDataString(workKey)}.json");

        using var response = await SendAsyncWithTimeout(
            new HttpRequestMessage(HttpMethod.Get, workUri),
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var work = await response.Content.ReadFromJsonAsync<OpenLibraryWork>(
            cancellationToken: cancellationToken);

        if (work is null || string.IsNullOrWhiteSpace(work.Title))
        {
            return null;
        }

        var author = await ResolveOpenLibraryAuthorsAsync(
            work.Authors,
            cancellationToken);

        string? textUrl = null;
        try
        {
            textUrl = await FindGutenbergTextAsync(
                work.Title,
                author,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException)
        {
            // Metadata remains useful even if the optional text provider is down.
        }

        var coverId = work.Covers?
            .FirstOrDefault(x => x > 0);

        return new BookCatalogItem(
            "ol-" + workKey,
            work.Title.Trim(),
            string.IsNullOrWhiteSpace(author) ? null : author,
            ExtractDescription(work.Description),
            coverId is > 0
                ? $"https://covers.openlibrary.org/b/id/{coverId}-L.jpg"
                : null,
            (work.Subjects ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Take(8)
                .ToArray(),
            ParseYear(work.FirstPublishDate),
            textUrl,
            $"https://openlibrary.org/works/{workKey}",
            "Open Library",
            textUrl is null ? null : "Project Gutenberg");
    }

    private async Task<string?> ResolveOpenLibraryAuthorsAsync(
        OpenLibraryAuthorReference[]? authorReferences,
        CancellationToken cancellationToken)
    {
        var keys = (authorReferences ?? [])
            .Select(x => x.Author?.Key)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToArray();

        if (keys.Length == 0)
        {
            return null;
        }

        var names = new List<string>(keys.Length);
        foreach (var key in keys)
        {
            try
            {
                var author = await GetJsonAsync<OpenLibraryAuthor>(
                    new Uri($"https://openlibrary.org{key}.json"),
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(author?.Name))
                {
                    names.Add(author.Name.Trim());
                }
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException)
            {
                // A missing author lookup must not make the entire book unusable.
            }
        }

        return names.Count == 0 ? null : string.Join(", ", names);
    }

    private async Task<string?> FindGutenbergTextAsync(
        string title,
        string? author,
        CancellationToken cancellationToken)
    {
        var query = string.IsNullOrWhiteSpace(author)
            ? title
            : $"{title} {author}";

        var response = await GetJsonAsync<GutendexListResponse>(
            new Uri(
                httpClient.BaseAddress!,
                "books?languages=en&search=" + Uri.EscapeDataString(query)),
            cancellationToken);

        if (response is null)
        {
            return null;
        }

        return response.Results
            .Where(x => IsLikelyMatch(x, title, author))
            .Select(MapGutenberg)
            .Where(x => x.CanRead)
            .Select(x => x.TextUrl)
            .FirstOrDefault();
    }

    private async Task<T?> GetJsonAsync<T>(
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsyncWithTimeout(
            new HttpRequestMessage(HttpMethod.Get, uri),
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(
            cancellationToken: cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsyncWithTimeout(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(CatalogRequestTimeout);

        return await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
    }

    private async Task<string> GetTextFollowingRedirectsAsync(
        Uri initialUri,
        CancellationToken cancellationToken)
    {
        if (!IsAllowedGutenbergUri(initialUri))
        {
            throw new InvalidOperationException("The readable text source is not trusted.");
        }

        var currentUri = initialUri;

        for (var redirect = 0; redirect <= MaxRedirects; redirect++)
        {
            using var response = await SendAsyncWithTimeout(
                new HttpRequestMessage(HttpMethod.Get, currentUri),
                cancellationToken);

            if (IsRedirect(response.StatusCode))
            {
                if (redirect == MaxRedirects || response.Headers.Location is null)
                {
                    throw new InvalidOperationException(
                        "The book text source redirected too many times.");
                }

                currentUri = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(currentUri, response.Headers.Location);

                if (!IsAllowedGutenbergUri(currentUri))
                {
                    throw new InvalidOperationException(
                        "The book text source redirected to an untrusted host.");
                }

                continue;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        throw new InvalidOperationException("The book text source could not be loaded.");
    }

    private static BookCatalogItem MapOpenLibrarySearch(OpenLibrarySearchDoc book)
    {
        var workKey = book.Key["/works/".Length..];
        var author = book.AuthorName is { Length: > 0 }
            ? string.Join(", ", book.AuthorName.Where(x => !string.IsNullOrWhiteSpace(x)))
            : null;

        return new BookCatalogItem(
            "ol-" + workKey,
            book.Title!.Trim(),
            string.IsNullOrWhiteSpace(author) ? null : author,
            null,
            book.CoverId is > 0
                ? $"https://covers.openlibrary.org/b/id/{book.CoverId}-M.jpg"
                : null,
            (book.Subjects ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Take(8)
                .ToArray(),
            book.FirstPublishYear,
            null,
            $"https://openlibrary.org/works/{workKey}",
            "Open Library",
            null);
    }

    private static BookCatalogItem MapGutenberg(GutendexBook book)
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
            book.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            book.Title.Trim(),
            string.IsNullOrWhiteSpace(author) ? null : author,
            summary,
            cover,
            book.Subjects
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Take(8)
                .ToArray(),
            null,
            textUrl,
            $"https://www.gutenberg.org/ebooks/{book.Id}",
            "Project Gutenberg",
            textUrl is null ? null : "Project Gutenberg");
    }

    private static bool IsLikelyMatch(
        GutendexBook candidate,
        string title,
        string? author)
    {
        var expectedTitle = NormalizeForMatch(title);
        var candidateTitle = NormalizeForMatch(candidate.Title);

        if (expectedTitle.Length == 0
            || (!candidateTitle.Contains(expectedTitle, StringComparison.Ordinal)
                && !expectedTitle.Contains(candidateTitle, StringComparison.Ordinal)))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(author))
        {
            return true;
        }

        var expectedAuthorParts = NormalizeForMatch(author)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var surname = expectedAuthorParts.LastOrDefault();

        return string.IsNullOrWhiteSpace(surname)
            || candidate.Authors.Any(x =>
                NormalizeForMatch(x.Name ?? "")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains(surname, StringComparer.Ordinal));
    }

    private static string NormalizeForMatch(string value) =>
        Regex.Replace(
                value.ToLowerInvariant(),
                @"[^\p{L}\p{N}]+",
                " ")
            .Trim();

    private static bool TryParseGutenbergId(
        string id,
        out int gutenbergId)
    {
        if (id.StartsWith("pg-", StringComparison.OrdinalIgnoreCase))
        {
            id = id[3..];
        }

        return int.TryParse(
            id,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out gutenbergId)
            && gutenbergId > 0;
    }

    private static bool TryParseOpenLibraryId(
        string id,
        out string workKey)
    {
        workKey = "";

        if (!id.StartsWith("ol-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidate = id[3..].Trim();
        if (!Regex.IsMatch(candidate, @"^OL\d+W$", RegexOptions.IgnoreCase))
        {
            return false;
        }

        workKey = candidate;
        return true;
    }

    private static string? ExtractDescription(JsonElement description)
    {
        if (description.ValueKind == JsonValueKind.String)
        {
            return description.GetString()?.Trim();
        }

        if (description.ValueKind == JsonValueKind.Object
            && description.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString()?.Trim();
        }

        return null;
    }

    private static int? ParseYear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = Regex.Match(value, @"\b(1[0-9]{3}|20[0-9]{2})\b");
        return match.Success && int.TryParse(match.Value, out var year)
            ? year
            : null;
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static bool IsAllowedGutenbergUri(Uri uri) =>
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && (uri.Host.Equals("gutenberg.org", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".gutenberg.org", StringComparison.OrdinalIgnoreCase));

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

    private sealed record OpenLibrarySearchResponse(
        [property: JsonPropertyName("docs")] OpenLibrarySearchDoc[] Docs);

    private sealed record OpenLibrarySearchDoc(
        [property: JsonPropertyName("key")] string? Key,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("author_name")] string[]? AuthorName,
        [property: JsonPropertyName("cover_i")] int? CoverId,
        [property: JsonPropertyName("first_publish_year")] int? FirstPublishYear,
        [property: JsonPropertyName("subject")] string[]? Subjects);

    private sealed record OpenLibraryWork(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("description")] JsonElement Description,
        [property: JsonPropertyName("subjects")] string[]? Subjects,
        [property: JsonPropertyName("covers")] int[]? Covers,
        [property: JsonPropertyName("first_publish_date")] string? FirstPublishDate,
        [property: JsonPropertyName("authors")] OpenLibraryAuthorReference[]? Authors);

    private sealed record OpenLibraryAuthorReference(
        [property: JsonPropertyName("author")] OpenLibraryKeyReference? Author);

    private sealed record OpenLibraryKeyReference(
        [property: JsonPropertyName("key")] string? Key);

    private sealed record OpenLibraryAuthor(
        [property: JsonPropertyName("name")] string? Name);
}
