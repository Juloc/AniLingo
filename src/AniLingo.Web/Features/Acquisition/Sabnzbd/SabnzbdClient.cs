using System.Globalization;
using System.Net;
using System.Text.Json;

namespace AniLingo.Web.Features.Acquisition.Sabnzbd;

public interface ISabnzbdClient
{
    Task<SabnzbdConnectionTestResult> TestAsync(
        SabnzbdConnection connection,
        CancellationToken cancellationToken);

    Task<SabnzbdGrabResult> GrabAsync(
        SabnzbdConnection connection,
        SabnzbdGrabRequest grab,
        CancellationToken cancellationToken);

    Task<SabnzbdQueueSnapshot> GetQueueAsync(
        SabnzbdConnection connection,
        CancellationToken cancellationToken);

    Task<SabnzbdHistorySnapshot> GetHistoryAsync(
        SabnzbdConnection connection,
        IReadOnlyCollection<string>? nzoIds,
        CancellationToken cancellationToken);

    Task<SabnzbdActionResult> CancelAsync(
        SabnzbdConnection connection,
        string nzoId,
        bool deleteFiles,
        CancellationToken cancellationToken);

    Task<SabnzbdActionResult> RetryAsync(
        SabnzbdConnection connection,
        string nzoId,
        CancellationToken cancellationToken);

    Task<SabnzbdActionResult> PauseAsync(
        SabnzbdConnection connection,
        string nzoId,
        CancellationToken cancellationToken);

    Task<SabnzbdActionResult> ResumeAsync(
        SabnzbdConnection connection,
        string nzoId,
        CancellationToken cancellationToken);
}

public sealed class SabnzbdClient(HttpClient httpClient) : ISabnzbdClient
{
    public async Task<SabnzbdConnectionTestResult> TestAsync(
        SabnzbdConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAsync(
                connection,
                [
                    Pair("mode", "version")
                ],
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new SabnzbdConnectionTestResult(
                    false,
                    Error: DescribeStatus(response.StatusCode));
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var version = ReadString(document.RootElement, "version");

            return new SabnzbdConnectionTestResult(
                !string.IsNullOrWhiteSpace(version),
                version,
                string.IsNullOrWhiteSpace(version)
                    ? "SABnzbd returned no version."
                    : null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
            JsonException or
            UriFormatException)
        {
            return new SabnzbdConnectionTestResult(
                false,
                Error: "SABnzbd could not be reached or returned an invalid response.");
        }
    }

    public async Task<SabnzbdGrabResult> GrabAsync(
        SabnzbdConnection connection,
        SabnzbdGrabRequest grab,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grab);

        if (!grab.NzbUrl.IsAbsoluteUri ||
            (grab.NzbUrl.Scheme != Uri.UriSchemeHttp &&
             grab.NzbUrl.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "NZB URL must be an absolute HTTP(S) URL.",
                nameof(grab));
        }

        var settings = SabnzbdSettingsStore.NormalizeAndValidate(connection.Settings);
        var parameters = new List<KeyValuePair<string, string?>>
        {
            Pair("mode", "addurl"),
            Pair("name", grab.NzbUrl.ToString()),
            Pair("cat", string.IsNullOrWhiteSpace(grab.Category)
                ? settings.Category
                : grab.Category!.Trim())
        };

        if (!string.IsNullOrWhiteSpace(grab.NzbName))
        {
            parameters.Add(Pair("nzbname", grab.NzbName!.Trim()));
        }

        if (grab.Priority is int priority)
        {
            parameters.Add(Pair(
                "priority",
                priority.ToString(CultureInfo.InvariantCulture)));
        }

        using var response = await SendAsync(
            connection with { Settings = settings },
            parameters,
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new SabnzbdGrabResult(
                false,
                [],
                $"SABnzbd returned HTTP {(int)response.StatusCode}.");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var success = ReadBool(root, "status") ?? false;
            var ids = ReadStringArray(root, "nzo_ids");

            return new SabnzbdGrabResult(
                success && ids.Count > 0,
                ids,
                success && ids.Count > 0
                    ? null
                    : ReadString(root, "error") ?? "SABnzbd did not return a job ID.");
        }
        catch (JsonException exception)
        {
            throw new SabnzbdException(
                "SABnzbd returned invalid add-url JSON.",
                exception);
        }
    }

