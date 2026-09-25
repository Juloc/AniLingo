using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AniLingo.Web.Features.Books;

public sealed record BookOpdsCatalogItem(
    string SourceId,
    string SourceName,
    string Key,
    string Title,
    string? Author,
    string? Description,
    string? Language,
    string? CoverImageUrl);

public sealed partial class BookCatalogService
{
    private const int MaxOpdsCatalogBytes = 5 * 1024 * 1024;
    private const int MaxOpdsResultsPerSource = 32;
    private static readonly TimeSpan OpdsRequestTimeout =
        TimeSpan.FromSeconds(12);

    public IReadOnlyList<BookOpdsSourceSettings> GetOpdsSources() =>
        BookOpdsSettingsStore.Load(
            GetOpdsSettingsPath());

    public Task<BookOpdsSourceSettings> SaveOpdsSourceAsync(
        string? id,
        string name,
        string url,
        string? username,
        string? password,
        bool isEnabled,
        bool preserveExistingPassword,
        CancellationToken cancellationToken) =>
        BookOpdsSettingsStore.UpsertAsync(
            id,
            name,
            url,
            username,
            password,
            isEnabled,
            preserveExistingPassword,
            cancellationToken,
            GetOpdsSettingsPath());

    public Task RemoveOpdsSourceAsync(
        string id,
        CancellationToken cancellationToken) =>
        BookOpdsSettingsStore.RemoveAsync(
            id,
            cancellationToken,
            GetOpdsSettingsPath());

    public async Task<string> TestOpdsSourceAsync(
        string sourceId,
        CancellationToken cancellationToken)
    {
        var source = FindOpdsSource(
            sourceId,
            requireEnabled: false);

        var document = await LoadOpdsDocumentAsync(
            source,
            new Uri(source.Url, UriKind.Absolute),
            cancellationToken);

        return $"Connected to {source.Name}. "
            + $"{document.Items.Count} EPUB item(s) visible on the catalog page.";
    }

