using System.Net.Http.Json;
using System.Text.Json;

namespace AniLingo.Web.Features.Metadata;

public sealed class MetadataProviderException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class AniListMetadataProvider(
    HttpClient httpClient,
    ILogger<AniListMetadataProvider> logger) : IAnimeMetadataProvider
{
    public const string ProviderKey = "anilist";
    private const int MaximumSearchLimit = 12;

    private const string SearchQuery = """
        query ($search: String!, $perPage: Int!) {
          Page(page: 1, perPage: $perPage) {
            media(search: $search, type: ANIME, isAdult: false) {
              id
              title { romaji english native }
              description(asHtml: false)
              coverImage { extraLarge large }
              bannerImage
              format
              status
              season
              seasonYear
              episodes
              duration
              isAdult
            }
          }
        }
        """;

    private const string ByIdQuery = """
        query ($id: Int!) {
          Media(id: $id, type: ANIME) {
            id
            title { romaji english native }
            description(asHtml: false)
            coverImage { extraLarge large }
            bannerImage
            format
            status
            season
            seasonYear
            episodes
            duration
            isAdult
          }
        }
        """;

    public string Key => ProviderKey;

    public async Task<IReadOnlyList<AnimeMetadataCandidate>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalized = query.Trim();
        if (normalized.Length == 0)
        {
            return [];
        }

        var response = await SendAsync(
            SearchQuery,
            new
            {
                search = normalized,
                perPage = Math.Clamp(limit, 1, MaximumSearchLimit)
            },
            cancellationToken);

        return ParseSearchResponse(response);
    }

    public async Task<AnimeMetadataCandidate?> GetAsync(
        string externalId,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(externalId, out var id) || id <= 0)
        {
            return null;
        }

        var response = await SendAsync(
            ByIdQuery,
            new { id },
            cancellationToken);

        return ParseMediaResponse(response);
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
                logger.LogWarning(
                    "AniList metadata request failed with HTTP {StatusCode}.",
                    (int)response.StatusCode);
                throw new MetadataProviderException(
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

                throw new MetadataProviderException(
                    string.IsNullOrWhiteSpace(message)
                        ? "AniList returned a GraphQL error."
                        : $"AniList: {message}");
            }

            return body;
        }
        catch (MetadataProviderException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
            TaskCanceledException or
            JsonException)
        {
            logger.LogWarning(exception, "AniList metadata request failed.");
            throw new MetadataProviderException(
                "AniList metadata is currently unavailable.",
                exception);
        }
    }

    public static IReadOnlyList<AnimeMetadataCandidate> ParseSearchResponse(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("Page", out var page) ||
            page.ValueKind == JsonValueKind.Null ||
            !page.TryGetProperty("media", out var media) ||
            media.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return media.EnumerateArray()
            .Select(ParseMedia)
            .Where(candidate => candidate is not null)
            .Cast<AnimeMetadataCandidate>()
            .ToArray();
    }

    public static AnimeMetadataCandidate? ParseMediaResponse(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("Media", out var media) ||
            media.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return ParseMedia(media);
    }

    private static AnimeMetadataCandidate? ParseMedia(JsonElement media)
    {
        if (!media.TryGetProperty("id", out var idElement) ||
            !idElement.TryGetInt32(out var id))
        {
            return null;
        }

        if (media.TryGetProperty("isAdult", out var adultElement) &&
            adultElement.ValueKind is JsonValueKind.True)
        {
            return null;
        }

        var title = media.TryGetProperty("title", out var titleElement)
            ? titleElement
            : default;

        var romaji = ReadString(title, "romaji");
        var english = ReadString(title, "english");
        var native = ReadString(title, "native");
        var preferred = AnimeMetadataTitles.Choose(
            english,
            romaji,
            native,
            $"AniList {id}");

        string? cover = null;
        if (media.TryGetProperty("coverImage", out var coverElement) &&
            coverElement.ValueKind == JsonValueKind.Object)
        {
            cover = ReadString(coverElement, "extraLarge")
                ?? ReadString(coverElement, "large");
        }

        return new AnimeMetadataCandidate(
            ProviderKey,
            id.ToString(),
            preferred,
            romaji,
            english,
            native,
            ReadString(media, "description"),
            cover,
            ReadString(media, "bannerImage"),
            ReadString(media, "format"),
            ReadString(media, "status"),
            ReadString(media, "season"),
            ReadInt(media, "seasonYear"),
            ReadInt(media, "episodes"),
            ReadInt(media, "duration"));
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
}
