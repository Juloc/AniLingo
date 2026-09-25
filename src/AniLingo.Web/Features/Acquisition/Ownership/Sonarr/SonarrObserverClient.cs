using System.Net;
using System.Text.Json;
using AniLingo.Web.Features.Acquisition;

namespace AniLingo.Web.Features.Acquisition.Ownership.Sonarr;

public interface ISonarrObserverClient
{
    Task<SonarrConnectionTestResult> TestAsync(
        SonarrConnection connection,
        CancellationToken cancellationToken);

    Task<SonarrQueueSnapshot> GetQueueAsync(
        SonarrConnection connection,
        CancellationToken cancellationToken);
}

public sealed class SonarrObserverClient(HttpClient httpClient) : ISonarrObserverClient
{
    public async Task<SonarrConnectionTestResult> TestAsync(
        SonarrConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = CreateRequest(connection, "/api/v3/system/status");
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new(false, Error: $"Sonarr returned HTTP {(int)response.StatusCode}.");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            var version = document.RootElement.TryGetProperty("version", out var value)
                ? value.GetString()
                : null;

            return new(true, version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or JsonException or UriFormatException)
        {
            return new(false, Error: "Sonarr could not be reached or returned invalid data.");
        }
    }

    public async Task<SonarrQueueSnapshot> GetQueueAsync(
        SonarrConnection connection,
        CancellationToken cancellationToken)
    {
        var settings = SonarrSettingsStore.NormalizeAndValidate(connection.Settings);
        var normalizedConnection = connection with { Settings = settings };
        const int pageSize = 1000;
        var page = 1;
        var items = new List<SonarrQueueItem>();

        while (true)
        {
            var path =
                $"/api/v3/queue?page={page}&pageSize={pageSize}" +
                "&includeUnknownSeriesItems=true&includeSeries=false&includeEpisode=false";

            using var request = CreateRequest(normalizedConnection, path);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new SonarrObserverException(
                    $"Sonarr queue request failed with HTTP {(int)response.StatusCode}.");
            }

            var parsed = ParseQueuePage(json);
            items.AddRange(parsed.Items);

            if (items.Count >= parsed.TotalRecords || parsed.Items.Count == 0)
            {
                break;
            }

            page++;
            if (page > 100)
            {
                throw new SonarrObserverException("Sonarr queue pagination exceeded the safety limit.");
            }
        }

        var releases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            releases.Add(item.ReleaseKey);
            if (!string.IsNullOrWhiteSpace(item.OutputPath))
            {
                paths.Add(item.OutputPath);
            }
        }

        return new(
            items,
            new SonarrObservedState(releases, paths));
    }

    public static (IReadOnlyList<SonarrQueueItem> Items, int TotalRecords) ParseQueuePage(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var totalRecords = root.TryGetProperty("totalRecords", out var total) &&
                           total.TryGetInt32(out var totalValue)
            ? totalValue
            : 0;

        if (!root.TryGetProperty("records", out var records) ||
            records.ValueKind != JsonValueKind.Array)
        {
            return ([], totalRecords);
        }

        var items = new List<SonarrQueueItem>();
        foreach (var record in records.EnumerateArray())
        {
            var title = ReadString(record, "title");
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var parsed = AnimeReleaseParser.Parse(title);
            items.Add(new(
                ReadInt64(record, "id") ?? 0,
                title,
                ReadString(record, "downloadId"),
                ReadString(record, "outputPath"),
                ReadString(record, "status"),
                ReadString(record, "trackedDownloadStatus"),
                ReadInt32(record, "seriesId"),
                ReadInt32(record, "episodeId"),
                parsed.ReleaseKey));
        }

        return (items, totalRecords);
    }

    private static HttpRequestMessage CreateRequest(
        SonarrConnection connection,
        string path)
    {
        var settings = SonarrSettingsStore.NormalizeAndValidate(connection.Settings);
        if (string.IsNullOrWhiteSpace(connection.ApiKey))
        {
            throw new ArgumentException("Sonarr API key is required.", nameof(connection));
        }

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(new Uri(settings.BaseUrl + "/"), path.TrimStart('/')));
        request.Headers.TryAddWithoutValidation("X-Api-Key", connection.ApiKey.Trim());
        request.Headers.Accept.ParseAdd("application/json");
        return request;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt32(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static long? ReadInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var parsed)
            ? parsed
            : null;
}

public sealed class SonarrObserverException : Exception
{
    public SonarrObserverException(string message) : base(message)
    {
    }
}
