using System.Globalization;
using System.Text.Json;
using Jularr.Web.Features.Novels;

namespace Jularr.Web.Features.ReadingDiscovery;

public sealed record ReadingCatalogCandidate(
    string Provider,
    string ExternalId,
    string Title,
    string? NativeTitle,
    string? Author,
    string? CoverImageUrl,
    int? FirstPublishYear,
    string? Status,
    int? VolumeCount,
    int? ChapterCount,
    string? SourceUrl,
    bool IsPublicWebSource)
{
    public string Identity => $"{Provider}:{ExternalId}";
}

public static class ReadingCatalogSearch
{
    public static async Task<IReadOnlyList<ReadingCatalogCandidate>> SearchMangaAsync(
        NovelAniListProvider aniList,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeQuery(query);
        if (normalized.Length == 0)
        {
            return [];
        }

        try
        {
            var results = await aniList.SearchReadingMediaAsync(
                normalized,
                Math.Clamp(limit, 1, 24),
                includeNovels: false,
                includeManga: true,
                cancellationToken);

            return Rank(
                normalized,
                results.Select(MapAniList).ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is NovelMetadataProviderException or
            HttpRequestException or
            TaskCanceledException)
        {
            return [];
        }
    }

    public static async Task<IReadOnlyList<ReadingCatalogCandidate>> SearchLightNovelsAsync(
        NovelAniListProvider aniList,
        HttpClient syosetuClient,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeQuery(query);
        if (normalized.Length == 0)
        {
            return [];
        }

        var boundedLimit = Math.Clamp(limit, 1, 24);
        var aniListTask = SearchAniListNovelsSafeAsync(
            aniList,
            normalized,
            boundedLimit,
            cancellationToken);
        var syosetuTask = SearchSyosetuSafeAsync(
            syosetuClient,
            normalized,
            boundedLimit,
            cancellationToken);

        await Task.WhenAll(aniListTask, syosetuTask);

        var merged = aniListTask.Result
            .Concat(syosetuTask.Result)
            .GroupBy(candidate => candidate.Identity, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        return Rank(normalized, merged);
    }

    public static string NormalizeQuery(string? query) =>
        string.Join(
            " ",
            (query ?? "")
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static async Task<IReadOnlyList<ReadingCatalogCandidate>> SearchAniListNovelsSafeAsync(
        NovelAniListProvider aniList,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        try
        {
            var results = await aniList.SearchReadingMediaAsync(
                query,
                limit,
                includeNovels: true,
                includeManga: false,
                cancellationToken);

            return results.Select(MapAniList).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is NovelMetadataProviderException or
            HttpRequestException or
            TaskCanceledException)
        {
            return [];
        }
    }

    private static async Task<IReadOnlyList<ReadingCatalogCandidate>> SearchSyosetuSafeAsync(
        HttpClient client,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        try
        {
            return await new SyosetuCatalogClient(client)
                .SearchAsync(query, limit, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            HttpRequestException or
            TaskCanceledException or
            JsonException)
        {
            return [];
        }
    }

    private static ReadingCatalogCandidate MapAniList(
        AniListReadingMediaCandidate candidate) =>
        new(
            NovelAniListProvider.ProviderKey,
            candidate.ExternalId,
            candidate.PreferredTitle,
            candidate.NativeTitle,
            null,
            candidate.CoverImageUrl,
            candidate.StartYear,
            candidate.Status,
            candidate.VolumeCount,
            candidate.ChapterCount,
            null,
            IsPublicWebSource: false);

    private static IReadOnlyList<ReadingCatalogCandidate> Rank(
        string query,
        IReadOnlyList<ReadingCatalogCandidate> candidates) =>
        candidates
            .OrderByDescending(candidate => MatchScore(query, candidate))
            .ThenBy(candidate => candidate.Title, StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToArray();

    internal static int MatchScore(
        string query,
        ReadingCatalogCandidate candidate)
    {
        var normalizedQuery = NormalizeForMatch(query);
        var title = NormalizeForMatch(candidate.Title);
        var nativeTitle = NormalizeForMatch(candidate.NativeTitle);
        var author = NormalizeForMatch(candidate.Author);

        var score = title == normalizedQuery || nativeTitle == normalizedQuery
            ? 1000
            : title.StartsWith(normalizedQuery, StringComparison.Ordinal) ||
              nativeTitle.StartsWith(normalizedQuery, StringComparison.Ordinal)
                ? 700
                : title.Contains(normalizedQuery, StringComparison.Ordinal) ||
                  nativeTitle.Contains(normalizedQuery, StringComparison.Ordinal)
                    ? 500
                    : 0;

        if (author.Contains(normalizedQuery, StringComparison.Ordinal))
        {
            score += 200;
        }

        if (!candidate.IsPublicWebSource)
        {
            score += 10;
        }

        return score;
    }

    private static string NormalizeForMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return string.Concat(
                value
                    .Normalize()
                    .ToLowerInvariant()
                    .Where(char.IsLetterOrDigit))
            .Trim();
    }
}

public sealed class SyosetuCatalogClient(HttpClient client)
{
    private static readonly Uri Endpoint =
        new("https://api.syosetu.com/novelapi/api/");

    public static bool IsValidNcode(string? value)
    {
        var code = value?.Trim();
        if (code is null ||
            code.Length is < 6 or > 12 ||
            code[0] is not ('n' or 'N') ||
            !code.AsSpan(1, 4).ToArray().All(char.IsDigit) ||
            !code.AsSpan(5).ToArray().All(char.IsLetter))
        {
            return false;
        }

        return true;
    }

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

        var uri = BuildSearchUri(
            normalized,
            Math.Clamp(limit, 1, 24));

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            uri);
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            "Jularr/1.0");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(7));

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Syosetu catalog returned HTTP {(int)response.StatusCode}.");
        }

