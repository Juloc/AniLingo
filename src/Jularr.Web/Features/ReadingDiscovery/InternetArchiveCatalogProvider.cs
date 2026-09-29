using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jularr.Web.Features.ReadingDiscovery;

/// <summary>
/// Internet Archive as a rights-aware reference source, through its documented advanced
/// search API. Anyone can upload to the Archive, so nothing found here is treated as a
/// trusted full-text source: every result is a link to the item's own page and is never
/// imported. Access flags decide what is listed at all:
/// <list type="bullet">
/// <item>Access-restricted items are listed only when the Archive lends them (the
/// <c>inlibrary</c> lending collection); everything else restricted is left out.</item>
/// <item>Unrestricted items are listed, labelled by their rights evidence: a Creative Commons
/// or public-domain statement, or none ("rights not verified").</item>
/// <item>Items in the <c>no-preview</c> collection are left out.</item>
/// </list>
/// Jularr never downloads or borrows anything on the user's behalf and never bypasses the
/// Archive's lending or login.
/// </summary>
public sealed partial class InternetArchiveCatalogProvider(HttpClient client) : IReadingCatalogProvider
{
    public const string ProviderKey = "internetarchive";

    private const string SourceName = "Internet Archive";
    private const string Origin = "https://archive.org";
    private const string LendingCollection = "inlibrary";
    private const string NoPreviewCollection = "no-preview";
    private const int MaximumQueryTerms = 8;

    private static readonly string[] Fields =
    [
        "identifier",
        "title",
        "creator",
        "year",
        "date",
        "access-restricted-item",
        "collection",
        "licenseurl",
        "rights"
    ];

    public string Key => ProviderKey;

    public async Task<IReadOnlyList<ReadingCatalogCandidate>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var searchQuery = BuildLuceneQuery(query);
        if (searchQuery is null)
        {
            return [];
        }

        var bounded = Math.Clamp(limit, 1, 24);
        var json = await ReadingSourceHttp.GetStringAsync(
            client,
            BuildSearchUri(searchQuery, bounded * 2),
            SourceName,
            "application/json",
            notFoundIsEmpty: false,
            cancellationToken);

        return ParseResponse(json ?? "")
            .Take(bounded)
            .ToArray();
    }

    /// <summary>
    /// Title words only, each quoted, so the user's text can never inject search syntax.
    /// Returns null when no usable word remains.
    /// </summary>
    internal static string? BuildLuceneQuery(string? query)
    {
        var terms = QueryTermRegex()
            .Matches(query ?? "")
            .Select(match => match.Value)
            .Take(MaximumQueryTerms)
            .ToArray();

        return terms.Length == 0
            ? null
            : $"title:({string.Join(" AND ", terms.Select(term => $"\"{term}\""))}) AND mediatype:texts";
    }

    internal static Uri BuildSearchUri(
        string luceneQuery,
        int rows)
    {
        var parameters = new List<string>
        {
            $"q={Uri.EscapeDataString(luceneQuery)}"
        };
        parameters.AddRange(
            Fields.Select(field => $"fl%5B%5D={Uri.EscapeDataString(field)}"));
        parameters.Add(
            $"rows={Math.Clamp(rows, 1, 48).ToString(CultureInfo.InvariantCulture)}");
        parameters.Add("output=json");

        return new Uri($"{Origin}/advancedsearch.php?{string.Join("&", parameters)}");
    }

    internal static IReadOnlyList<ReadingCatalogCandidate> ParseResponse(
        string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("response", out var response) ||
            !response.TryGetProperty("docs", out var docs) ||
            docs.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException(
                "Internet Archive search response had no result list.");
        }

        var results = new List<ReadingCatalogCandidate>();
        foreach (var doc in docs.EnumerateArray())
        {
            if (doc.ValueKind != JsonValueKind.Object ||
                ReadStrings(doc, "identifier").FirstOrDefault() is not { } identifier ||
                !IdentifierRegex().IsMatch(identifier) ||
                ReadStrings(doc, "title").FirstOrDefault() is not { } title)
            {
                continue;
            }

            var access = Classify(
                ReadStrings(doc, "collection"),
                ReadStrings(doc, "access-restricted-item")
                    .Any(value => value.Equals("true", StringComparison.OrdinalIgnoreCase)),
                ReadStrings(doc, "licenseurl").FirstOrDefault(),
                ReadStrings(doc, "rights").FirstOrDefault());
            if (access is null)
            {
                continue;
            }

            results.Add(
                new ReadingCatalogCandidate(
                    ProviderKey,
                    identifier,
                    title,
                    NativeTitle: null,
                    ReadStrings(doc, "creator").FirstOrDefault(),
                    $"{Origin}/services/img/{identifier}",
                    ReadYear(doc),
                    Status: null,
                    VolumeCount: null,
                    ChapterCount: null,
                    $"{Origin}/details/{identifier}",
                    IsPublicWebSource: false,
                    access.Value));
        }

        return results
            .GroupBy(candidate => candidate.Identity, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    /// <summary>
    /// The single rights rule. Returns null for an item that must not be listed.
    /// </summary>
    internal static ReadingAccess? Classify(
        IReadOnlyList<string> collections,
        bool accessRestricted,
        string? licenseUrl,
        string? rights)
    {
        var inCollection = (string name) =>
            collections.Contains(name, StringComparer.OrdinalIgnoreCase);

        if (accessRestricted)
        {
            // Restricted items are only ever reachable through the Archive's own lending.
            return inCollection(LendingCollection)
                ? ReadingAccess.Lendable
                : null;
        }

        if (inCollection(NoPreviewCollection))
        {
            return null;
        }

        return HasRightsEvidence(licenseUrl, rights)
            ? ReadingAccess.OpenLicense
            : ReadingAccess.OpenUnverified;
    }

    private static bool HasRightsEvidence(
        string? licenseUrl,
        string? rights)
    {
        if (Uri.TryCreate(licenseUrl, UriKind.Absolute, out var uri) &&
            (uri.Host.Equals("creativecommons.org", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith(".creativecommons.org", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return rights?.Contains("public domain", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static int? ReadYear(JsonElement doc)
    {
        var year = ReadStrings(doc, "year").FirstOrDefault();
        if (int.TryParse(year, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) &&
            parsed is > 0 and < 3000)
        {
            return parsed;
        }

        var date = ReadStrings(doc, "date").FirstOrDefault();
        return date is { Length: >= 4 } &&
               int.TryParse(
                   date.AsSpan(0, 4),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out var fromDate) &&
               fromDate is > 0 and < 3000
            ? fromDate
            : null;
    }

    /// <summary>Metadata fields are a string, a number, a bool or a list of them.</summary>
    private static IReadOnlyList<string> ReadStrings(
        JsonElement doc,
        string name)
    {
        if (!doc.TryGetProperty(name, out var value))
        {
            return [];
        }

        var values = value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : Enumerate(value);

        return values
            .Select(item => item.ValueKind switch
            {
                JsonValueKind.String => item.GetString(),
                JsonValueKind.Number => item.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            })
            .Select(text => text?.Trim())
            .OfType<string>()
            .Where(text => text.Length > 0)
            .ToArray();
    }

    private static IEnumerable<JsonElement> Enumerate(JsonElement value)
    {
        yield return value;
    }

    [GeneratedRegex(
        @"[\p{L}\p{N}]+",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex QueryTermRegex();

    [GeneratedRegex(
        @"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex IdentifierRegex();
}
