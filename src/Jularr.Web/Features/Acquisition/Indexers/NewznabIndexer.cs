using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Xml.Linq;
using Jularr.Web.Features.Acquisition.Prowlarr;
using Jularr.Web.Features.Providers;

namespace Jularr.Web.Features.Acquisition.Indexers;

/// <summary>
/// Direct Newznab (usenet) indexer client using the caps/search XML API.
/// Every result carries the usenet protocol per
/// <see cref="ProwlarrReleaseCandidate.Protocol"/>. Its HTTP calls run through the
/// shared <see cref="ProviderExecutor"/> (#438) for timeouts and bounded retries;
/// per-entry health stays in <c>AcquisitionHealthStore</c>, so framework health
/// tracking is left off here (see <see cref="ExecutionPolicy"/>).
/// </summary>
public sealed class NewznabIndexer(HttpClient httpClient, ProviderExecutor executor) : IIndexer, IExternalProvider
{
    /// <summary>
    /// Behaviour-preserving policy: retries only transient network faults (one extra attempt) so
    /// every HTTP status is still surfaced to the caller exactly as before. Per-entry health is
    /// owned by <c>AcquisitionHealthStore</c>, so framework health/circuit is disabled here to
    /// keep a single source of truth for indexer health.
    /// </summary>
    public static readonly ProviderExecutionPolicy ExecutionPolicy = new()
    {
        MaxAttempts = 2,
        BaseBackoff = TimeSpan.FromMilliseconds(250),
        RetryServerErrors = false,
        HonorRateLimitGate = false,
        TrackHealth = false,
        ShortCircuitWhenUnavailable = false
    };

    public IndexerType Type => IndexerType.Newznab;

    public ExternalProviderDescriptor Descriptor { get; } =
        new(ProviderKeys.Newznab, "Newznab indexer", ProviderCapabilities.Search);

    public async Task<IndexerConnectionTestResult> TestAsync(
        IndexerEntry entry,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await executor.SendAsync(
                ProviderKeys.Newznab,
                httpClient,
                () => CreateRequest(entry, "caps", []),
                ExecutionPolicy,
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

        using var response = await executor.SendAsync(
            ProviderKeys.Newznab,
            httpClient,
            () => CreateRequest(entry, "search", parameters),
            ExecutionPolicy,
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
        const string protocol = "usenet";
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
