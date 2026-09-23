using System.Net;
using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Novels;

public sealed partial class NcodeNovelSourceProvider(
    HttpClient httpClient,
    ILogger<NcodeNovelSourceProvider> logger) : INovelSourceProvider
{
    public const string ProviderKey = "syosetu";
    private const int MaxTocPages = 50;

    public string Key => ProviderKey;

    public bool CanHandle(Uri sourceUri) =>
        sourceUri.Scheme is "http" or "https" &&
        sourceUri.Host.Equals("ncode.syosetu.com", StringComparison.OrdinalIgnoreCase) &&
        TryGetSourceKey(sourceUri, out _);

    public async Task<NovelSourceWorkSnapshot> GetWorkAsync(
        Uri sourceUri,
        CancellationToken cancellationToken)
    {
        if (!TryGetSourceKey(sourceUri, out var sourceKey))
        {
            throw new InvalidOperationException("Unsupported Narou/Ncode URL.");
        }

        var workUrl = new Uri($"https://ncode.syosetu.com/{sourceKey}/");
        var firstHtml = await GetStringAsync(workUrl, cancellationToken);
        var title = ParseWorkTitle(firstHtml, sourceKey);
        var author = ParseAuthor(firstHtml);
        var description = ParseDescription(firstHtml);

        var chapters = new Dictionary<int, NovelSourceChapterReference>();
        AddChapterLinks(firstHtml, sourceKey, chapters);

        var currentPage = 1;
        var currentHtml = firstHtml;

        while (currentPage < MaxTocPages)
        {
            var nextPage = FindNextTocPage(currentHtml, currentPage);
            if (nextPage is null)
            {
                break;
            }

            var pageUri = new Uri($"{workUrl}?p={nextPage.Value}");
            currentHtml = await GetStringAsync(pageUri, cancellationToken);
            currentPage = nextPage.Value;

            var before = chapters.Count;
            AddChapterLinks(currentHtml, sourceKey, chapters);

            if (chapters.Count == before)
            {
                break;
            }
        }

        if (chapters.Count == 0)
        {
            throw new InvalidOperationException("No chapters were found on the Narou work page.");
        }

        return new NovelSourceWorkSnapshot(
            ProviderKey,
            sourceKey,
            workUrl.ToString(),
            title,
            author,
            description,
            chapters.Values.OrderBy(x => x.Number).ToArray());
    }

    public async Task<NovelSourceChapterSnapshot> GetChapterAsync(
        Uri sourceUri,
        CancellationToken cancellationToken)
    {
        if (!TryGetChapterIdentity(sourceUri, out var sourceKey, out var number))
        {
            throw new InvalidOperationException("The URL does not identify a Narou chapter.");
        }

        var canonical = new Uri($"https://ncode.syosetu.com/{sourceKey}/{number}/");
        var html = await GetStringAsync(canonical, cancellationToken);
        return ParseChapterHtml(html, sourceKey, number, canonical.ToString());
    }

    public static NovelSourceChapterSnapshot ParseChapterHtml(
        string html,
        string sourceKey,
        int number,
        string sourceUrl)
    {
        var title = FirstMatch(ChapterTitleRegex(), html)
            ?? FirstMatch(DocumentTitleRegex(), html)?.Split(" - ", 2, StringSplitOptions.TrimEntries)[0]
            ?? $"Chapter {number}";

        var body = FirstMatch(MainBodyRegex(), html);
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException(
                $"Narou chapter {sourceKey}/{number} did not contain readable novel text.");
        }

        var text = HtmlToText(body);
        if (text.Length == 0)
        {
            throw new InvalidOperationException(
                $"Narou chapter {sourceKey}/{number} contained no readable text.");
        }

        return new NovelSourceChapterSnapshot(
            number,
            CleanInlineText(title),
            sourceUrl,
            text);
    }

    public static IReadOnlyList<NovelSourceChapterReference> ParseChapterLinks(
        string html,
        string sourceKey)
    {
        var result = new Dictionary<int, NovelSourceChapterReference>();
        AddChapterLinks(html, sourceKey, result);
        return result.Values.OrderBy(x => x.Number).ToArray();
    }

    private async Task<string> GetStringAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(uri, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Narou request failed for {Url}.", uri);
            throw new InvalidOperationException(
                "The Narou source is currently unavailable.",
                exception);
        }
    }

    private static void AddChapterLinks(
        string html,
        string sourceKey,
        IDictionary<int, NovelSourceChapterReference> target)
    {
        foreach (Match match in ChapterLinkRegex().Matches(html))
        {
            var key = match.Groups["key"].Value;
            if (!key.Equals(sourceKey, StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(match.Groups["number"].Value, out var number) ||
                number <= 0)
            {
                continue;
            }

            var title = HtmlToText(match.Groups["title"].Value)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();

            if (title.Length == 0)
            {
                title = $"Chapter {number}";
            }

            target[number] = new NovelSourceChapterReference(
                number,
                title,
                $"https://ncode.syosetu.com/{sourceKey}/{number}/");
        }
    }

    private static string ParseWorkTitle(string html, string sourceKey)
    {
        var title = FirstMatch(WorkTitleRegex(), html)
            ?? FirstMatch(OpenGraphTitleRegex(), html)
            ?? FirstMatch(DocumentTitleRegex(), html)
            ?? sourceKey;

        title = CleanInlineText(title);
        var separator = title.IndexOf(" - ", StringComparison.Ordinal);
        return separator > 0 ? title[..separator].Trim() : title;
    }

    private static string? ParseAuthor(string html)
    {
        var author = FirstMatch(AuthorRegex(), html);
        if (string.IsNullOrWhiteSpace(author))
        {
            return null;
        }

        author = CleanInlineText(author)
            .Replace("作者：", "", StringComparison.Ordinal)
            .Replace("作者:", "", StringComparison.Ordinal)
            .Trim();

        return author.Length == 0 ? null : author;
    }

    private static string? ParseDescription(string html)
    {
        var description = FirstMatch(DescriptionMetaRegex(), html)
            ?? FirstMatch(SummaryRegex(), html);

        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var clean = HtmlToText(description);
        return clean.Length == 0 ? null : clean;
    }

    private static bool TryGetSourceKey(Uri uri, out string sourceKey)
    {
        var match = SourceKeyRegex().Match(uri.AbsolutePath);
        if (!match.Success)
        {
            sourceKey = "";
            return false;
        }

        sourceKey = match.Groups["key"].Value.ToLowerInvariant();
        return true;
    }

    private static bool TryGetChapterIdentity(
        Uri uri,
        out string sourceKey,
        out int number)
    {
        var match = ChapterPathRegex().Match(uri.AbsolutePath);
        if (!match.Success ||
            !int.TryParse(match.Groups["number"].Value, out number))
        {
            sourceKey = "";
            number = 0;
            return false;
        }

        sourceKey = match.Groups["key"].Value.ToLowerInvariant();
        return number > 0;
    }

    private static int? FindNextTocPage(string html, int currentPage)
    {
        var nextPage = TocPageLinkRegex()
            .Matches(html)
            .Select(match =>
                int.TryParse(match.Groups["page"].Value, out var page)
                    ? page
                    : 0)
            .Where(page => page > currentPage && page <= MaxTocPages)
            .DefaultIfEmpty()
            .Min();

        return nextPage > currentPage ? nextPage : null;
    }

    private static string? FirstMatch(Regex regex, string input)
    {
        var match = regex.Match(input);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["value"].Value) : null;
    }

    private static string CleanInlineText(string value) =>
        HtmlToText(value)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

    private static string HtmlToText(string html)
    {
        var value = RubyAnnotationRegex().Replace(html, "");
        value = BreakRegex().Replace(value, "\n");
        value = ParagraphEndRegex().Replace(value, "\n");
        value = TagRegex().Replace(value, "");
        value = WebUtility.HtmlDecode(value)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var lines = value
            .Split('\n')
            .Select(line => line.Trim(' ', '\t'))
            .ToList();

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return ExcessBlankLinesRegex()
            .Replace(string.Join("\n", lines), "\n\n")
            .Trim();
    }

    [GeneratedRegex(@"^/(?<key>n[0-9a-z]+)/?", RegexOptions.IgnoreCase)]
    private static partial Regex SourceKeyRegex();

    [GeneratedRegex(@"^/(?<key>n[0-9a-z]+)/(?<number>\d+)/?", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterPathRegex();

    [GeneratedRegex(
        @"<a\b[^>]*href\s*=\s*[""']/(?:novel/)?(?<key>n[0-9a-z]+)/(?<number>\d+)/?[""'][^>]*>(?<title>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ChapterLinkRegex();

    [GeneratedRegex(
        @"href\s*=\s*[""'][^""']*[?&](?:amp;)?p=(?<page>\d+)[^""']*[""']",
        RegexOptions.IgnoreCase)]
    private static partial Regex TocPageLinkRegex();

    [GeneratedRegex(
        @"<h1\b[^>]*class\s*=\s*[""'][^""']*p-novel__title[^""']*[""'][^>]*>(?<value>.*?)</h1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex WorkTitleRegex();

    [GeneratedRegex(
        @"<h1\b[^>]*class\s*=\s*[""'][^""']*p-novel__title[^""']*[""'][^>]*>(?<value>.*?)</h1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ChapterTitleRegex();

    [GeneratedRegex(
        @"<title\b[^>]*>(?<value>.*?)</title>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DocumentTitleRegex();

    [GeneratedRegex(
        @"<meta\b[^>]*property\s*=\s*[""']og:title[""'][^>]*content\s*=\s*[""'](?<value>.*?)[""'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex OpenGraphTitleRegex();

    [GeneratedRegex(
        @"<[^>]+class\s*=\s*[""'][^""']*p-novel__author[^""']*[""'][^>]*>(?<value>.*?)</[^>]+>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AuthorRegex();

    [GeneratedRegex(
        @"<meta\b[^>]*name\s*=\s*[""']description[""'][^>]*content\s*=\s*[""'](?<value>.*?)[""'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DescriptionMetaRegex();

    [GeneratedRegex(
        @"<[^>]+class\s*=\s*[""'][^""']*p-novel__summary[^""']*[""'][^>]*>(?<value>.*?)</[^>]+>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SummaryRegex();

    [GeneratedRegex(
        @"<div\b(?=[^>]*(?:id\s*=\s*[""']novel_honbun[""']|class\s*=\s*[""'][^""']*p-novel__body[^""']*[""']))[^>]*>(?<value>.*?)</div>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex MainBodyRegex();

    [GeneratedRegex(@"<(?:rt|rp)\b[^>]*>.*?</(?:rt|rp)>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex RubyAnnotationRegex();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakRegex();

    [GeneratedRegex(@"</p\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphEndRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\n(?:[ \t]*\n){2,}")]
    private static partial Regex ExcessBlankLinesRegex();
}