        var json = await response.Content.ReadAsStringAsync(timeout.Token);
        return ParseResponse(json);
    }

    internal static Uri BuildSearchUri(
        string query,
        int limit)
    {
        var parameters = new Dictionary<string, string>
        {
            ["out"] = "json",
            ["of"] = "t-n-w-gf-e-ga",
            ["lim"] = Math.Clamp(limit, 1, 24).ToString(CultureInfo.InvariantCulture),
            ["order"] = "hyoka",
            ["title"] = "1",
            ["wname"] = "1",
            ["word"] = query
        };

        var queryString = string.Join(
            "&",
            parameters.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        return new UriBuilder(Endpoint)
        {
            Query = queryString
        }.Uri;
    }

    internal static IReadOnlyList<ReadingCatalogCandidate> ParseResponse(
        string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Syosetu catalog response was not an array.");
        }

        var results = new List<ReadingCatalogCandidate>();

        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !TryReadString(item, "ncode", out var ncode) ||
                !TryReadString(item, "title", out var title))
            {
                continue;
            }

            var normalizedCode = ncode.ToLowerInvariant();
            results.Add(
                new ReadingCatalogCandidate(
                    "syosetu",
                    normalizedCode,
                    title,
                    title,
                    ReadOptionalString(item, "writer"),
                    null,
                    ParseYear(ReadOptionalString(item, "general_firstup")),
                    ReadInt(item, "end") == 1
                        ? "RELEASING"
                        : "FINISHED",
                    null,
                    ReadInt(item, "general_all_no"),
                    $"https://ncode.syosetu.com/{normalizedCode}/",
                    IsPublicWebSource: true));
        }

        return results
            .GroupBy(candidate => candidate.Identity, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private static int? ParseYear(string? value) =>
        DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var parsed)
            ? parsed.Year
            : null;

    private static int? ReadInt(
        JsonElement element,
        string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number)
            ? number
            : null;

    private static string? ReadOptionalString(
        JsonElement element,
        string propertyName) =>
        TryReadString(element, propertyName, out var value)
            ? value
            : null;

    private static bool TryReadString(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = "";

        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()?.Trim() ?? "";
        return value.Length > 0;
    }
}
