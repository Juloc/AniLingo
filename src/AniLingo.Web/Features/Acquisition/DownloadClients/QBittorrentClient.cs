using System.Text.Json;

namespace AniLingo.Web.Features.Acquisition.DownloadClients;

/// <summary>
/// qBittorrent Web API v2 client: cookie login, add torrent/magnet with
/// category/save path, status polling and delete. The HTTP client must not
/// use automatic cookies (see Program.cs registration); the session cookie
/// is read from the login response and attached to every following request
/// explicitly, so connections never leak between qBittorrent entries.
/// </summary>
public sealed class QBittorrentClient(HttpClient httpClient) : IDownloadClient
{
    public const string ProviderIdConstant = "qbittorrent";

    public DownloadClientType Type => DownloadClientType.QBittorrent;

    public DownloadProtocol Protocol => DownloadProtocol.Torrent;

    public string ProviderId => ProviderIdConstant;

    public async Task<DownloadClientTestResult> TestAsync(
        DownloadClientEntry entry,
        CancellationToken cancellationToken)
    {
        try
        {
            var sid = await LoginAsync(entry, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, Combine(entry, "/api/v2/app/version"));
            request.Headers.Add("Cookie", sid);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new DownloadClientTestResult(false, Error: $"qBittorrent returned HTTP {(int)response.StatusCode}.");
            }

            var version = await response.Content.ReadAsStringAsync(cancellationToken);
            return new DownloadClientTestResult(true, version.Trim());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DownloadClientException exception)
        {
            return new DownloadClientTestResult(false, Error: exception.Message);
        }
        catch (HttpRequestException)
        {
            return new DownloadClientTestResult(false, Error: "qBittorrent could not be reached.");
        }
    }

    public async Task<DownloadClientSubmitResult> SubmitAsync(
        DownloadClientEntry entry,
        DownloadClientSubmitRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string sid;
        try
        {
            sid = await LoginAsync(entry, cancellationToken);
        }
        catch (DownloadClientException exception)
        {
            return new DownloadClientSubmitResult(false, null, exception.Message);
        }

        if (request.File is not null)
        {
            return new DownloadClientSubmitResult(false, null, "qBittorrent does not accept an NZB/file upload; use a magnet link or torrent URL.");
        }

        string? hash;
        using var content = new MultipartFormDataContent();

        if (!string.IsNullOrWhiteSpace(request.MagnetUri))
        {
            hash = TorrentInfoHash.ExtractMagnetHash(request.MagnetUri);
            content.Add(new StringContent(request.MagnetUri), "urls");
        }
        else if (request.Url is not null)
        {
            byte[] bytes;
            try
            {
                bytes = await httpClient.GetByteArrayAsync(request.Url, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                return new DownloadClientSubmitResult(false, null, $"Could not download the torrent file: {exception.Message}");
            }

            try
            {
                hash = TorrentInfoHash.ComputeInfoHash(bytes);
            }
            catch (FormatException exception)
            {
                return new DownloadClientSubmitResult(false, null, $"Invalid torrent file: {exception.Message}");
            }

            content.Add(new ByteArrayContent(bytes), "torrents", "release.torrent");
        }
        else
        {
            return new DownloadClientSubmitResult(false, null, "No magnet link or torrent URL was provided.");
        }

        var category = entry.CategoryFor(request.IsBooks);
        if (!string.IsNullOrWhiteSpace(category))
        {
            content.Add(new StringContent(category), "category");
        }

        if (!string.IsNullOrWhiteSpace(entry.Settings.SavePath))
        {
            content.Add(new StringContent(entry.Settings.SavePath), "savepath");
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            content.Add(new StringContent(request.Name), "rename");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Combine(entry, "/api/v2/torrents/add"))
        {
            Content = content
        };
        httpRequest.Headers.Add("Cookie", sid);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        var body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        if (!response.IsSuccessStatusCode || !body.Equals("Ok.", StringComparison.OrdinalIgnoreCase))
        {
            return new DownloadClientSubmitResult(false, null, $"qBittorrent rejected the torrent: {body}");
        }

        return new DownloadClientSubmitResult(true, hash);
    }

    public async Task<IReadOnlyList<DownloadClientJobStatus>> GetStatusAsync(
        DownloadClientEntry entry,
        IReadOnlyCollection<string> externalIds,
        CancellationToken cancellationToken)
    {
        if (externalIds.Count == 0)
        {
            return [];
        }

        var sid = await LoginAsync(entry, cancellationToken);
        var hashes = string.Join('|', externalIds);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            Combine(entry, $"/api/v2/torrents/info?hashes={Uri.EscapeDataString(hashes)}"));
        request.Headers.Add("Cookie", sid);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new DownloadClientException($"qBittorrent torrents/info failed with HTTP {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(body);
        var results = new List<DownloadClientJobStatus>();
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var item in document.RootElement.EnumerateArray())
        {
            var hash = ReadString(item, "hash") ?? string.Empty;
            if (hash.Length == 0)
            {
                continue;
            }

            var name = ReadString(item, "name") ?? hash;
            var state = ReadString(item, "state") ?? string.Empty;
            var progress = ReadDouble(item, "progress") ?? 0;
            var size = ReadLong(item, "size");
            var completed = ReadLong(item, "completed");
            var eta = ReadLong(item, "eta");
            var contentPath = ReadString(item, "content_path") ?? ReadString(item, "save_path");

            results.Add(
                new DownloadClientJobStatus(
                    hash,
                    name,
                    MapState(state),
                    Math.Clamp(progress * 100, 0, 100),
                    eta is > 0 and < 8_640_000 ? TimeSpan.FromSeconds(eta.Value) : null,
                    size,
                    size is { } total && completed is { } done ? Math.Max(0, total - done) : null,
                    ReadDouble(item, "dlspeed"),
                    contentPath,
                    state is "error" or "missingFiles" ? $"qBittorrent reports state '{state}'." : null));
        }

        return results;
    }

    public async Task<bool> DeleteAsync(
        DownloadClientEntry entry,
        string externalId,
        bool deleteFiles,
        CancellationToken cancellationToken)
    {
        var sid = await LoginAsync(entry, cancellationToken);
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["hashes"] = externalId,
                ["deleteFiles"] = deleteFiles ? "true" : "false"
            });

        using var request = new HttpRequestMessage(HttpMethod.Post, Combine(entry, "/api/v2/torrents/delete"))
        {
            Content = content
        };
        request.Headers.Add("Cookie", sid);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private async Task<string> LoginAsync(DownloadClientEntry entry, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["username"] = entry.Settings.Username ?? string.Empty,
                ["password"] = entry.Secret ?? string.Empty
            });

        using var request = new HttpRequestMessage(HttpMethod.Post, Combine(entry, "/api/v2/auth/login"))
        {
            Content = content
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        if (!response.IsSuccessStatusCode || !body.Equals("Ok.", StringComparison.OrdinalIgnoreCase))
        {
            throw new DownloadClientException("qBittorrent rejected the login; check the username and password.");
        }

        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            throw new DownloadClientException("qBittorrent did not return a session cookie.");
        }

        var sid = cookies
            .Select(cookie => cookie.Split(';')[0].Trim())
            .FirstOrDefault(cookie => cookie.StartsWith("SID=", StringComparison.OrdinalIgnoreCase));

        return sid ?? throw new DownloadClientException("qBittorrent did not return a SID cookie.");
    }

    private static DownloadClientJobState MapState(string state) =>
        state switch
        {
            "error" or "missingFiles" => DownloadClientJobState.Failed,
            "uploading" or "stalledUP" or "queuedUP" or "forcedUP" or "pausedUP" => DownloadClientJobState.Completed,
            "checkingUP" or "checkingDL" or "checkingResumeData" or "moving" => DownloadClientJobState.PostProcessing,
            "queuedDL" or "pausedDL" => DownloadClientJobState.Queued,
            "downloading" or "stalledDL" or "metaDL" or "forcedDL" or "allocating" => DownloadClientJobState.Downloading,
            _ => DownloadClientJobState.Unknown
        };

    private static Uri Combine(DownloadClientEntry entry, string pathAndQuery) =>
        new($"{entry.Settings.BaseUrl.TrimEnd('/')}/{pathAndQuery.TrimStart('/')}", UriKind.Absolute);

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? ReadDouble(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static long? ReadLong(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : null;
}
