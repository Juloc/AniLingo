using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jularr.Web.Features.Metadata;

namespace Jularr.Web.Features.Calendar;

/// <summary>Release-relevant fields of one AniList media entry.</summary>
public sealed record AniListReleaseMedia(
    int Id,
    string? Type,
    string? Status,
    int? StartYear,
    int? StartMonth,
    int? StartDay,
    AniListAiring? NextAiring);

public sealed record AniListAiring(int MediaId, int Episode, DateTimeOffset AiringAt);

/// <summary>The provider response of one batch: media entries and their airing schedule.</summary>
public sealed record AniListReleaseSchedule(
    IReadOnlyList<AniListReleaseMedia> Media,
    IReadOnlyList<AniListAiring> Airings,
    bool HasMoreAirings);

/// <summary>AniList asked Jularr to slow down; the refresh stops and resumes after the delay.</summary>
public sealed class ReleaseProviderRateLimitedException(TimeSpan retryAfter)
    : Exception($"The release provider is rate limited for {retryAfter.TotalSeconds:0} s.")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

public interface IAniListReleaseScheduleClient
{
    /// <summary>
    /// One page of release data for up to <see cref="AniListReleaseScheduleClient.MaxIdsPerRequest"/>
    /// AniList ids: page 1 also returns the media entries, later pages only more airings.
    /// </summary>
    Task<AniListReleaseSchedule> FetchAsync(
        IReadOnlyList<int> ids,
        DateTimeOffset from,
        DateTimeOffset to,
        int page,
        CancellationToken cancellationToken);
}

/// <summary>
/// Batched AniList GraphQL client for airing schedules and start dates. One request covers many
/// media entries, so a refresh of the whole library costs a handful of calls.
/// </summary>
public sealed class AniListReleaseScheduleClient(
    HttpClient httpClient,
    ILogger<AniListReleaseScheduleClient> logger) : IAniListReleaseScheduleClient
{
    public const int MaxIdsPerRequest = 50;

    private const string FirstPageQuery = """
        query ($ids: [Int], $from: Int, $to: Int, $page: Int) {
          media: Page(page: 1, perPage: 50) {
            media(id_in: $ids) {
              id
              type
              status
              startDate { year month day }
              nextAiringEpisode { episode airingAt }
            }
          }
          schedule: Page(page: $page, perPage: 50) {
            pageInfo { hasNextPage }
            airingSchedules(mediaId_in: $ids, airingAt_greater: $from, airingAt_lesser: $to, sort: TIME) {
              mediaId
              episode
              airingAt
            }
          }
        }
        """;

    private const string NextPageQuery = """
        query ($ids: [Int], $from: Int, $to: Int, $page: Int) {
          schedule: Page(page: $page, perPage: 50) {
            pageInfo { hasNextPage }
            airingSchedules(mediaId_in: $ids, airingAt_greater: $from, airingAt_lesser: $to, sort: TIME) {
              mediaId
              episode
              airingAt
            }
          }
        }
        """;

    public async Task<AniListReleaseSchedule> FetchAsync(
        IReadOnlyList<int> ids,
        DateTimeOffset from,
        DateTimeOffset to,
        int page,
        CancellationToken cancellationToken)
    {
        if (ids.Count is 0 or > MaxIdsPerRequest)
        {
            throw new ArgumentOutOfRangeException(nameof(ids));
        }

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "",
                new
                {
                    query = page <= 1 ? FirstPageQuery : NextPageQuery,
                    variables = new
                    {
                        ids,
                        from = from.ToUnixTimeSeconds(),
                        to = to.ToUnixTimeSeconds(),
                        page = Math.Max(1, page)
                    }
                },
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1);
                throw new ReleaseProviderRateLimitedException(retryAfter);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("AniList release schedule request failed with HTTP {StatusCode}.", (int)response.StatusCode);
                throw new MetadataProviderException($"AniList returned HTTP {(int)response.StatusCode}.");
            }

            return AniListReleaseScheduleParser.Parse(body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new MetadataProviderException("AniList release data is currently unavailable.", exception);
        }
    }
}

