using System.Net;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Books;

public sealed partial class BookCatalogService
{
    private const int MaxWikisourceSearchResults = 12;
    private const int MaxWikisourceChapters = 160;
    private const int MaxWikisourceTextCharacters = 15_000_000;

    private async Task<IReadOnlyList<BookCatalogItem>> SearchIndonesianWikisourceAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<WikisourceSearchResponse>(
            BuildWikisourceUri(
                ("action", "query"),
                ("list", "search"),
                ("srsearch", query),
                ("srnamespace", "0"),
                ("srlimit", MaxWikisourceSearchResults.ToString(
                    System.Globalization.CultureInfo.InvariantCulture))),
            cancellationToken);

        return (response?.Query?.Search ?? [])
            .Where(x =>
                x.PageId > 0
                && !string.IsNullOrWhiteSpace(x.Title)
                && !x.Title.Contains(
                    '/',
                    StringComparison.Ordinal))
            .Select(x => new BookCatalogItem(
                "wsid-" + x.PageId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                x.Title.Trim(),
                null,
                CleanWikisourceSnippet(x.Snippet),
                null,
                ["Indonesian literature", "Wikisource"],
                null,
                null,
                null,
                WikisourcePageUrl(x.Title),
                "Indonesian Wikisource",
                null))
            .Take(MaxWikisourceSearchResults)
            .ToArray();
    }

    private async Task<BookCatalogItem?> GetIndonesianWikisourceAsync(
        int pageId,
        CancellationToken cancellationToken)
    {
        var page = await GetWikisourcePageInfoAsync(
            pageId,
            cancellationToken);

        if (page is null
            || page.PageId <= 0
            || string.IsNullOrWhiteSpace(page.Title))
        {
            return null;
        }

        return new BookCatalogItem(
            "wsid-" + page.PageId.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            page.Title.Trim(),
            null,
            "Public-domain text available for direct import from Indonesian Wikisource.",
            null,
            ["Indonesian literature", "Wikisource"],
            null,
            null,
            null,
            FirstNonEmpty(
                page.FullUrl,
                WikisourcePageUrl(page.Title))
                ?? WikisourcePageUrl(page.Title),
            "Indonesian Wikisource",
            null);
    }

    private async Task<Guid> ImportIndonesianWikisourceAsync(
        int pageId,
        CancellationToken cancellationToken)
    {
        var page = await GetWikisourcePageInfoAsync(
            pageId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The Indonesian Wikisource work could not be found.");

        if (string.IsNullOrWhiteSpace(page.Title))
        {
            throw new InvalidOperationException(
                "The Indonesian Wikisource work has no usable title.");
        }

        var root = await ParseWikisourcePageAsync(
            pageId,
            null,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The Indonesian Wikisource work could not be read.");

        var linkedChapters = (root.Links ?? [])
            .Where(x =>
                x.Namespace == 0
                && !string.IsNullOrWhiteSpace(x.Title)
                && x.Title.StartsWith(
                    page.Title + "/",
                    StringComparison.Ordinal))
            .Select(x => x.Title.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(MaxWikisourceChapters)
            .ToArray();

        var chapters = new List<ImportedBookChapter>();
        var totalCharacters = 0;

        if (linkedChapters.Length == 0)
        {
            var text = ExtractWikisourceText(root.Text);
            if (!string.IsNullOrWhiteSpace(text))
            {
                chapters.Add(new ImportedBookChapter(
                    1,
                    CleanWikisourceTitle(
                        root.DisplayTitle,
                        page.Title),
                    text));
            }
        }
        else
        {
            var parsedChapters = await Task.WhenAll(
                linkedChapters.Select((title, index) =>
                    LoadWikisourceChapterAsync(
                        title,
                        index,
                        cancellationToken)));

            foreach (var chapter in parsedChapters
                         .OrderBy(x => x.Index))
            {
                if (string.IsNullOrWhiteSpace(chapter.Text))
                {
                    continue;
                }

                totalCharacters += chapter.Text.Length;
                if (totalCharacters > MaxWikisourceTextCharacters)
                {
                    throw new InvalidOperationException(
                        "The Wikisource work is too large for one import.");
                }

                chapters.Add(new ImportedBookChapter(
                    chapters.Count + 1,
                    chapter.Title,
                    chapter.Text));
            }
        }

        if (chapters.Count == 0)
        {
            throw new InvalidOperationException(
                "No readable Wikisource chapters were found for this work.");
        }

        var parsed = new ParsedEpubBook(
            page.Title.Trim(),
            null,
            "Imported from Indonesian Wikisource.",
            "id",
            ["Indonesian literature", "Wikisource"],
            chapters,
            null,
            null);

        return await ImportParsedBookAsync(
            parsed,
            sourceKey: "wikisource-"
                + pageId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            sourceUrl: FirstNonEmpty(
                    page.FullUrl,
                    WikisourcePageUrl(page.Title))
                ?? WikisourcePageUrl(page.Title),
            metadataProvider: "wikisource-id",
            metadataExternalId: "wsid-"
                + pageId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            coverImageUrl: null,
            fallbackAuthor: null,
            fallbackDescription:
                "Imported from Indonesian Wikisource.",
            fallbackSubjects:
                ["Indonesian literature", "Wikisource"],
            cancellationToken);
    }

    private async Task<WikisourceLoadedChapter> LoadWikisourceChapterAsync(
        string pageTitle,
        int index,
        CancellationToken cancellationToken)
    {
        var parsed = await ParseWikisourcePageAsync(
            null,
            pageTitle,
            cancellationToken);

        if (parsed is null)
        {
            return new WikisourceLoadedChapter(
                index,
                WikisourceRelativeTitle(pageTitle),
                "");
        }

        return new WikisourceLoadedChapter(
            index,
            CleanWikisourceTitle(
                parsed.DisplayTitle,
                WikisourceRelativeTitle(pageTitle)),
            ExtractWikisourceText(parsed.Text));
    }

    private async Task<WikisourceQueryPage?> GetWikisourcePageInfoAsync(
        int pageId,
        CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<WikisourcePageInfoResponse>(
            BuildWikisourceUri(
                ("action", "query"),
                ("pageids", pageId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
                ("prop", "info"),
                ("inprop", "url")),
            cancellationToken);

        return response?.Query?.Pages?
            .FirstOrDefault(x =>
                x.PageId == pageId
                && !x.Missing);
    }

    private async Task<WikisourceParse?> ParseWikisourcePageAsync(
        int? pageId,
        string? title,
        CancellationToken cancellationToken)
    {
        var values = new List<(string Key, string Value)>
        {
            ("action", "parse"),
            ("prop", "text|links|displaytitle"),
            ("disableeditsection", "1")
        };

        if (pageId is int id)
        {
            values.Add((
                "pageid",
                id.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)));
        }
        else if (!string.IsNullOrWhiteSpace(title))
        {
            values.Add(("page", title));
        }
        else
        {
            throw new ArgumentException(
                "Wikisource page id or title is required.");
        }

        var response = await GetJsonAsync<WikisourceParseResponse>(
            BuildWikisourceUri(values.ToArray()),
            cancellationToken);

        return response?.Parse;
    }

    private static Uri BuildWikisourceUri(
        params (string Key, string Value)[] values)
    {
        var all = values
            .Concat(
            [
                ("format", "json"),
                ("formatversion", "2"),
                ("errorformat", "plaintext")
            ])
            .ToArray();

        return new Uri(
            "https://id.wikisource.org/w/api.php?"
            + BuildQuery(all));
    }

    private static bool TryParseWikisourceId(
        string id,
        out int pageId)
    {
        pageId = 0;

        if (!id.StartsWith(
                "wsid-",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(
                id[5..],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out pageId)
            && pageId > 0;
    }

    private static string WikisourcePageUrl(
        string title) =>
        "https://id.wikisource.org/wiki/"
        + Uri.EscapeDataString(
                title.Replace(' ', '_'))
            .Replace(
                "%2F",
                "/",
                StringComparison.OrdinalIgnoreCase);

    private static string? CleanWikisourceSnippet(
        string? snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet))
        {
            return null;
        }

        var text = WebUtility.HtmlDecode(
            Regex.Replace(
                snippet,
                @"<[^>]+>",
                " "));

        text = Regex.Replace(
                text,
                @"\s+",
                " ")
            .Trim();

        return text.Length <= 1200
            ? text
            : text[..1200].TrimEnd();
    }

    private static string CleanWikisourceTitle(
        string? displayTitle,
        string fallback)
    {
        var value = string.IsNullOrWhiteSpace(displayTitle)
            ? fallback
            : displayTitle;

        value = WebUtility.HtmlDecode(
            Regex.Replace(
                value,
                @"<[^>]+>",
                " "))
            .Trim();

        if (value.Contains(
            '/',
            StringComparison.Ordinal))
        {
            value = WikisourceRelativeTitle(value);
        }

        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value;
    }

    private static string WikisourceRelativeTitle(
        string title)
    {
        var separator = title.LastIndexOf('/');
        return separator >= 0
            && separator + 1 < title.Length
            ? title[(separator + 1)..]
            : title;
    }

    internal static string ExtractWikisourceText(
        string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return "";
        }

        var text = Regex.Replace(
            html,
            @"<!--.*?-->",
            "",
            RegexOptions.Singleline);
        text = Regex.Replace(
            text,
            @"<(script|style|noscript)\b[^>]*>.*?</\1>",
            "",
            RegexOptions.IgnoreCase
                | RegexOptions.Singleline);
        text = Regex.Replace(
            text,
            @"<sup\b[^>]*class=""[^""]*reference[^""]*""[^>]*>.*?</sup>",
            "",
            RegexOptions.IgnoreCase
                | RegexOptions.Singleline);
        text = Regex.Replace(
            text,
            @"<span\b[^>]*class=""[^""]*mw-editsection[^""]*""[^>]*>.*?</span>",
            "",
            RegexOptions.IgnoreCase
                | RegexOptions.Singleline);
        text = Regex.Replace(
            text,
            @"<(br\s*/?|/p|/div|/li|/h[1-6]|/blockquote)>",
            "\n",
            RegexOptions.IgnoreCase);
        text = Regex.Replace(
            text,
            @"<[^>]+>",
            " ");

        text = WebUtility.HtmlDecode(text)
            .Replace('\u00a0', ' ')
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var paragraphs = text
            .Split(
                '\n',
                StringSplitOptions.TrimEntries)
            .Select(x => Regex.Replace(
                    x,
                    @"[ \t]+",
                    " ")
                .Trim())
            .Where(x =>
                x.Length > 0
                && !x.Equals(
                    "sunting",
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var result = string.Join(
            "\n\n",
            paragraphs)
            .Trim();

        return result.Length <= MaxWikisourceTextCharacters
            ? result
            : throw new InvalidOperationException(
                "A Wikisource chapter is too large to import.");
    }

    private sealed record WikisourceLoadedChapter(
        int Index,
        string Title,
        string Text);

    private sealed record WikisourceSearchResponse(
        [property: JsonPropertyName("query")]
        WikisourceSearchQuery? Query);

    private sealed record WikisourceSearchQuery(
        [property: JsonPropertyName("search")]
        WikisourceSearchHit[]? Search);

    private sealed record WikisourceSearchHit(
        [property: JsonPropertyName("pageid")]
        int PageId,
        [property: JsonPropertyName("title")]
        string Title,
        [property: JsonPropertyName("snippet")]
        string? Snippet);

    private sealed record WikisourcePageInfoResponse(
        [property: JsonPropertyName("query")]
        WikisourcePageInfoQuery? Query);

    private sealed record WikisourcePageInfoQuery(
        [property: JsonPropertyName("pages")]
        WikisourceQueryPage[]? Pages);

    private sealed record WikisourceQueryPage(
        [property: JsonPropertyName("pageid")]
        int PageId,
        [property: JsonPropertyName("title")]
        string? Title,
        [property: JsonPropertyName("fullurl")]
        string? FullUrl,
        [property: JsonPropertyName("missing")]
        bool Missing = false);

    private sealed record WikisourceParseResponse(
        [property: JsonPropertyName("parse")]
        WikisourceParse? Parse);

    private sealed record WikisourceParse(
        [property: JsonPropertyName("title")]
        string? Title,
        [property: JsonPropertyName("displaytitle")]
        string? DisplayTitle,
        [property: JsonPropertyName("text")]
        string? Text,
        [property: JsonPropertyName("links")]
        WikisourceLink[]? Links);

    private sealed record WikisourceLink(
        [property: JsonPropertyName("ns")]
        int Namespace,
        [property: JsonPropertyName("title")]
        string Title);
}
