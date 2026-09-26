using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Xml.Linq;
using AniLingo.Web.Features.Acquisition.Prowlarr;

namespace AniLingo.Web.Features.Acquisition.Indexers;

/// <summary>
/// Direct Newznab (usenet) or Torznab (torrent) indexer client. Both use the
/// same caps/search XML API; only the release protocol of the results
/// differs, carried per <see cref="ProwlarrReleaseCandidate.Protocol"/>.
/// </summary>
public sealed class NewznabIndexer(HttpClient httpClient) : IIndexer
{
    public IndexerType Type => IndexerType.Newznab;

    public async Task<IndexerConnectionTestResult> TestAsync(
        IndexerEntry entry,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = CreateRequest(entry, "caps", []);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new IndexerConnectionTestResult(false, Error: DescribeStatus(response.StatusCode));
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var document = XDocument.Parse(body);
            if (!string.Equals(document.Root?.Name.LocalName, "caps", StringComparison.OrdinalIgnoreCase))
            {
                return new IndexerConnectionTestResult(false, Error: "Indexer did not return a caps document.");
            }

            var version = document.Root!
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName == "server")?
                .Attribute("version")?.Value;

            return new IndexerConnectionTestResult(true, version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
            System.Xml.XmlException or
            UriFormatException)
        {
            return new IndexerConnectionTestResult(
                false,
                Error: "The indexer could not be reached or returned an invalid response.");
        }
    }

    public async Task<IReadOnlyList<ProwlarrReleaseCandidate>> SearchAsync(
        IndexerEntry entry,
        IndexerSearchQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.Query))
        {
            return [];
        }

        var parameters = new List<KeyValuePair<string, string>> { new("q", query.Query) };
        parameters.AddRange(
            entry.Settings.Categories.Select(
                category => new KeyValuePair<string, string>("cat", category.ToString(CultureInfo.InvariantCulture))));
        parameters.Add(new("limit", entry.Settings.SearchLimit.ToString(CultureInfo.InvariantCulture)));

        using var request = CreateRequest(entry, "search", parameters);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new IndexerException($"'{entry.Name}' search failed with HTTP {(int)response.StatusCode}.");
        }

        try
        {
            return ParseSearchResponse(entry, query.Query, body);
        }
        catch (System.Xml.XmlException exception)
        {
            throw new IndexerException($"'{entry.Name}' returned invalid search XML.", exception);
        }
    }

    public static IReadOnlyList<ProwlarrReleaseCandidate> ParseSearchResponse(
        IndexerEntry entry,
        string query,
        string xml)
    {
        var document = XDocument.Parse(xml);
        var items = document.Descendants().Where(element => element.Name.LocalName == "item");
        var releases = new List<ProwlarrReleaseCandidate>();
        var protocol = entry.Protocol;
        var now = DateTimeOffset.UtcNow;

        foreach (var item in items)
        {
            var title = Text(item, "title");
            if (string.IsNullOrWhiteSpace(title) || !AnimeReleaseParser.TryParse(title, out var parsed))
            {
                continue;
            }

            var attrs = item.Elements()
                .Where(element => element.Name.LocalName == "attr")
                .ToDictionary(
                    element => element.Attribute("name")?.Value ?? string.Empty,
                    element => element.Attribute("value")?.Value ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase);

            var enclosure = item.Elements().FirstOrDefault(element => element.Name.LocalName == "enclosure");
            var link = enclosure?.Attribute("url")?.Value ?? Text(item, "link");
            var guid = Text(item, "guid");
            var publishedAt = ParseDate(Text(item, "pubDate"));
            var size = ReadLong(attrs, "size") ?? ReadLong(enclosure?.Attribute("length")?.Value);
            var seeders = ReadInt(attrs, "seeders");
            var peers = ReadInt(attrs, "peers");
            var leechers = ReadInt(attrs, "leechers") ?? (peers is int p && seeders is int s ? Math.Max(0, p - s) : null);

            Uri? downloadUri = null;
            string? magnetUri = null;
            if (!string.IsNullOrWhiteSpace(link))
            {
                if (link.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                {
                    magnetUri = link;
                }
                else
                {
                    downloadUri = ResolveInternalUri(entry.Settings.BaseUrl, link);
                }
            }

            releases.Add(
                new ProwlarrReleaseCandidate(
                    title,
                    entry.Name,
                    null,
                    protocol,
                    size,
                    seeders,
                    leechers,
                    publishedAt,
                    publishedAt is { } published ? (int)Math.Max(0, (now - published).TotalDays) : null,
                    publishedAt is { } publishedH ? Math.Max(0, (now - publishedH).TotalHours) : null,
                    string.IsNullOrWhiteSpace(guid) ? null : guid,
                    Text(item, "comments") ?? Text(item, "link"),
                    parsed,
                    [query],
                    downloadUri,
                    magnetUri));
        }

        return releases;
    }

    private static HttpRequestMessage CreateRequest(
        IndexerEntry entry,
        string operation,
        IReadOnlyList<KeyValuePair<string, string>> parameters)
    {
        var query = new List<KeyValuePair<string, string>>
        {
            new("t", operation),
            new("apikey", entry.ApiKey.Trim()),
            new("o", "xml")
        };
        query.AddRange(parameters);

        var uri = new Uri(
            $"{entry.Settings.BaseUrl}/api?" +
            string.Join(
                "&",
                query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}")),
            UriKind.Absolute);

        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        return request;
    }

    private static string? Text(XElement item, string localName) =>
        item.Elements()
            .FirstOrDefault(element => element.Name.LocalName == localName)?
            .Value.Trim() is { Length: > 0 } value
            ? value
            : null;

    private static long? ReadLong(IReadOnlyDictionary<string, string> attrs, string name) =>
        attrs.TryGetValue(name, out var value) ? ReadLong(value) : null;

    private static long? ReadLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static int? ReadInt(IReadOnlyDictionary<string, string> attrs, string name) =>
        attrs.TryGetValue(name, out var value) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;

    private static Uri? ResolveInternalUri(string baseUrl, string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute;
        }

        return Uri.TryCreate(
            $"{baseUrl.TrimEnd('/')}/{value.TrimStart('/')}",
            UriKind.Absolute,
            out var relative) &&
            (relative.Scheme == Uri.UriSchemeHttp || relative.Scheme == Uri.UriSchemeHttps)
            ? relative
            : null;
    }

    private static string DescribeStatus(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "The indexer rejected the API key.",
            HttpStatusCode.NotFound => "The indexer API endpoint was not found. Check the Base URL.",
            _ => $"The indexer returned HTTP {(int)statusCode}."
        };
}

/// <summary>Torznab is the torrent-protocol twin of Newznab; the wire format is identical.</summary>
public sealed class TorznabIndexer(HttpClient httpClient) : IIndexer
{
    private readonly NewznabIndexer inner = new(httpClient);

    public IndexerType Type => IndexerType.Torznab;

    public Task<IndexerConnectionTestResult> TestAsync(
        IndexerEntry entry,
        CancellationToken cancellationToken) =>
        inner.TestAsync(entry, cancellationToken);

    public Task<IReadOnlyList<ProwlarrReleaseCandidate>> SearchAsync(
        IndexerEntry entry,
        IndexerSearchQuery query,
        CancellationToken cancellationToken) =>
        inner.SearchAsync(entry, query, cancellationToken);
}
