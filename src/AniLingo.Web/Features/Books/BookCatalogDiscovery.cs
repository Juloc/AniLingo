using System.Net;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Books;

public sealed partial class BookCatalogService
{
    private static readonly TimeSpan DiscoveryProviderTimeout =
        TimeSpan.FromSeconds(7);

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
        var gutenberg = await CaptureCatalogAsync(
            BrowsePopularGutenbergAsync,
            cancellationToken);

        if (gutenberg.Count > 0)
        {
            return gutenberg;
        }

        return await CaptureCatalogAsync(
            BrowseFreeGoogleBooksAsync,
            cancellationToken);
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
        var response = await GetJsonAsync<GoogleVolumesResponse>(
            BuildGoogleBooksUri(
                "volumes",
                new Dictionary<string, string?>
                {
                    ["q"] = query,
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
        var order = new List<string>();
        var items = new Dictionary<string, BookCatalogItem>(
            StringComparer.Ordinal);

        foreach (var source in sources)
        {
            foreach (var item in source)
            {
                var key = CatalogMergeKey(item);
                if (!items.TryGetValue(key, out var current))
                {
                    order.Add(key);
                    items[key] = item;
                    continue;
                }

                items[key] = MergeCatalogItem(
                    current,
                    item);
            }
        }

        return order
            .Select(key => items[key])
            .ToArray();
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

        return primary with
        {
            Author = FirstNonEmpty(
                primary.Author,
                secondary.Author),
            Summary = FirstNonEmpty(
                primary.Summary,
                secondary.Summary),
            CoverImageUrl = FirstNonEmpty(
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
                secondary.TextSourceName)
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

        var cover = FirstNonEmpty(
            info.ImageLinks?.Thumbnail,
            info.ImageLinks?.SmallThumbnail);
        if (cover is not null
            && cover.StartsWith(
                "http://",
                StringComparison.OrdinalIgnoreCase))
        {
            cover = "https://" + cover["http://".Length..];
        }

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
            null);
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

        [JsonPropertyName("imageLinks")]
        public GoogleImageLinks? ImageLinks { get; init; }

        [JsonPropertyName("infoLink")]
        public string? InfoLink { get; init; }

        [JsonPropertyName("canonicalVolumeLink")]
        public string? CanonicalVolumeLink { get; init; }
    }

    private sealed record GoogleImageLinks(
        [property: JsonPropertyName("smallThumbnail")]
        string? SmallThumbnail,
        [property: JsonPropertyName("thumbnail")]
        string? Thumbnail);
}
