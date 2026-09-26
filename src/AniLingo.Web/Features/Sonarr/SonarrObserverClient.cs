using System.Globalization;
using System.Text.Json;
using AniLingo.Web.Features.Acquisition;
using AniLingo.Web.Features.Acquisition.Ownership;

namespace AniLingo.Web.Features.Sonarr;

public interface ISonarrObserverClient
{
    Task<IReadOnlyList<SonarrObservedSeries>> GetSeriesAsync(
        SonarrConnectionSettings settings,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SonarrObservedEpisodeFile>> GetEpisodeFilesAsync(
        SonarrConnectionSettings settings,
        int seriesId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SonarrObservedQueueItem>> GetQueueAsync(
        SonarrConnectionSettings settings,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SonarrObservedHistoryEvent>> GetRecentHistoryAsync(
        SonarrConnectionSettings settings,
        CancellationToken cancellationToken);
}

public sealed class SonarrObserverException(string message, Exception? innerException = null)
    : Exception(message, innerException);

// Read-only Sonarr v3 observation. Every request is an HTTP GET; the only Sonarr mutation
// Jularr performs lives in SonarrSeriesMonitoringClient and is reachable solely from an
// explicit owner migration action.
public sealed class SonarrObserverClient(IHttpClientFactory httpClientFactory) : ISonarrObserverClient
{
    public const int QueuePageSize = 200;
    public const int MaxQueuePages = 50;
    public const int HistoryPageSize = 250;

    public async Task<IReadOnlyList<SonarrObservedSeries>> GetSeriesAsync(
        SonarrConnectionSettings settings,
        CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(settings, "api/v3/series", cancellationToken);
        return ParseSeries(document.RootElement);
    }

    public async Task<IReadOnlyList<SonarrObservedEpisodeFile>> GetEpisodeFilesAsync(
        SonarrConnectionSettings settings,
        int seriesId,
        CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(
            settings,
            $"api/v3/episodefile?seriesId={seriesId.ToString(CultureInfo.InvariantCulture)}",
            cancellationToken);
        return ParseEpisodeFiles(document.RootElement);
    }

    public async Task<IReadOnlyList<SonarrObservedQueueItem>> GetQueueAsync(
        SonarrConnectionSettings settings,
        CancellationToken cancellationToken)
    {
        var items = new List<SonarrObservedQueueItem>();
        for (var page = 1; ; page++)
        {
            if (page > MaxQueuePages)
            {
                throw new SonarrObserverException("Sonarr queue pagination exceeded the safety limit.");
            }

            using var document = await GetJsonAsync(
                settings,
                $"api/v3/queue?page={page.ToString(CultureInfo.InvariantCulture)}" +
                $"&pageSize={QueuePageSize.ToString(CultureInfo.InvariantCulture)}" +
                "&includeUnknownSeriesItems=true&includeSeries=false&includeEpisode=true",
                cancellationToken);

            var parsed = ParseQueuePage(document.RootElement);
            items.AddRange(parsed.Items);
            if (parsed.RecordCount == 0 || page * QueuePageSize >= parsed.TotalRecords)
            {
                return items;
            }
        }
    }

    public async Task<IReadOnlyList<SonarrObservedHistoryEvent>> GetRecentHistoryAsync(
        SonarrConnectionSettings settings,
        CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(
            settings,
            $"api/v3/history?page=1&pageSize={HistoryPageSize.ToString(CultureInfo.InvariantCulture)}" +
            "&sortKey=date&sortDirection=descending&includeSeries=false&includeEpisode=true",
            cancellationToken);
        return ParseHistoryPage(document.RootElement);
    }

    public static IReadOnlyList<SonarrObservedSeries> ParseSeries(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ParseSeries(document.RootElement);
    }

    public static IReadOnlyList<SonarrObservedEpisodeFile> ParseEpisodeFiles(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ParseEpisodeFiles(document.RootElement);
    }

    public static (IReadOnlyList<SonarrObservedQueueItem> Items, int RecordCount, int TotalRecords) ParseQueuePage(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ParseQueuePage(document.RootElement);
    }

    public static IReadOnlyList<SonarrObservedHistoryEvent> ParseHistoryPage(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ParseHistoryPage(document.RootElement);
    }

    public static SonarrHistoryEventKind ClassifyHistoryEvent(string? eventType) =>
        eventType?.Trim().ToLowerInvariant() switch
        {
            "grabbed" => SonarrHistoryEventKind.Grabbed,
            "downloadfolderimported" or "seriesfolderimported" => SonarrHistoryEventKind.Imported,
            "episodefilerenamed" => SonarrHistoryEventKind.Renamed,
            "downloadfailed" => SonarrHistoryEventKind.DownloadFailed,
            "episodefiledeleted" => SonarrHistoryEventKind.FileDeleted,
            _ => SonarrHistoryEventKind.Other
        };

    public static bool TryCreateBaseUri(string? baseUrl, out Uri baseUri, out string? error)
    {
        error = null;
        var normalized = SonarrConnectionStore.NormalizeBaseUrl(baseUrl ?? "") + "/";
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(parsed.Host) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment))
        {
            baseUri = null!;
            error = "Sonarr URL must be an absolute http:// or https:// URL without credentials, query or fragment.";
            return false;
        }

        baseUri = parsed;
        return true;
    }

    private static IReadOnlyList<SonarrObservedSeries> ParseSeries(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new SonarrObserverException("Sonarr series response is not a JSON array.");
        }

        var series = new List<SonarrObservedSeries>();
        foreach (var item in root.EnumerateArray())
        {
            var id = ReadInt32(item, "id");
            var title = ReadString(item, "title");
            if (id is not > 0 || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            series.Add(new(
                id.Value,
                title.Trim(),
                ReadString(item, "path") ?? "",
                ReadBoolean(item, "monitored") ?? true));
        }

        return series;
    }

    private static IReadOnlyList<SonarrObservedEpisodeFile> ParseEpisodeFiles(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new SonarrObserverException("Sonarr episode file response is not a JSON array.");
        }

        var files = new List<SonarrObservedEpisodeFile>();
        foreach (var item in root.EnumerateArray())
        {
            var id = ReadInt32(item, "id");
            var seriesId = ReadInt32(item, "seriesId");
            var path = ReadString(item, "path");
            if (id is not > 0 || seriesId is not > 0 || string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            files.Add(new(id.Value, seriesId.Value, ReadInt32(item, "seasonNumber") ?? 0, path));
        }

        return files;
    }

    private static (IReadOnlyList<SonarrObservedQueueItem> Items, int RecordCount, int TotalRecords) ParseQueuePage(
        JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("records", out var records) ||
            records.ValueKind != JsonValueKind.Array)
        {
            throw new SonarrObserverException("Sonarr queue response has no records array.");
        }

        var items = new List<SonarrObservedQueueItem>();
        var recordCount = 0;
        foreach (var record in records.EnumerateArray())
        {
            recordCount++;
            var title = ReadString(record, "title");
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            items.Add(new(
                ReadInt64(record, "id") ?? 0,
                ReadInt32(record, "seriesId"),
                title,
                AnimeReleaseParser.Parse(title).ReleaseKey,
                ReadString(record, "downloadId"),
                ReadString(record, "outputPath"),
                ReadString(record, "status"),
                ReadString(record, "trackedDownloadState"),
                ReadEpisode(record)));
        }

        return (items, recordCount, ReadInt32(root, "totalRecords") ?? recordCount);
    }

