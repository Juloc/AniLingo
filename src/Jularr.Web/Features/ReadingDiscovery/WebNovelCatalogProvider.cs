using System.Net;
using System.Text.RegularExpressions;

namespace Jularr.Web.Features.ReadingDiscovery;

/// <summary>
/// WebNovel (webnovel.com) as a Light Novel reference source. WebNovel documents no public
/// search or catalog API and fronts its pages with a bot challenge, so the only path used is
/// the public search page a visitor opens, with an honest user agent, one request per
/// search. When the site refuses or challenges the request, the source is reported blocked
/// and left alone for an hour: there is no header spoofing, no challenge handling and no
/// session use. Results are listings that link to the book's public page. Free chapters are
/// read there; locked and paid chapters stay locked, and nothing is imported.
/// </summary>
public sealed partial class WebNovelCatalogProvider(HttpClient client) : IReadingCatalogProvider
{
    public const string ProviderKey = "webnovel";

    private const string SourceName = "WebNovel";
    private const string Origin = "https://www.webnovel.com";

    public string Key => ProviderKey;

    public async Task<IReadOnlyList<ReadingCatalogCandidate>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalized = ReadingCatalogSearch.NormalizeQuery(query);
        if (normalized.Length == 0)
        {
            return [];
        }

        var html = await ReadingSourceHttp.GetStringAsync(
            client,
            BuildSearchUri(normalized),
            SourceName,
            "text/html",
            notFoundIsEmpty: true,
            cancellationToken);

        return html is null
            ? []
            : ParseSearchResults(html)
                .Take(Math.Clamp(limit, 1, 24))
                .ToArray();
    }

    internal static Uri BuildSearchUri(string query) =>
        new($"{Origin}/search?keywords={Uri.EscapeDataString(query)}");

    /// <summary>
    /// Collects the book links of the search page. A book is identified by the numeric id at
    /// the end of its <c>/book/…</c> path; its title comes from the link's title attribute or
    /// the cover image's alt text, so a "Read now" button never becomes a title.
    /// </summary>
    internal static IReadOnlyList<ReadingCatalogCandidate> ParseSearchResults(
        string html)
    {
        var books = new Dictionary<string, (string? Title, string? Cover)>(
            StringComparer.Ordinal);
        var order = new List<string>();

        foreach (Match anchor in BookAnchorRegex().Matches(html))
        {
            var id = anchor.Groups["id"].Value;
            var attributes = anchor.Groups["attributes"].Value;
            var inner = anchor.Groups["inner"].Value;

            var title = FirstNonEmpty(
                CleanText(TitleAttributeRegex().Match(attributes).Groups["value"].Value),
                CleanText(ImageAltRegex().Match(inner).Groups["value"].Value));
            var cover = ReadCover(inner);

            if (!books.TryGetValue(id, out var existing))
            {
                order.Add(id);
                existing = (null, null);
            }

            books[id] = (existing.Title ?? title, existing.Cover ?? cover);
        }

        return order
            .Where(id => books[id].Title is not null)
            .Select(id => new ReadingCatalogCandidate(
                ProviderKey,
                id,
                books[id].Title!,
                NativeTitle: null,
                Author: null,
                books[id].Cover,
                FirstPublishYear: null,
                Status: null,
                VolumeCount: null,
                ChapterCount: null,
                $"{Origin}/book/{id}",
                IsPublicWebSource: false))
            .ToArray();
    }

    /// <summary>The first image on WebNovel's own hosts; lazy-load placeholders are skipped.</summary>
    private static string? ReadCover(string inner)
    {
        foreach (Match image in ImageSourceRegex().Matches(inner))
        {
            if (Uri.TryCreate(
                    WebUtility.HtmlDecode(image.Groups["url"].Value),
                    UriKind.Absolute,
                    out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps &&
                uri.Host.EndsWith(".webnovel.com", StringComparison.OrdinalIgnoreCase))
            {
                return uri.AbsoluteUri;
            }
        }

        return null;
    }

    private static string? FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => value.Length > 0);

    private static string CleanText(string value) =>
        WhitespaceRegex().Replace(WebUtility.HtmlDecode(value), " ").Trim();

    [GeneratedRegex(
        @"<a\b(?<attributes>[^>]*?\bhref\s*=\s*""(?:https?://(?:www\.)?webnovel\.com)?/book/(?:[^""?#/]*?_)?(?<id>\d{8,25})(?:[/?#][^""]*)?""[^>]*)>(?<inner>.*?)</a>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex BookAnchorRegex();

    [GeneratedRegex(
        @"(?<![\w-])title\s*=\s*""(?<value>[^""]*)""",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex TitleAttributeRegex();

    [GeneratedRegex(
        @"<img\b[^>]*?(?<![\w-])alt\s*=\s*""(?<value>[^""]*)""",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ImageAltRegex();

    [GeneratedRegex(
        @"(?<![\w-])(?:data-src|data-original|src)\s*=\s*""(?<url>https://[^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ImageSourceRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WhitespaceRegex();
}
