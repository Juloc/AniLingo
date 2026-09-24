using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Manga;

public sealed partial class MangaAniListService(
    MangaRepository repository,
    IHttpClientFactory httpClientFactory)
{
    private const string SearchQuery = """
        query ($search: String!, $perPage: Int!) {
          Page(page: 1, perPage: $perPage) {
            media(search: $search, type: MANGA, isAdult: false) {
              id
              format
              isAdult
              title { romaji english native }
              description(asHtml: false)
              coverImage { extraLarge large }
              bannerImage
              status
            }
          }
        }
        """;

    private const string ByIdQuery = """
        query ($id: Int!) {
          Media(id: $id, type: MANGA) {
            id
            format
            isAdult
            title { romaji english native }
            description(asHtml: false)
            coverImage { extraLarge large }
            bannerImage
            status
          }
        }
        """;

    public async Task<IReadOnlyList<MangaAniListCandidate>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var normalized = query.Trim();
        if (normalized.Length == 0)
        {
            return [];
        }

        using var document = await SendAsync(
            SearchQuery,
            new { search = normalized, perPage = 12 },
            cancellationToken);

        if (!document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("Page", out var page) ||
            !page.TryGetProperty("media", out var media) ||
            media.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return media.EnumerateArray()
            .Select(Parse)
            .Where(x => x is not null)
            .Cast<MangaAniListCandidate>()
            .ToArray();
    }

    public async Task MatchAsync(
        Guid seriesId,
        string externalId,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(externalId, out var id) || id <= 0)
        {
            throw new InvalidOperationException("Invalid AniList manga ID.");
        }

        using var document = await SendAsync(
            ByIdQuery,
            new { id },
            cancellationToken);

        if (!document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("Media", out var media) ||
            media.ValueKind == JsonValueKind.Null)
        {
            throw new InvalidOperationException("AniList manga was not found.");
        }

        var candidate = Parse(media)
            ?? throw new InvalidOperationException("AniList result is not a manga.");

        await repository.UpdateMetadataAsync(
            seriesId,
            candidate,
            cancellationToken);
    }

    private async Task<JsonDocument> SendAsync(
        string query,
        object variables,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri("https://graphql.anilist.co/");
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        using var response = await client.PostAsJsonAsync(
            "",
            new { query, variables },
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"AniList returned HTTP {(int)response.StatusCode}.");
        }

        var document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("errors", out var errors) &&
            errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0)
        {
            var message = errors[0].TryGetProperty("message", out var element)
                ? element.GetString()
                : null;
            document.Dispose();
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(message)
                    ? "AniList returned an error."
                    : $"AniList: {message}");
        }

        return document;
    }

    private static MangaAniListCandidate? Parse(JsonElement media)
    {
        if (!media.TryGetProperty("id", out var idElement) ||
            !idElement.TryGetInt32(out var id))
        {
            return null;
        }

        if (media.TryGetProperty("isAdult", out var adult) &&
            adult.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        var format = ReadString(media, "format");
        if (string.Equals(format, "NOVEL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var title = media.TryGetProperty("title", out var titles)
            ? titles
            : default;
        var preferred = FirstNonEmpty(
            ReadString(title, "english"),
            ReadString(title, "romaji"),
            ReadString(title, "native")) ?? $"AniList {id}";

        string? cover = null;
        if (media.TryGetProperty("coverImage", out var coverElement) &&
            coverElement.ValueKind == JsonValueKind.Object)
        {
            cover = FirstNonEmpty(
                ReadString(coverElement, "extraLarge"),
                ReadString(coverElement, "large"));
        }

        return new MangaAniListCandidate(
            id.ToString(),
            preferred,
            ReadString(title, "native"),
            NormalizeDescription(ReadString(media, "description")),
            cover,
            ReadString(media, "bannerImage"),
            ReadString(media, "status"));
    }

    private static string? NormalizeDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var decoded = WebUtility.HtmlDecode(HtmlTag().Replace(value, " "));
        var normalized = Whitespace().Replace(decoded, " ").Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string? ReadString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
