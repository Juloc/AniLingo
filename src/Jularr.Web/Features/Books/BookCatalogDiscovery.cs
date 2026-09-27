using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Jularr.Web.Features.Books;

public sealed partial class BookCatalogService
{
    private const int MaxRemoteCoverBytes = 10 * 1024 * 1024;
    private static readonly TimeSpan DiscoveryProviderTimeout =
        TimeSpan.FromSeconds(7);

    private static readonly HashSet<string> AllowedCoverHosts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "books.google.com",
            "books.googleusercontent.com",
            "books.googleusercontent.com",
            "covers.openlibrary.org",
            "www.gutenberg.org",
            "gutenberg.org"
        };

    private async Task<IReadOnlyList<BookCatalogItem>> CaptureCatalogAsync(
        Func<CancellationToken, Task<IReadOnlyList<BookCatalogItem>>> action,
        CancellationToken cancellationToken,
        bool fallbackToEmpty = true)
    {
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        timeout.CancelAfter(DiscoveryProviderTimeout);

        try
        {
            return await action(timeout.Token);
        }
        catch (TaskCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
        catch (InvalidOperationException)
            when (fallbackToEmpty)
        {
            return [];
        }
    }

    private async Task<IReadOnlyList<BookCatalogItem>> BrowsePopularBooksAsync(
        CancellationToken cancellationToken)
    {
        var daily = await CaptureCatalogAsync(
            token => BrowseOpenLibraryTrendingAsync("daily", token),
            cancellationToken);

        if (daily.Count > 0)
        {
            return daily;
        }

        // Still preserve trending semantics when the daily window happens to be
        // unavailable. Do not silently substitute all-time Gutenberg downloads.
        return await CaptureCatalogAsync(
            token => BrowseOpenLibraryTrendingAsync("weekly", token),
            cancellationToken);
    }

    private async Task<IReadOnlyList<BookCatalogItem>> BrowseOpenLibraryTrendingAsync(
        string window,
        CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<OpenLibraryTrendingResponse>(
            new Uri(
                $"https://openlibrary.org/trending/{window}.json?limit={SearchLimit}"),
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Open Library trending returned no data.");

        return (response.Works ?? [])
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.Key)
                && !string.IsNullOrWhiteSpace(x.Title)
                && x.Key.StartsWith("/works/", StringComparison.Ordinal))
            .Select(MapOpenLibrarySearch)
            .Take(SearchLimit)
            .ToArray();
    }

    private async Task<IReadOnlyList<BookCatalogItem>> BrowsePopularGutenbergAsync(
        CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<GutendexListResponse>(
            new Uri(
                httpClient.BaseAddress!,
                "books?languages=en&sort=popular"),
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Project Gutenberg catalog returned no data.");

        return response.Results
            .Select(MapGutenberg)
            .Take(SearchLimit)
            .ToArray();
    }

    private async Task<IReadOnlyList<BookCatalogItem>> SearchGoogleBooksAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var response = await SearchGoogleVolumesAsync(
            query,
            24,
            cancellationToken);

        return response
            .Select(MapGoogleBook)
            .Take(SearchLimit)
            .ToArray();
    }

    private async Task<IReadOnlyList<GoogleVolume>> SearchGoogleVolumesAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<GoogleVolumesResponse>(
            BuildGoogleBooksUri(
                "volumes",
                new Dictionary<string, string?>
                {
                    ["q"] = query,
                    ["maxResults"] = Math.Clamp(limit, 1, 40).ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ["printType"] = "books",
                    ["orderBy"] = "relevance",
                    ["projection"] = "full"
                }),
            cancellationToken);

        return (response?.Items ?? [])
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.Id)
                && !string.IsNullOrWhiteSpace(x.VolumeInfo?.Title))
            .ToArray();
    }

    private async Task<IReadOnlyList<BookCatalogItem>> BrowseFreeGoogleBooksAsync(
        CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<GoogleVolumesResponse>(
            BuildGoogleBooksUri(
                "volumes",
                new Dictionary<string, string?>
                {
                    ["q"] = "subject:fiction",
                    ["filter"] = "free-ebooks",
                    ["maxResults"] = "24",
                    ["printType"] = "books",
                    ["orderBy"] = "relevance",
                    ["projection"] = "full"
                }),
            cancellationToken);

        return (response?.Items ?? [])
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.Id)
                && !string.IsNullOrWhiteSpace(x.VolumeInfo?.Title))
            .Select(MapGoogleBook)
            .Take(SearchLimit)
            .ToArray();
    }

    private async Task<BookCatalogItem?> GetGoogleBooksAsync(
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            var volume = await GetJsonAsync<GoogleVolume>(
                BuildGoogleBooksUri(
                    "volumes/" + Uri.EscapeDataString(id),
                    new Dictionary<string, string?>()),
                cancellationToken);

            return volume is null
                || string.IsNullOrWhiteSpace(volume.VolumeInfo?.Title)
                ? null
                : MapGoogleBook(volume);
        }
        catch (HttpRequestException exception)
            when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private Uri BuildGoogleBooksUri(
        string path,
        IReadOnlyDictionary<string, string?> query)
    {
        var parameters = new List<string>();

        foreach (var pair in query)
        {
            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            parameters.Add(
                Uri.EscapeDataString(pair.Key)
                + "="
                + Uri.EscapeDataString(pair.Value));
        }

        var apiKey = configuration["Books:GoogleBooks:ApiKey"]?.Trim();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            parameters.Add(
                "key=" + Uri.EscapeDataString(apiKey));
        }

        var suffix = parameters.Count == 0
            ? ""
            : "?" + string.Join("&", parameters);

        return new Uri(
            "https://www.googleapis.com/books/v1/"
            + path
            + suffix);
    }

    private static IReadOnlyList<BookCatalogItem> MergeCatalogResults(
        params IReadOnlyList<BookCatalogItem>[] sources)
    {
        var merged = new List<BookCatalogItem>();

        foreach (var item in sources.SelectMany(x => x))
        {
            var index = merged.FindIndex(existing =>
                SameCatalogWork(existing, item));

            if (index < 0)
            {
                merged.Add(item);
                continue;
            }

            merged[index] = MergeCatalogItem(
                merged[index],
                item);
        }

        return merged;
    }

    private static bool SameCatalogWork(
        BookCatalogItem left,
        BookCatalogItem right)
    {
        var leftIsbns = (left.Isbns ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);
        if (leftIsbns.Count > 0
            && (right.Isbns ?? []).Any(leftIsbns.Contains))
        {
            return true;
        }

        return CatalogMergeKey(left) == CatalogMergeKey(right);
    }

    private static BookCatalogItem MergeCatalogItem(
        BookCatalogItem primary,
        BookCatalogItem secondary)
    {
        var subjects = primary.Subjects
            .Concat(secondary.Subjects)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToArray();
        var isbns = (primary.Isbns ?? [])
            .Concat(secondary.Isbns ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Take(24)
            .ToArray();

        var google = primary.SourceName.Equals(
                "Google Books",
                StringComparison.OrdinalIgnoreCase)
            ? primary
            : secondary.SourceName.Equals(
                "Google Books",
                StringComparison.OrdinalIgnoreCase)
                ? secondary
                : null;

        return primary with
        {
            Author = FirstNonEmpty(
                primary.Author,
                secondary.Author),
            Summary = FirstNonEmpty(
                google?.Summary,
                primary.Summary,
                secondary.Summary),
            CoverImageUrl = FirstNonEmpty(
                google?.CoverImageUrl,
                primary.CoverImageUrl,
                secondary.CoverImageUrl),
            Subjects = subjects,
            FirstPublishYear =
                primary.FirstPublishYear
                ?? secondary.FirstPublishYear,
            TextUrl = FirstNonEmpty(
                primary.TextUrl,
                secondary.TextUrl),
            EpubUrl = FirstNonEmpty(
                primary.EpubUrl,
                secondary.EpubUrl),
            TextSourceName = FirstNonEmpty(
                primary.TextSourceName,
                secondary.TextSourceName),
            Isbns = isbns,
            Publisher = FirstNonEmpty(
                google?.Publisher,
                primary.Publisher,
                secondary.Publisher),
            PublishedDate = FirstNonEmpty(
                google?.PublishedDate,
                primary.PublishedDate,
                secondary.PublishedDate)
        };
    }

    private static string CatalogMergeKey(
        BookCatalogItem item)
    {
        var title = NormalizeForMatch(item.Title);
        var authorTokens = NormalizeForMatch(item.Author ?? "")
            .Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(x => x, StringComparer.Ordinal);

        return title
            + "|"
            + string.Join(" ", authorTokens);
    }

    private static BookCatalogItem MapGoogleBook(
        GoogleVolume volume)
    {
        var info = volume.VolumeInfo
            ?? new GoogleVolumeInfo();

        var author = info.Authors is { Length: > 0 }
            ? string.Join(
                ", ",
                info.Authors.Where(x =>
                    !string.IsNullOrWhiteSpace(x)))
            : null;

        var cover = NormalizeGoogleCoverUrl(
            FirstNonEmpty(
                info.ImageLinks?.ExtraLarge,
                info.ImageLinks?.Large,
                info.ImageLinks?.Medium,
                info.ImageLinks?.Small,
                info.ImageLinks?.Thumbnail,
                info.ImageLinks?.SmallThumbnail));

        var sourceUrl = FirstNonEmpty(
                info.CanonicalVolumeLink,
                info.InfoLink)
            ?? "https://books.google.com/books?id="
                + Uri.EscapeDataString(volume.Id ?? "");

        return new BookCatalogItem(
            "gb-" + (volume.Id ?? ""),
            info.Title?.Trim() ?? "Untitled book",
            string.IsNullOrWhiteSpace(author)
                ? null
                : author,
            CleanGoogleDescription(info.Description),
            cover,
            (info.Categories ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Take(16)
                .ToArray(),
            ParseYear(info.PublishedDate),
            null,
            null,
            sourceUrl,
            "Google Books",
            null,
            (info.IndustryIdentifiers ?? [])
                .Select(x => NormalizeIsbn(x.Identifier))
                .Where(x => x is not null)
                .Select(x => x!)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            info.Publisher?.Trim(),
            info.PublishedDate?.Trim());
    }

    private static string? FirstNonEmpty(
        string? first,
        string? second,
        string? third,
        params string?[] remaining)
    {
        foreach (var value in new[] { first, second, third }.Concat(remaining))
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string? NormalizeGoogleCoverUrl(string? cover)
    {
        if (string.IsNullOrWhiteSpace(cover))
        {
            return null;
        }

        var normalized = cover.Trim();
        if (normalized.StartsWith(
                "http://",
                StringComparison.OrdinalIgnoreCase))
        {
            normalized = "https://" + normalized["http://".Length..];
        }

        return normalized;
    }

    private Task<bool> TryPersistPreferredCoverAsync(
        Guid workId,
        ParsedEpubBook parsed,
        string? catalogFallback,
        CancellationToken cancellationToken) =>
        TryPersistPreferredCoverAsync(
            workId,
            parsed.Title,
            parsed.Author,
            parsed.Isbn10,
            parsed.Isbn13,
            parsed.CoverBytes,
            parsed.CoverMediaType,
            catalogFallback,
            cancellationToken);

    private async Task<bool> TryPersistPreferredCoverAsync(
        Guid workId,
        string title,
        string? author,
        string? isbn10,
        string? isbn13,
        byte[]? embeddedCover,
        string? embeddedMediaType,
        string? catalogFallback,
        CancellationToken cancellationToken)
    {
        string? googleCover = null;
        try
        {
            googleCover = await FindPreferredGoogleCoverAsync(
                title,
                author,
                isbn10,
                isbn13,
                cancellationToken);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested
            && exception is not OperationCanceledException)
        {
            // Artwork enrichment is optional; importing the owned book must
            // still succeed when a metadata provider is unavailable.
        }

        if (await TryCacheRemoteCoverAsync(
                workId,
                googleCover,
                cancellationToken))
        {
            return true;
        }

        if (embeddedCover is { Length: > 0 }
            && !string.IsNullOrWhiteSpace(embeddedMediaType)
            && await SaveLocalCoverAsync(
                workId,
                embeddedCover,
                embeddedMediaType,
                cancellationToken) is not null)
        {
            return true;
        }

        if (!string.Equals(
                googleCover,
                catalogFallback,
                StringComparison.OrdinalIgnoreCase)
            && await TryCacheRemoteCoverAsync(
                workId,
                catalogFallback,
                cancellationToken))
        {
            return true;
        }

        return false;
    }

    private async Task<string?> FindPreferredGoogleCoverAsync(
        string title,
        string? author,
        string? isbn10,
        string? isbn13,
        CancellationToken cancellationToken)
    {
        foreach (var isbn in new[] { isbn13, isbn10 }
                     .Select(NormalizeIsbn)
                     .Where(x => x is not null)
                     .Select(x => x!))
        {
            var exact = await SearchGoogleVolumesAsync(
                "isbn:" + isbn,
                10,
                cancellationToken);
            var exactCover = exact
                .Select(MapGoogleBook)
                .FirstOrDefault(x =>
                    (x.Isbns ?? []).Contains(
                        isbn,
                        StringComparer.Ordinal)
                    && !string.IsNullOrWhiteSpace(x.CoverImageUrl))
                ?.CoverImageUrl;
            if (!string.IsNullOrWhiteSpace(exactCover))
            {
                return exactCover;
            }
        }

        title = title.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var query = "intitle:" + title;
        if (!string.IsNullOrWhiteSpace(author))
        {
            query += " inauthor:" + author.Trim();
        }

        var expectedTitle = NormalizeForMatch(title);
        var expectedAuthor = NormalizeForMatch(author ?? "");
        return (await SearchGoogleVolumesAsync(
                query,
                24,
                cancellationToken))
            .Select(MapGoogleBook)
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.CoverImageUrl)
                && StrongBookMatch(
                    expectedTitle,
                    expectedAuthor,
                    x))
            .OrderByDescending(x => ParseYear(x.PublishedDate)
                ?? x.FirstPublishYear
                ?? 0)
            .Select(x => x.CoverImageUrl)
            .FirstOrDefault();
    }

    private static bool StrongBookMatch(
        string expectedTitle,
        string expectedAuthor,
        BookCatalogItem candidate)
    {
        var candidateTitle = NormalizeForMatch(candidate.Title);
        if (candidateTitle != expectedTitle
            && !candidateTitle.Contains(expectedTitle, StringComparison.Ordinal)
            && !expectedTitle.Contains(candidateTitle, StringComparison.Ordinal))
        {
            return false;
        }

        if (expectedAuthor.Length == 0)
        {
            return true;
        }

        var candidateAuthor = NormalizeForMatch(candidate.Author ?? "");
        var surname = expectedAuthor
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();

        return surname is null
            || candidateAuthor
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains(surname, StringComparer.Ordinal);
    }

    private async Task<bool> TryCacheRemoteCoverAsync(
        Guid workId,
        string? coverUrl,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(
                coverUrl,
                UriKind.Absolute,
                out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !AllowedCoverHosts.Contains(uri.Host))
        {
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await SendAsyncWithTimeout(
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var finalUri = response.RequestMessage?.RequestUri;
            if (finalUri is not null
                && (finalUri.Scheme != Uri.UriSchemeHttps
                    || !AllowedCoverHosts.Contains(finalUri.Host)))
            {
                return false;
            }

            var length = response.Content.Headers.ContentLength;
            if (length is > MaxRemoteCoverBytes)
            {
                return false;
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is null
                || mediaType is not (
                    "image/jpeg"
                    or "image/jpg"
                    or "image/png"
                    or "image/webp"
                    or "image/gif"))
            {
                return false;
            }

            await using var source = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            using var copy = await CopyToMemoryBoundedAsync(
                source,
                MaxRemoteCoverBytes,
                cancellationToken);
            var bytes = copy.ToArray();

            if (!LooksLikeImage(bytes, mediaType))
            {
                return false;
            }

            return await SaveLocalCoverAsync(
                    workId,
                    bytes,
                    mediaType,
                    cancellationToken)
                is not null;
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested
            && exception is HttpRequestException
                or TaskCanceledException
                or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool LooksLikeImage(
        byte[] bytes,
        string mediaType)
    {
        if (bytes.Length < 12)
        {
            return false;
        }

        return mediaType switch
        {
            "image/jpeg" or "image/jpg" =>
                bytes[0] == 0xFF && bytes[1] == 0xD8,
            "image/png" =>
                bytes[0] == 0x89
                && bytes[1] == 0x50
                && bytes[2] == 0x4E
                && bytes[3] == 0x47,
            "image/gif" =>
                bytes[0] == (byte)'G'
                && bytes[1] == (byte)'I'
                && bytes[2] == (byte)'F',
            "image/webp" =>
                bytes[0] == (byte)'R'
                && bytes[1] == (byte)'I'
                && bytes[2] == (byte)'F'
                && bytes[3] == (byte)'F'
                && bytes[8] == (byte)'W'
                && bytes[9] == (byte)'E'
                && bytes[10] == (byte)'B'
                && bytes[11] == (byte)'P',
            _ => false
        };
    }

    private static string? CleanGoogleDescription(
        string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var decoded = WebUtility.HtmlDecode(description);
        var withoutTags = Regex.Replace(
            decoded,
            @"<[^>]+>",
            " ");
        var clean = Regex.Replace(
                withoutTags,
                @"\s+",
                " ")
            .Trim();

        return clean.Length <= 4000
            ? clean
            : clean[..4000].TrimEnd();
    }

    private static bool TryParseGoogleBooksId(
        string id,
        out string googleId)
    {
        googleId = "";

        if (!id.StartsWith(
                "gb-",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidate = id[3..].Trim();
        if (candidate.Length is < 1 or > 128
            || !Regex.IsMatch(
                candidate,
                @"^[A-Za-z0-9_-]+$"))
        {
            return false;
        }

        googleId = candidate;
        return true;
    }

    private sealed record OpenLibraryTrendingResponse(
        [property: JsonPropertyName("works")]
        OpenLibrarySearchDoc[]? Works);

    private sealed record GoogleVolumesResponse(
        [property: JsonPropertyName("items")]
        GoogleVolume[]? Items);

    private sealed record GoogleVolume(
        [property: JsonPropertyName("id")]
        string? Id,
        [property: JsonPropertyName("volumeInfo")]
        GoogleVolumeInfo? VolumeInfo);

    private sealed record GoogleVolumeInfo
    {
        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("authors")]
        public string[]? Authors { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("categories")]
        public string[]? Categories { get; init; }

        [JsonPropertyName("publishedDate")]
        public string? PublishedDate { get; init; }

        [JsonPropertyName("publisher")]
        public string? Publisher { get; init; }

        [JsonPropertyName("industryIdentifiers")]
        public GoogleIndustryIdentifier[]? IndustryIdentifiers { get; init; }

        [JsonPropertyName("imageLinks")]
        public GoogleImageLinks? ImageLinks { get; init; }

        [JsonPropertyName("infoLink")]
        public string? InfoLink { get; init; }

        [JsonPropertyName("canonicalVolumeLink")]
        public string? CanonicalVolumeLink { get; init; }
    }

    private sealed record GoogleIndustryIdentifier(
        [property: JsonPropertyName("type")]
        string? Type,
        [property: JsonPropertyName("identifier")]
        string? Identifier);

    private sealed record GoogleImageLinks
    {
        [JsonPropertyName("smallThumbnail")]
        public string? SmallThumbnail { get; init; }

        [JsonPropertyName("thumbnail")]
        public string? Thumbnail { get; init; }

        [JsonPropertyName("small")]
        public string? Small { get; init; }

        [JsonPropertyName("medium")]
        public string? Medium { get; init; }

        [JsonPropertyName("large")]
        public string? Large { get; init; }

        [JsonPropertyName("extraLarge")]
        public string? ExtraLarge { get; init; }
    }
}