    public async Task<IReadOnlyList<BookOpdsCatalogItem>> SearchOpdsAsync(
        string? sourceId,
        string? query,
        CancellationToken cancellationToken)
    {
        var all = BookOpdsSettingsStore.Load(
                GetOpdsSettingsPath())
            .Where(x => x.IsEnabled)
            .ToArray();

        var sources = string.IsNullOrWhiteSpace(sourceId)
            ? all
            : all.Where(x => x.Id.Equals(
                    sourceId.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

        if (sources.Length == 0)
        {
            return [];
        }

        var tasks = sources
            .Take(12)
            .Select(source =>
                CaptureOpdsSearchAsync(
                    source,
                    query,
                    cancellationToken))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        return results
            .SelectMany(x => x)
            .Take(100)
            .Select(x => x.ToPublic())
            .ToArray();
    }

    public async Task<Guid> ImportOpdsBookAsync(
        string sourceId,
        string? query,
        string bookKey,
        CancellationToken cancellationToken)
    {
        var source = FindOpdsSource(
            sourceId,
            requireEnabled: true);

        var items = await SearchSingleOpdsAsync(
            source,
            query,
            cancellationToken);

        var item = items.FirstOrDefault(x =>
            x.Key.Equals(
                bookKey?.Trim(),
                StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                "The OPDS book is no longer present in this catalog result.");

        var bytes = await DownloadOpdsEpubAsync(
            source,
            item.AcquisitionUrl,
            cancellationToken);

        using var stream = new MemoryStream(
            bytes,
            writable: false);
        var parsed = EpubBookParser.Parse(
            stream,
            SanitizeOpdsFileName(item.Title) + ".epub");

        return await ImportParsedBookAsync(
            parsed,
            sourceKey: CleanSourceKey(
                "opds-"
                + source.Id[..12]
                + "-"
                + item.Key),
            sourceUrl: item.AcquisitionUrl.ToString(),
            metadataProvider:
                "opds-" + source.Id[..12],
            metadataExternalId: item.Key,
            coverImageUrl: item.CoverImageUrl,
            fallbackAuthor: item.Author,
            fallbackDescription: item.Description,
            fallbackSubjects: [],
            fileName: SanitizeOpdsFileName(item.Title) + ".epub",
            sourceKind: "opds",
            contentHash: HashBytes(bytes),
            sizeBytes: bytes.LongLength,
            cancellationToken);
    }

    private async Task<IReadOnlyList<ResolvedOpdsItem>> CaptureOpdsSearchAsync(
        BookOpdsSourceSettings source,
        string? query,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SearchSingleOpdsAsync(
                source,
                query,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or InvalidOperationException
                or TaskCanceledException
                or JsonException
                or System.Xml.XmlException)
        {
            return [];
        }
    }

    private async Task<IReadOnlyList<ResolvedOpdsItem>> SearchSingleOpdsAsync(
        BookOpdsSourceSettings source,
        string? query,
        CancellationToken cancellationToken)
    {
        var feedUri = new Uri(
            source.Url,
            UriKind.Absolute);

        var root = await LoadOpdsDocumentAsync(
            source,
            feedUri,
            cancellationToken);

        var normalizedQuery = query?.Trim();

        if (!string.IsNullOrWhiteSpace(normalizedQuery)
            && root.SearchLink is not null)
        {
            var searchUri = await ResolveOpdsSearchUriAsync(
                source,
                feedUri,
                root.SearchLink,
                normalizedQuery,
                cancellationToken);

            if (searchUri is not null)
            {
                root = await LoadOpdsDocumentAsync(
                    source,
                    searchUri,
                    cancellationToken);
            }
        }

        IEnumerable<ResolvedOpdsItem> items =
            root.Items;

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            var terms = normalizedQuery
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries
                        | StringSplitOptions.TrimEntries);

            items = items.Where(item =>
            {
                var haystack =
                    (item.Title
                    + " "
                    + item.Author
                    + " "
                    + item.Description)
                    .ToLowerInvariant();

                return terms.All(term =>
                    haystack.Contains(
                        term.ToLowerInvariant(),
                        StringComparison.Ordinal));
            });
        }

        return items
            .Take(MaxOpdsResultsPerSource)
            .ToArray();
    }

    private async Task<Uri?> ResolveOpdsSearchUriAsync(
        BookOpdsSourceSettings source,
        Uri baseUri,
        OpdsLink searchLink,
        string query,
        CancellationToken cancellationToken)
    {
        var href = searchLink.Href;

        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        if (searchLink.Type?.Contains(
                "opensearchdescription",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            var descriptorUri = ResolveOpdsUri(
                baseUri,
                href);
            var descriptor = await FetchOpdsTextAsync(
                source,
                descriptorUri,
                "application/opensearchdescription+xml, application/xml, text/xml",
                cancellationToken);

            var template = ParseOpenSearchTemplate(
                descriptor);

            return string.IsNullOrWhiteSpace(template)
                ? null
                : ResolveOpdsUri(
                    descriptorUri,
                    ExpandOpenSearchTemplate(
                        template,
                        query));
        }

        if (href.Contains(
                "{searchTerms",
                StringComparison.OrdinalIgnoreCase))
        {
            return ResolveOpdsUri(
                baseUri,
                ExpandOpenSearchTemplate(
                    href,
                    query));
        }

        return ResolveOpdsUri(
            baseUri,
            href);
    }

    private async Task<OpdsDocument> LoadOpdsDocumentAsync(
        BookOpdsSourceSettings source,
        Uri uri,
        CancellationToken cancellationToken)
    {
        var text = await FetchOpdsTextAsync(
            source,
            uri,
            "application/opds+json, application/atom+xml;profile=opds-catalog, application/atom+xml, application/json, application/xml;q=0.8, text/xml;q=0.8",
            cancellationToken);

        var trimmed = text.TrimStart();
        if (trimmed.StartsWith(
            "{",
            StringComparison.Ordinal))
        {
            return ParseOpds2(
                source,
                uri,
                text);
        }

        return ParseOpds1(
            source,
            uri,
            text);
    }

    private async Task<string> FetchOpdsTextAsync(
        BookOpdsSourceSettings source,
        Uri uri,
        string accept,
        CancellationToken cancellationToken)
    {
        if (uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                "OPDS links must use HTTP or HTTPS.");
        }

        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        timeout.CancelAfter(OpdsRequestTimeout);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            uri);
        request.Headers.TryAddWithoutValidation(
            "Accept",
            accept);
        request.Headers.UserAgent.ParseAdd(
            "AniLingo/Books");

        AddOpdsAuthorizationIfAllowed(
            request,
            source,
            uri);

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength
            is > MaxOpdsCatalogBytes)
        {
            throw new InvalidOperationException(
                "OPDS response exceeds the 5 MB catalog limit.");
        }

