using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Novels;

public sealed class NovelMetadataProviderException(
    string message,
    Exception? innerException = null) : Exception(message, innerException);

public sealed partial class NovelAniListProvider(
    HttpClient httpClient,
    ILogger<NovelAniListProvider> logger) : INovelMetadataProvider
{
    public const string ProviderKey = "anilist";
    private const int MaximumSearchLimit = 12;

    private const string SearchQuery = """
        query ($search: String!, $perPage: Int!) {
          Page(page: 1, perPage: $perPage) {
            media(search: $search, type: MANGA, format: NOVEL, isAdult: false) {
              id
              title { romaji english native }
              description(asHtml: false)
              coverImage { extraLarge large }
              bannerImage
              format
              status
              chapters
              volumes
              isAdult
            }
          }
        }
        """;

    private const string ByIdQuery = """
        query ($id: Int!) {
          Media(id: $id, type: MANGA) {
            id
            title { romaji english native }
            description(asHtml: false)
            coverImage { extraLarge large }
            bannerImage
            format
            status
            chapters
            volumes
            isAdult
          }
        }
        """;

    public string Key => ProviderKey;

    public async Task<IReadOnlyList<NovelMetadataCandidate>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalized = query.Trim();
        if (normalized.Length == 0)
        {
            return [];
        }

        var json = await SendAsync(
            SearchQuery,
            new
            {
                search = normalized,
                perPage = Math.Clamp(limit, 1, MaximumSearchLimit)
            },
            cancellationToken);

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("Page", out var page) ||
            !page.TryGetProperty("media", out var media) ||
            media.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return media.EnumerateArray()
            .Select(ParseMedia)
            .Where(x => x is not null)
            .Cast<NovelMetadataCandidate>()
            .ToArray();
    }

    public async Task<NovelMetadataCandidate?> GetAsync(
        string externalId,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(externalId, out var id) || id <= 0)
        {
            return null;
        }

        var json = await SendAsync(ByIdQuery, new { id }, cancellationToken);
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("Media", out var media) ||
            media.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return ParseMedia(media);
    }

    private async Task<string> SendAsync(
        string query,
        object variables,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "",
                new { query, variables },
                cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new NovelMetadataProviderException(
                    $"AniList returned HTTP {(int)response.StatusCode}.");
            }

            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Array &&
                errors.GetArrayLength() > 0)
            {
                var message = errors[0].TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : null;

                throw new NovelMetadataProviderException(
                    string.IsNullOrWhiteSpace(message)
                        ? "AniList returned a GraphQL error."
                        : $"AniList: {message}");
            }

            return body;
        }
        catch (NovelMetadataProviderException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "AniList novel metadata request failed.");
            throw new NovelMetadataProviderException(
                "AniList novel metadata is currently unavailable.",
                exception);
        }
    }

    internal static NovelMetadataCandidate? ParseMedia(JsonElement media)
    {
        if (!media.TryGetProperty("id", out var idElement) ||
            !idElement.TryGetInt32(out var id))
        {
            return null;
        }

        if (media.TryGetProperty("isAdult", out var adultElement) &&
            adultElement.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        var format = ReadString(media, "format");
        if (!string.Equals(format, "NOVEL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var title = media.TryGetProperty("title", out var titleElement)
            ? titleElement
            : default;

        var english = ReadString(title, "english");
        var romaji = ReadString(title, "romaji");
        var native = ReadString(title, "native");
        var preferred = FirstNonEmpty(english, romaji, native) ?? $"AniList {id}";

        string? cover = null;
        if (media.TryGetProperty("coverImage", out var coverElement) &&
            coverElement.ValueKind == JsonValueKind.Object)
        {
            cover = FirstNonEmpty(
                ReadString(coverElement, "extraLarge"),
                ReadString(coverElement, "large"));
        }

        return new NovelMetadataCandidate(
            ProviderKey,
            id.ToString(),
            preferred,
            native,
            NormalizeDescription(ReadString(media, "description")),
            cover,
            ReadString(media, "bannerImage"),
            format,
            ReadString(media, "status"),
            ReadInt(media, "chapters"),
            ReadInt(media, "volumes"));
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

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static int? ReadInt(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.TryGetInt32(out var number)
            ? number
            : null;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
