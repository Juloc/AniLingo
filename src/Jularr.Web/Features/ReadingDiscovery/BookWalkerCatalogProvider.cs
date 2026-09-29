using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Jularr.Web.Features.ReadingDiscovery;

/// <summary>
/// BOOK☆WALKER (bookwalker.jp) as a Light Novel reference source. BOOK☆WALKER documents no
/// public API, so this reads the public search page a visitor would open: one request per
/// category, at most two per search, at the paths its robots.txt allows. Results are
/// listings only. They link to the store's own series or book page, where its official trial
/// reading lives; Jularr never requests trial or sample URLs, never touches DRM-protected
/// content and never imports anything from here.
/// </summary>
public sealed partial class BookWalkerCatalogProvider(HttpClient client) : IReadingCatalogProvider
{
    public const string ProviderKey = "bookwalker";

    private const string SourceName = "BOOK☆WALKER";
    private const string Origin = "https://bookwalker.jp";

    // The search page's own category ids: 3 = ライトノベル, 9 = 新文芸 (web-novel-born
    // novels such as Overlord). Everything else on the store is manga, magazines or games.
    private static readonly int[] NovelCategories = [3, 9];

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

        var perCategory = new List<IReadOnlyList<ReadingCatalogCandidate>>();
        foreach (var category in NovelCategories)
        {
            // An empty search answers HTTP 404 with a normal page.
            var html = await ReadingSourceHttp.GetStringAsync(
                client,
                BuildSearchUri(normalized, category),
                SourceName,
                "text/html",
                notFoundIsEmpty: true,
                cancellationToken);

            perCategory.Add(html is null ? [] : ParseSearchResults(html));
        }

        // Interleave the categories in the store's own relevance order so one category
        // cannot fill the limit on its own.
        var results = new List<ReadingCatalogCandidate>();
        for (var index = 0; perCategory.Any(list => index < list.Count); index++)
        {
            results.AddRange(
                perCategory
                    .Where(list => index < list.Count)
                    .Select(list => list[index]));
        }

        return results
            .GroupBy(candidate => candidate.Identity, StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(Math.Clamp(limit, 1, 24))
            .ToArray();
    }

    internal static Uri BuildSearchUri(
        string query,
        int category) =>
        new(
            $"{Origin}/search/?word={Uri.EscapeDataString(query)}" +
            $"&qcat={category.ToString(CultureInfo.InvariantCulture)}&order=score");

    internal static IReadOnlyList<ReadingCatalogCandidate> ParseSearchResults(
        string html)
    {
        var results = new List<ReadingCatalogCandidate>();

        // Each result is one <li class="m-tile">; the first chunk is the page before it.
        foreach (var tile in html.Split("<li class=\"m-tile\">").Skip(1))
        {
            var anchor = TitleAnchorRegex().Match(tile);
            if (!anchor.Success)
            {
                continue;
            }

            var attributes = anchor.Groups["attributes"].Value;
            var titleAttribute = TitleAttributeRegex().Match(attributes);
            var href = HrefAttributeRegex().Match(attributes);
            var title = CleanText(
                titleAttribute.Success
                    ? titleAttribute.Groups["value"].Value
                    : anchor.Groups["text"].Value);

            if (title.Length == 0 ||
                !href.Success ||
                !TryReadIdentity(
                    href.Groups["value"].Value,
                    out var externalId,
                    out var sourceUrl))
            {
                continue;
            }

            var volumes = VolumeCountRegex().Match(tile);

            results.Add(
                new ReadingCatalogCandidate(
                    ProviderKey,
                    externalId,
                    title,
                    NativeTitle: null,
                    Author: null,
                    ReadCover(tile),
                    FirstPublishYear: null,
                    tile.Contains("a-tag-comp", StringComparison.Ordinal)
                        ? "FINISHED"
                        : null,
                    volumes.Success
                        ? int.Parse(volumes.Groups["count"].Value, CultureInfo.InvariantCulture)
                        : null,
                    ChapterCount: null,
                    sourceUrl,
                    IsPublicWebSource: false));
        }

        return results
            .GroupBy(candidate => candidate.Identity, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    /// <summary>
    /// Accepts only the store's own series and book pages, so a result can never point
    /// anywhere else.
    /// </summary>
    private static bool TryReadIdentity(
        string href,
        out string externalId,
        out string sourceUrl)
    {
        externalId = "";
        sourceUrl = "";

        if (!Uri.TryCreate(new Uri(Origin), WebUtility.HtmlDecode(href), out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("bookwalker.jp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var series = SeriesPathRegex().Match(uri.AbsolutePath);
        if (series.Success)
        {
            var id = series.Groups["id"].Value;
            externalId = $"series-{id}";
            sourceUrl = $"{Origin}/series/{id}/list/";
            return true;
        }

        var book = BookPathRegex().Match(uri.AbsolutePath);
        if (book.Success)
        {
            var uuid = book.Groups["uuid"].Value.ToLowerInvariant();
            externalId = $"book-{uuid}";
            sourceUrl = $"{Origin}/de{uuid}/";
            return true;
        }

        return false;
    }

    private static string? ReadCover(string tile)
    {
        var cover = CoverRegex().Match(tile);
        return cover.Success &&
               Uri.TryCreate(cover.Groups["url"].Value, UriKind.Absolute, out var uri) &&
               uri.Scheme == Uri.UriSchemeHttps &&
               uri.Host.EndsWith(".bookwalker.jp", StringComparison.OrdinalIgnoreCase)
            ? uri.AbsoluteUri
            : null;
    }

    private static string CleanText(string value) =>
        WhitespaceRegex().Replace(
            TagRegex().Replace(WebUtility.HtmlDecode(value), " "),
            " ").Trim();

    [GeneratedRegex(
        @"<a\b(?<attributes>[^>]*?\bclass\s*=\s*""m-book-item__title""[^>]*)>(?<text>.*?)</a>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex TitleAnchorRegex();

    [GeneratedRegex(
        @"(?<![\w-])title\s*=\s*""(?<value>[^""]*)""",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex TitleAttributeRegex();

    [GeneratedRegex(
        @"(?<![\w-])href\s*=\s*""(?<value>[^""]*)""",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex HrefAttributeRegex();

    [GeneratedRegex(
        @"data-original\s*=\s*""(?<url>[^""]+)""",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex CoverRegex();

    [GeneratedRegex(
        @"シリーズ\s*(?<count>\d{1,4})\s*冊",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex VolumeCountRegex();

    [GeneratedRegex(
        @"^/series/(?<id>\d{1,10})(?:/|$)",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex SeriesPathRegex();

    [GeneratedRegex(
        @"^/de(?<uuid>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})(?:/|$)",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex BookPathRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WhitespaceRegex();
}