public static class AniListReleaseScheduleParser
{
    public static AniListReleaseSchedule Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("errors", out var errors) &&
            errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0)
        {
            var message = errors[0].TryGetProperty("message", out var text) ? text.GetString() : null;
            throw new MetadataProviderException(string.IsNullOrWhiteSpace(message) ? "AniList returned a GraphQL error." : $"AniList: {message}");
        }

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return new AniListReleaseSchedule([], [], false);
        }

        var media = new List<AniListReleaseMedia>();
        if (data.TryGetProperty("media", out var mediaPage) &&
            mediaPage.ValueKind == JsonValueKind.Object &&
            mediaPage.TryGetProperty("media", out var mediaList) &&
            mediaList.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in mediaList.EnumerateArray())
            {
                if (ReadInt(item, "id") is not { } id)
                {
                    continue;
                }

                var start = item.TryGetProperty("startDate", out var startDate) ? startDate : default;
                AniListAiring? next = null;
                if (item.TryGetProperty("nextAiringEpisode", out var nextAiring) &&
                    ReadInt(nextAiring, "episode") is { } nextEpisode &&
                    ReadLong(nextAiring, "airingAt") is { } nextAt)
                {
                    next = new AniListAiring(id, nextEpisode, DateTimeOffset.FromUnixTimeSeconds(nextAt));
                }

                media.Add(new AniListReleaseMedia(
                    id,
                    ReadString(item, "type"),
                    ReadString(item, "status"),
                    ReadInt(start, "year"),
                    ReadInt(start, "month"),
                    ReadInt(start, "day"),
                    next));
            }
        }

        var airings = new List<AniListAiring>();
        var hasMore = false;
        if (data.TryGetProperty("schedule", out var schedulePage) && schedulePage.ValueKind == JsonValueKind.Object)
        {
            hasMore = schedulePage.TryGetProperty("pageInfo", out var pageInfo) &&
                      pageInfo.ValueKind == JsonValueKind.Object &&
                      pageInfo.TryGetProperty("hasNextPage", out var hasNext) &&
                      hasNext.ValueKind == JsonValueKind.True;

            if (schedulePage.TryGetProperty("airingSchedules", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    if (ReadInt(item, "mediaId") is { } mediaId &&
                        ReadInt(item, "episode") is { } episode and > 0 &&
                        ReadLong(item, "airingAt") is { } airingAt)
                    {
                        airings.Add(new AniListAiring(mediaId, episode, DateTimeOffset.FromUnixTimeSeconds(airingAt)));
                    }
                }
            }
        }

        return new AniListReleaseSchedule(media, airings, hasMore);
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static int? ReadInt(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number)
            ? number
            : null;

    private static long? ReadLong(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var number)
            ? number
            : null;
}

/// <summary>
/// Turns AniList release data into cached releases. Only what AniList states is stored: airings
/// are exact instants, a not-yet-released entry contributes its start date with the precision
/// AniList has (year, month or day), and nothing is extrapolated from past episodes.
/// </summary>
public static class AniListReleaseNormalizer
{
    public const string Provider = AniListMetadataProvider.ProviderKey;

    public static IReadOnlyList<ReleaseSourceSnapshot> Normalize(
        IReadOnlyCollection<int> requestedIds,
        IReadOnlyList<AniListReleaseMedia> media,
        IReadOnlyList<AniListAiring> airings)
    {
        var mediaById = media.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First());
        var airingsById = airings
            .Concat(media.Where(item => item.NextAiring is not null).Select(item => item.NextAiring!))
            .GroupBy(airing => airing.MediaId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var snapshots = new List<ReleaseSourceSnapshot>();
        foreach (var id in requestedIds.Distinct())
        {
            var entry = mediaById.GetValueOrDefault(id);
            var releases = new Dictionary<(ReleaseKind, int), CachedRelease>();
            var externalId = id.ToString(CultureInfo.InvariantCulture);

            foreach (var airing in airingsById.GetValueOrDefault(id) ?? [])
            {
                var kind = airing.Episode == 1 ? ReleaseKind.SeasonPremiere : ReleaseKind.Episode;
                releases.TryAdd((kind, airing.Episode), new CachedRelease(Provider, externalId, kind, airing.Episode, ReleaseDate.FromInstant(airing.AiringAt)));
            }

            if (entry is { Status: "NOT_YET_RELEASED" })
            {
                var isAnime = string.Equals(entry.Type, "ANIME", StringComparison.OrdinalIgnoreCase);
                var kind = isAnime ? ReleaseKind.SeasonPremiere : ReleaseKind.SeriesStart;
                var unit = isAnime ? 1 : 0;
                if (!releases.ContainsKey((kind, unit)))
                {
                    releases[(kind, unit)] = new CachedRelease(
                        Provider,
                        externalId,
                        kind,
                        unit,
                        ReleaseDate.FromParts(entry.StartYear, entry.StartMonth, entry.StartDay));
                }
            }

            // A missing entry (deleted on AniList or filtered as adult) is stored without releases
            // so its old future data is cleared instead of lingering.
            snapshots.Add(new ReleaseSourceSnapshot(externalId, entry?.Status, releases.Values.ToArray()));
        }

        return snapshots;
    }
}