        await using var stream =
            await response.Content.ReadAsStreamAsync(
                timeout.Token);
        using var memory =
            await CopyToMemoryBoundedAsync(
                stream,
                MaxOpdsCatalogBytes,
                timeout.Token);

        return Encoding.UTF8.GetString(
            memory.ToArray());
    }

    private async Task<byte[]> DownloadOpdsEpubAsync(
        BookOpdsSourceSettings source,
        Uri uri,
        CancellationToken cancellationToken)
    {
        if (uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                "OPDS acquisition link must use HTTP or HTTPS.");
        }

        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        timeout.CancelAfter(EpubDownloadTimeout);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            uri);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/epub+zip"));
        request.Headers.UserAgent.ParseAdd(
            "AniLingo/Books");

        AddOpdsAuthorizationIfAllowed(
            request,
            source,
            uri);

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength
            is > MaxEpubBytes)
        {
            throw new InvalidOperationException(
                "EPUB exceeds the 100 MB import limit.");
        }

        await using var stream =
            await response.Content.ReadAsStreamAsync(
                timeout.Token);
        using var memory =
            await CopyToMemoryBoundedAsync(
                stream,
                MaxEpubBytes,
                timeout.Token);

        var bytes = memory.ToArray();

        if (bytes.Length < 4
            || bytes[0] != (byte)'P'
            || bytes[1] != (byte)'K')
        {
            throw new InvalidOperationException(
                "The OPDS acquisition did not return an EPUB/ZIP file.");
        }

        return bytes;
    }

    private static void AddOpdsAuthorizationIfAllowed(
        HttpRequestMessage request,
        BookOpdsSourceSettings source,
        Uri target)
    {
        if (string.IsNullOrWhiteSpace(source.Username))
        {
            return;
        }

        var sourceUri = new Uri(
            source.Url,
            UriKind.Absolute);

        if (!SameOrigin(
                sourceUri,
                target))
        {
            return;
        }

        var raw =
            source.Username
            + ":"
            + (source.Password ?? "");

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(raw)));
    }

    public static bool IsOpdsCredentialTargetAllowed(
        string sourceUrl,
        string targetUrl)
    {
        return Uri.TryCreate(
                sourceUrl,
                UriKind.Absolute,
                out var source)
            && Uri.TryCreate(
                targetUrl,
                UriKind.Absolute,
                out var target)
            && SameOrigin(
                source,
                target);
    }

    private static bool SameOrigin(
        Uri left,
        Uri right) =>
        left.Scheme.Equals(
            right.Scheme,
            StringComparison.OrdinalIgnoreCase)
        && left.Host.Equals(
            right.Host,
            StringComparison.OrdinalIgnoreCase)
        && left.Port == right.Port;

    private static OpdsDocument ParseOpds2(
        BookOpdsSourceSettings source,
        Uri baseUri,
        string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var searchLink = ParseOpds2Links(
                root,
                baseUri)
            .FirstOrDefault(x =>
                RelContains(
                    x.Rel,
                    "search"));

        var items = new List<ResolvedOpdsItem>();

        if (root.TryGetProperty(
                "publications",
                out var publications)
            && publications.ValueKind
                == JsonValueKind.Array)
        {
            foreach (var publication in
                     publications.EnumerateArray())
            {
                var item = ParseOpds2Publication(
                    source,
                    baseUri,
                    publication);

                if (item is not null)
                {
                    items.Add(item);
                }
            }
        }

        return new OpdsDocument(
            items,
            searchLink);
    }

    private static ResolvedOpdsItem? ParseOpds2Publication(
        BookOpdsSourceSettings source,
        Uri baseUri,
        JsonElement publication)
    {
        if (!publication.TryGetProperty(
                "metadata",
                out var metadata)
            || !metadata.TryGetProperty(
                "title",
                out var titleElement)
            || titleElement.ValueKind
                != JsonValueKind.String)
        {
            return null;
        }

        var links = ParseOpds2Links(
            publication,
            baseUri);

        var acquisition = links
            .FirstOrDefault(x =>
                x.Type?.StartsWith(
                    "application/epub+zip",
                    StringComparison.OrdinalIgnoreCase)
                    == true);

        if (acquisition is null)
        {
            return null;
        }

        var title = titleElement.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var author = ReadOpds2Author(metadata);
        var description =
            ReadJsonString(
                metadata,
                "description");
        var language =
            ReadOpds2Language(metadata);

        var cover = links
            .FirstOrDefault(x =>
                x.Type?.StartsWith(
                    "image/",
                    StringComparison.OrdinalIgnoreCase)
                    == true
                && (RelContains(
                        x.Rel,
                        "image")
                    || RelContains(
                        x.Rel,
                        "thumbnail")))
            ?.Href;

        var acquisitionUri = ResolveOpdsUri(
            baseUri,
            acquisition.Href);

        return new ResolvedOpdsItem(
            source.Id,
            source.Name,
            OpdsBookKey(
                source.Id,
                acquisitionUri),
            title,
            author,
            description,
            language,
            cover is null
                ? null
                : ResolveOpdsUri(
                    baseUri,
                    cover)
                    .ToString(),
            acquisitionUri);
    }

    private static IReadOnlyList<OpdsLink> ParseOpds2Links(
        JsonElement element,
        Uri baseUri)
    {
        if (!element.TryGetProperty(
                "links",
                out var links)
            || links.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<OpdsLink>();

        foreach (var link in links.EnumerateArray())
        {
            var href = ReadJsonString(
                link,
                "href");
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            var type = ReadJsonString(
                link,
                "type");
            var rel = ReadJsonRel(link);

            result.Add(new OpdsLink(
                href,
                type,
                rel));
        }

        return result;
    }

    private static OpdsDocument ParseOpds1(
        BookOpdsSourceSettings source,
        Uri baseUri,
        string xml)
    {
        var document = XDocument.Parse(
            xml,
            LoadOptions.None);
        var root = document.Root
            ?? throw new InvalidOperationException(
                "OPDS XML feed is empty.");

        XNamespace atom =
            "http://www.w3.org/2005/Atom";
        XNamespace dc =
            "http://purl.org/dc/terms/";

        var searchElement = root
            .Elements(atom + "link")
            .FirstOrDefault(x =>
                RelContains(
                    (string?)x.Attribute("rel"),
                    "search"));

        var searchLink = searchElement is null
            ? null
            : new OpdsLink(
                (string?)searchElement.Attribute("href")
                    ?? "",
                (string?)searchElement.Attribute("type"),
                (string?)searchElement.Attribute("rel"));

        var items = new List<ResolvedOpdsItem>();

        foreach (var entry in root.Elements(
                     atom + "entry"))
        {
            var acquisition = entry
                .Elements(atom + "link")
                .FirstOrDefault(x =>
                    ((string?)x.Attribute("type"))
                        ?.StartsWith(
                            "application/epub+zip",
                            StringComparison.OrdinalIgnoreCase)
                        == true);

            var href =
                (string?)acquisition?.Attribute("href");
            var title =
                entry.Element(atom + "title")?.Value
                    ?.Trim();

            if (string.IsNullOrWhiteSpace(href)
                || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var author = string.Join(
                ", ",
                entry.Elements(atom + "author")
                    .Select(x =>
                        x.Element(atom + "name")?.Value?.Trim())
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x)));

            var description =
                entry.Element(atom + "summary")?.Value?.Trim()
                ?? entry.Element(atom + "content")?.Value?.Trim();

            var language =
                entry.Element(dc + "language")?.Value?.Trim();

            var coverHref = entry
                .Elements(atom + "link")
                .Where(x =>
                    ((string?)x.Attribute("type"))
                        ?.StartsWith(
                            "image/",
                            StringComparison.OrdinalIgnoreCase)
                        == true)
                .Select(x =>
                    (string?)x.Attribute("href"))
                .FirstOrDefault(x =>
                    !string.IsNullOrWhiteSpace(x));

            var acquisitionUri = ResolveOpdsUri(
                baseUri,
                href);

            items.Add(new ResolvedOpdsItem(
                source.Id,
                source.Name,
                OpdsBookKey(
                    source.Id,
                    acquisitionUri),
                title,
                string.IsNullOrWhiteSpace(author)
                    ? null
                    : author,
                description,
                language,
                string.IsNullOrWhiteSpace(coverHref)
                    ? null
                    : ResolveOpdsUri(
                        baseUri,
                        coverHref)
                        .ToString(),
                acquisitionUri));
        }

        return new OpdsDocument(
            items,
            searchLink);
    }

    private static string? ParseOpenSearchTemplate(
        string xml)
    {
        var document = XDocument.Parse(
            xml,
            LoadOptions.None);

        return document
            .Descendants()
            .Where(x =>
                x.Name.LocalName.Equals(
                    "Url",
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x =>
                ((string?)x.Attribute("type"))
                    ?.Contains(
                        "opds",
                        StringComparison.OrdinalIgnoreCase)
                    == true)
            .Select(x =>
                (string?)x.Attribute("template"))
            .FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x));
    }

    private static string ExpandOpenSearchTemplate(
        string template,
        string query)
    {
        var expanded = Regex.Replace(
            template,
            @"\{searchTerms\??\}",
            Uri.EscapeDataString(query),
            RegexOptions.IgnoreCase);

        expanded = Regex.Replace(
            expanded,
            @"\{count\??\}",
            MaxOpdsResultsPerSource.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            RegexOptions.IgnoreCase);

        expanded = Regex.Replace(
            expanded,
            @"\{startIndex\??\}",
            "0",
            RegexOptions.IgnoreCase);

        expanded = Regex.Replace(
            expanded,
            @"\{startPage\??\}",
            "1",
            RegexOptions.IgnoreCase);

        expanded = Regex.Replace(
            expanded,
            @"\{[^{}]+\?\}",
            "",
            RegexOptions.CultureInvariant);

        if (expanded.Contains(
            '{',
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The OPDS search template contains unsupported required parameters.");
        }

        return expanded;
    }

    private static Uri ResolveOpdsUri(
        Uri baseUri,
        string href)
    {
        if (!Uri.TryCreate(
                href,
                UriKind.RelativeOrAbsolute,
                out var value))
        {
            throw new InvalidOperationException(
                "OPDS feed contains an invalid link.");
        }

        var resolved = value.IsAbsoluteUri
            ? value
            : new Uri(
                baseUri,
                value);

        if (resolved.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                "OPDS feed contains a non-HTTP link.");
        }

        return resolved;
    }

    private static string? ReadOpds2Author(
        JsonElement metadata)
    {
        if (!metadata.TryGetProperty(
                "author",
                out var author))
        {
            return null;
        }

        if (author.ValueKind == JsonValueKind.String)
        {
            return author.GetString()?.Trim();
        }

        if (author.ValueKind == JsonValueKind.Object)
        {
            return ReadJsonString(
                author,
                "name");
        }

        if (author.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var names = author
            .EnumerateArray()
            .Select(x =>
                x.ValueKind == JsonValueKind.String
                    ? x.GetString()
                    : x.ValueKind == JsonValueKind.Object
                        ? ReadJsonString(
                            x,
                            "name")
                        : null)
            .Where(x =>
                !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .ToArray();

        return names.Length == 0
            ? null
            : string.Join(
                ", ",
                names);
    }

    private static string? ReadOpds2Language(
        JsonElement metadata)
    {
        if (!metadata.TryGetProperty(
                "language",
                out var language))
        {
            return null;
        }

        if (language.ValueKind == JsonValueKind.String)
        {
            return language.GetString()?.Trim();
        }

        if (language.ValueKind == JsonValueKind.Array)
        {
            return language
                .EnumerateArray()
                .Where(x =>
                    x.ValueKind == JsonValueKind.String)
                .Select(x =>
                    x.GetString()?.Trim())
                .FirstOrDefault(x =>
                    !string.IsNullOrWhiteSpace(x));
        }

        return null;
    }

    private static string? ReadJsonString(
        JsonElement element,
        string property)
    {
        return element.TryGetProperty(
                property,
                out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;
    }

    private static string? ReadJsonRel(
        JsonElement link)
    {
        if (!link.TryGetProperty(
                "rel",
                out var rel))
        {
            return null;
        }

        return rel.ValueKind switch
        {
            JsonValueKind.String =>
                rel.GetString(),
            JsonValueKind.Array =>
                string.Join(
                    " ",
                    rel.EnumerateArray()
                        .Where(x =>
                            x.ValueKind
                                == JsonValueKind.String)
                        .Select(x =>
                            x.GetString())),
            _ => null
        };
    }

    private static bool RelContains(
        string? rel,
        string value)
    {
        if (string.IsNullOrWhiteSpace(rel))
        {
            return false;
        }

        return rel
            .Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
            .Any(x =>
                x.Equals(
                    value,
                    StringComparison.OrdinalIgnoreCase)
                || x.EndsWith(
                    "/" + value,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static string OpdsBookKey(
        string sourceId,
        Uri acquisitionUri)
    {
        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                sourceId
                + "|"
                + acquisitionUri.AbsoluteUri));

        return Convert.ToHexString(hash)
            .ToLowerInvariant()[..24];
    }

    private static string SanitizeOpdsFileName(
        string title)
    {
        var invalid =
            Path.GetInvalidFileNameChars()
                .ToHashSet();

        var clean = new string(
            title.Select(ch =>
                    invalid.Contains(ch)
                        ? '_'
                        : ch)
                .ToArray())
            .Trim();

        return string.IsNullOrWhiteSpace(clean)
            ? "opds-book"
            : clean.Length <= 120
                ? clean
                : clean[..120];
    }

    private string GetOpdsSettingsPath()
    {
        var configured =
            configuration["Books:Opds:SettingsPath"]?.Trim();

        return string.IsNullOrWhiteSpace(configured)
            ? BookOpdsSettingsStore.SettingsPath
            : Path.GetFullPath(configured);
    }

    private BookOpdsSourceSettings FindOpdsSource(
        string sourceId,
        bool requireEnabled)
    {
        var cleanId = sourceId?.Trim();

        var source = BookOpdsSettingsStore.Load(
                GetOpdsSettingsPath())
            .FirstOrDefault(x =>
                x.Id.Equals(
                    cleanId,
                    StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                "OPDS source was not found.");

        if (requireEnabled
            && !source.IsEnabled)
        {
            throw new InvalidOperationException(
                "OPDS source is disabled.");
        }

        return source;
    }

    private sealed record OpdsDocument(
        IReadOnlyList<ResolvedOpdsItem> Items,
        OpdsLink? SearchLink);

    private sealed record OpdsLink(
        string Href,
        string? Type,
        string? Rel);

    private sealed record ResolvedOpdsItem(
        string SourceId,
        string SourceName,
        string Key,
        string Title,
        string? Author,
        string? Description,
        string? Language,
        string? CoverImageUrl,
        Uri AcquisitionUrl)
    {
        public BookOpdsCatalogItem ToPublic() =>
            new(
                SourceId,
                SourceName,
                Key,
                Title,
                Author,
                Description,
                Language,
                CoverImageUrl);
    }
}