    public async Task<SabnzbdQueueSnapshot> GetQueueAsync(
        SabnzbdConnection connection,
        CancellationToken cancellationToken)
    {
        var settings = SabnzbdSettingsStore.NormalizeAndValidate(connection.Settings);
        using var response = await SendAsync(
            connection with { Settings = settings },
            [
                Pair("mode", "queue"),
                Pair("start", "0"),
                Pair("limit", settings.QueuePageSize.ToString(CultureInfo.InvariantCulture))
            ],
            cancellationToken);

        var body = await RequireBodyAsync(response, "queue", cancellationToken);
        return ParseQueueResponse(body);
    }

    public async Task<SabnzbdHistorySnapshot> GetHistoryAsync(
        SabnzbdConnection connection,
        IReadOnlyCollection<string>? nzoIds,
        CancellationToken cancellationToken)
    {
        var settings = SabnzbdSettingsStore.NormalizeAndValidate(connection.Settings);
        var parameters = new List<KeyValuePair<string, string?>>
        {
            Pair("mode", "history"),
            Pair("start", "0"),
            Pair("limit", settings.HistoryPageSize.ToString(CultureInfo.InvariantCulture))
        };

        if (nzoIds is { Count: > 0 })
        {
            parameters.Add(
                Pair(
                    "nzo_ids",
                    string.Join(
                        ',',
                        nzoIds
                            .Where(id => !string.IsNullOrWhiteSpace(id))
                            .Select(id => id.Trim()))));
        }

        using var response = await SendAsync(
            connection with { Settings = settings },
            parameters,
            cancellationToken);

        var body = await RequireBodyAsync(response, "history", cancellationToken);
        return ParseHistoryResponse(body);
    }

    public Task<SabnzbdActionResult> CancelAsync(
        SabnzbdConnection connection,
        string nzoId,
        bool deleteFiles,
        CancellationToken cancellationToken) =>
        ExecuteActionAsync(
            connection,
            [
                Pair("mode", "queue"),
                Pair("name", "delete"),
                Pair("value", ValidateNzoId(nzoId)),
                Pair("del_files", deleteFiles ? "1" : "0")
            ],
            cancellationToken);

    public Task<SabnzbdActionResult> RetryAsync(
        SabnzbdConnection connection,
        string nzoId,
        CancellationToken cancellationToken) =>
        ExecuteActionAsync(
            connection,
            [
                Pair("mode", "retry"),
                Pair("value", ValidateNzoId(nzoId))
            ],
            cancellationToken,
            readNewNzoId: true);

    public Task<SabnzbdActionResult> PauseAsync(
        SabnzbdConnection connection,
        string nzoId,
        CancellationToken cancellationToken) =>
        ExecuteActionAsync(
            connection,
            [
                Pair("mode", "queue"),
                Pair("name", "pause"),
                Pair("value", ValidateNzoId(nzoId))
            ],
            cancellationToken);

    public Task<SabnzbdActionResult> ResumeAsync(
        SabnzbdConnection connection,
        string nzoId,
        CancellationToken cancellationToken) =>
        ExecuteActionAsync(
            connection,
            [
                Pair("mode", "queue"),
                Pair("name", "resume"),
                Pair("value", ValidateNzoId(nzoId))
            ],
            cancellationToken);