    private static IReadOnlyList<SonarrObservedHistoryEvent> ParseHistoryPage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("records", out var records) ||
            records.ValueKind != JsonValueKind.Array)
        {
            throw new SonarrObserverException("Sonarr history response has no records array.");
        }

        var events = new List<SonarrObservedHistoryEvent>();
        foreach (var record in records.EnumerateArray())
        {
            var eventType = ReadString(record, "eventType") ?? "unknown";
            var sourceTitle = ReadString(record, "sourceTitle");
            record.TryGetProperty("data", out var data);

            events.Add(new(
                ReadInt64(record, "id") ?? 0,
                ReadInt32(record, "seriesId"),
                ClassifyHistoryEvent(eventType),
                eventType,
                ReadDate(record, "date"),
                sourceTitle,
                string.IsNullOrWhiteSpace(sourceTitle) ? null : AnimeReleaseParser.Parse(sourceTitle).ReleaseKey,
                ReadString(record, "downloadId"),
                ReadString(data, "sourcePath") ?? ReadString(data, "droppedPath"),
                ReadString(data, "path") ?? ReadString(data, "importedPath"),
                ReadEpisode(record)));
        }

        return events;
    }

    private async Task<JsonDocument> GetJsonAsync(
        SonarrConnectionSettings settings,
        string relativePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!TryCreateBaseUri(settings.BaseUrl, out var baseUri, out var error))
        {
            throw new SonarrObserverException(error!);
        }

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new SonarrObserverException("Sonarr API key is required.");
        }

        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, relativePath));
        request.Headers.TryAddWithoutValidation("X-Api-Key", settings.ApiKey.Trim());
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new SonarrObserverException(
                $"Sonarr {relativePath.Split('?')[0]} returned HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        try
        {
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new SonarrObserverException("Sonarr returned invalid JSON.", exception);
        }
    }

    private static SonarrObservedEpisode? ReadEpisode(JsonElement record)
    {
        if (!record.TryGetProperty("episode", out var episode) ||
            episode.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var season = ReadInt32(episode, "seasonNumber");
        var number = ReadInt32(episode, "episodeNumber");
        return season is int s && number is int n
            ? new SonarrObservedEpisode(s, n, ReadInt32(episode, "absoluteEpisodeNumber"))
            : null;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt32(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static long? ReadInt64(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var parsed)
            ? parsed
            : null;

    private static bool? ReadBoolean(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static DateTimeOffset? ReadDate(JsonElement element, string name) =>
        ReadString(element, name) is { } raw &&
        DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
}