    public static SabnzbdQueueSnapshot ParseQueueResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("queue", out var queue) ||
            queue.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("SABnzbd queue payload is missing.");
        }

        var jobs = new List<SabnzbdQueueJob>();
        if (queue.TryGetProperty("slots", out var slots) &&
            slots.ValueKind == JsonValueKind.Array)
        {
            foreach (var slot in slots.EnumerateArray())
            {
                if (slot.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var id = ReadString(slot, "nzo_id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                jobs.Add(
                    new SabnzbdQueueJob(
                        id,
                        ReadString(slot, "filename")
                            ?? ReadString(slot, "name")
                            ?? id,
                        ReadString(slot, "status"),
                        ReadString(slot, "cat")
                            ?? ReadString(slot, "category"),
                        ReadDouble(slot, "percentage"),
                        ReadString(slot, "timeleft"),
                        ReadBytes(slot, "bytes")
                            ?? ParseMegabytes(ReadString(slot, "mb")),
                        ReadBytes(slot, "bytesleft")
                            ?? ParseMegabytes(ReadString(slot, "mbleft"))));
            }
        }

        return new SabnzbdQueueSnapshot(
            ReadBool(queue, "paused") ?? false,
            ReadString(queue, "speed"),
            ReadString(queue, "timeleft"),
            jobs);
    }

    public static SabnzbdHistorySnapshot ParseHistoryResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("history", out var history) ||
            history.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("SABnzbd history payload is missing.");
        }

        var jobs = new List<SabnzbdHistoryJob>();
        if (history.TryGetProperty("slots", out var slots) &&
            slots.ValueKind == JsonValueKind.Array)
        {
            foreach (var slot in slots.EnumerateArray())
            {
                if (slot.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var id = ReadString(slot, "nzo_id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var failureMessage =
                    ReadString(slot, "fail_message")
                    ?? ReadString(slot, "error");

                jobs.Add(
                    new SabnzbdHistoryJob(
                        id,
                        ReadString(slot, "name")
                            ?? ReadString(slot, "filename")
                            ?? id,
                        ReadString(slot, "status"),
                        ReadString(slot, "category")
                            ?? ReadString(slot, "cat"),
                        ReadString(slot, "storage"),
                        failureMessage,
                        ClassifyFailure(
                            ReadString(slot, "status"),
                            failureMessage),
                        ReadUnixDateTimeOffset(slot, "completed")
                            ?? ReadDateTimeOffset(slot, "completed_at")));
            }
        }

        return new SabnzbdHistorySnapshot(jobs);
    }

    public static SabnzbdFailureKind ClassifyFailure(
        string? status,
        string? failureMessage)
    {
        var combined = $"{status} {failureMessage}".ToLowerInvariant();

        if (!combined.Contains("fail", StringComparison.Ordinal) &&
            !combined.Contains("error", StringComparison.Ordinal) &&
            !combined.Contains("password", StringComparison.Ordinal) &&
            !combined.Contains("unpack", StringComparison.Ordinal) &&
            !combined.Contains("verify", StringComparison.Ordinal) &&
            !combined.Contains("repair", StringComparison.Ordinal) &&
            !combined.Contains("script", StringComparison.Ordinal))
        {
            return SabnzbdFailureKind.None;
        }

        if (combined.Contains("password", StringComparison.Ordinal) ||
            combined.Contains("encrypted", StringComparison.Ordinal))
        {
            return SabnzbdFailureKind.Password;
        }

        if (combined.Contains("unpack", StringComparison.Ordinal) ||
            combined.Contains("rar", StringComparison.Ordinal) ||
            combined.Contains("7zip", StringComparison.Ordinal))
        {
            return SabnzbdFailureKind.Unpack;
        }

        if (combined.Contains("verify", StringComparison.Ordinal) ||
            combined.Contains("repair", StringComparison.Ordinal) ||
            combined.Contains("par2", StringComparison.Ordinal))
        {
            return SabnzbdFailureKind.Verification;
        }

        if (combined.Contains("script", StringComparison.Ordinal))
        {
            return SabnzbdFailureKind.Script;
        }

        if (combined.Contains("download", StringComparison.Ordinal) ||
            combined.Contains("article", StringComparison.Ordinal))
        {
            return SabnzbdFailureKind.Download;
        }

        return SabnzbdFailureKind.Unknown;
    }

    private async Task<SabnzbdActionResult> ExecuteActionAsync(
        SabnzbdConnection connection,
        IReadOnlyList<KeyValuePair<string, string?>> parameters,
        CancellationToken cancellationToken,
        bool readNewNzoId = false)
    {
        using var response = await SendAsync(
            connection,
            parameters,
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new SabnzbdActionResult(
                false,
                Error: $"SABnzbd returned HTTP {(int)response.StatusCode}.");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var success = ReadBool(root, "status") ?? false;
            var newNzoId = readNewNzoId
                ? ReadString(root, "nzo_id")
                : null;

            return new SabnzbdActionResult(
                success,
                newNzoId,
                success
                    ? null
                    : ReadString(root, "error") ?? "SABnzbd action failed.");
        }
        catch (JsonException exception)
        {
            throw new SabnzbdException(
                "SABnzbd returned invalid action JSON.",
                exception);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        SabnzbdConnection connection,
        IReadOnlyList<KeyValuePair<string, string?>> parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var settings = SabnzbdSettingsStore.NormalizeAndValidate(connection.Settings);
        if (string.IsNullOrWhiteSpace(connection.ApiKey))
        {
            throw new ArgumentException(
                "SABnzbd API key is required.",
                nameof(connection));
        }

        var all = new List<KeyValuePair<string, string?>>(parameters)
        {
            Pair("output", "json"),
            Pair("apikey", connection.ApiKey.Trim())
        };

        var query = string.Join(
            "&",
            all
                .Where(pair => pair.Value is not null)
                .Select(pair =>
                    $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));

        var uri = new Uri(
            $"{settings.BaseUrl}/api?{query}",
            UriKind.Absolute);

        var request = new HttpRequestMessage(HttpMethod.Get, uri);

        try
        {
            return await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        finally
        {
            request.Dispose();
        }
    }

    private static async Task<string> RequireBodyAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new SabnzbdException(
                $"SABnzbd {operation} failed with HTTP {(int)response.StatusCode}.");
        }

        return body;
    }

    private static string ValidateNzoId(string nzoId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nzoId);
        var normalized = nzoId.Trim();
        if (normalized.Length > 200 ||
            normalized.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) ||
                  character is '_' or '-' or '.')))
        {
            throw new ArgumentException(
                "Invalid SABnzbd job ID.",
                nameof(nzoId));
        }

        return normalized;
    }

    private static KeyValuePair<string, string?> Pair(
        string key,
        string? value) =>
        new(key, value);

    private static string DescribeStatus(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                "SABnzbd rejected the API key.",
            HttpStatusCode.NotFound =>
                "SABnzbd API endpoint was not found. Check the Base URL.",
            _ =>
                $"SABnzbd returned HTTP {(int)statusCode}."
        };

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        return value.ValueKind == JsonValueKind.Number
            ? value.GetRawText()
            : null;
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt32(out var number) =>
                number != 0,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) =>
                parsed,
            JsonValueKind.String when value.GetString() == "1" => true,
            JsonValueKind.String when value.GetString() == "0" => false,
            _ => null
        };
    }

    private static double? ReadDouble(JsonElement element, string propertyName)
    {
        var raw = ReadString(element, propertyName);
        return double.TryParse(
            raw,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static long? ReadBytes(JsonElement element, string propertyName)
    {
        var raw = ReadString(element, propertyName);
        return long.TryParse(
            raw,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static long? ParseMegabytes(string? value)
    {
        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var megabytes))
        {
            return null;
        }

        return checked((long)Math.Round(megabytes * 1024d * 1024d));
    }

    private static IReadOnlyList<string> ReadStringArray(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return array
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()?.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
    }

    private static DateTimeOffset? ReadUnixDateTimeOffset(
        JsonElement element,
        string propertyName)
    {
        var raw = ReadString(element, propertyName);
        if (!long.TryParse(raw, out var seconds) || seconds <= 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static DateTimeOffset? ReadDateTimeOffset(
        JsonElement element,
        string propertyName)
    {
        var raw = ReadString(element, propertyName);
        return DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var value)
            ? value
            : null;
    }
}
