using System.Globalization;
using System.Text.Json;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Books;

namespace AniLingo.Web.Features.Operations;

public sealed record SabnzbdRemoteJob(
    string Id,
    string Name,
    string Status,
    int? ProgressPercent,
    long? BytesTotal,
    long? BytesCompleted,
    DateTime? EtaUtc,
    bool IsHistory);

public sealed record SabnzbdRemoteSnapshot(
    IReadOnlyList<SabnzbdRemoteJob> Jobs,
    double? QueueBytesPerSecond);

public sealed class SabnzbdOperationsClient(
    HttpClient httpClient,
    IConfiguration configuration)
{
    public const string ProviderId = "sabnzbd";

    public async Task<IReadOnlySet<string>> CaptureJobIdsAsync(
        CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        return snapshot.Jobs
            .Select(x => x.Id)
            .ToHashSet(StringComparer.Ordinal);
    }

    public async Task<string?> ResolveNewJobIdAsync(
        IReadOnlySet<string> existingIds,
        string? expectedName,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = await GetSnapshotAsync(cancellationToken);
            var candidates = snapshot.Jobs
                .Where(x => !existingIds.Contains(x.Id))
                .ToArray();

            var matching = candidates
                .FirstOrDefault(x => NamesMatch(x.Name, expectedName));
            if (matching is not null)
            {
                return matching.Id;
            }

            if (candidates.Length == 1)
            {
                return candidates[0].Id;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(500),
                cancellationToken);
        }

        return null;
    }

    public async Task<SabnzbdRemoteSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken)
    {
        var queueDocument = await RequestAsync(
            "queue",
            cancellationToken);
        using (queueDocument)
        {
            var historyDocument = await RequestAsync(
                "history",
                cancellationToken);
            using (historyDocument)
            {
                return ParseSnapshot(
                    queueDocument.RootElement,
                    historyDocument.RootElement,
                    DateTime.UtcNow);
            }
        }
    }

    public static SabnzbdRemoteSnapshot ParseSnapshot(
        JsonElement queueRoot,
        JsonElement historyRoot,
        DateTime nowUtc)
    {
        var jobs = new List<SabnzbdRemoteJob>();
        double? queueSpeed = null;

        if (TryGetContainer(queueRoot, "queue", out var queue))
        {
            var kbPerSecond = ReadDouble(queue, "kbpersec");
            if (kbPerSecond is >= 0)
            {
                queueSpeed = kbPerSecond.Value * 1024d;
            }

            if (queue.TryGetProperty("slots", out var slots)
                && slots.ValueKind == JsonValueKind.Array)
            {
                foreach (var slot in slots.EnumerateArray())
                {
                    var id = ReadString(slot, "nzo_id");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    var totalMb = ReadDouble(slot, "mb");
                    var leftMb = ReadDouble(slot, "mbleft");
                    var totalBytes = MegabytesToBytes(totalMb);
                    var leftBytes = MegabytesToBytes(leftMb);
                    long? completedBytes = null;
                    if (totalBytes is { } total && leftBytes is { } left)
                    {
                        completedBytes = Math.Clamp(
                            total - left,
                            0,
                            total);
                    }

                    var progress = ReadInt(slot, "percentage");
                    if (progress is not null)
                    {
                        progress = Math.Clamp(progress.Value, 0, 100);
                    }

                    var timeLeft = ParseTimeLeft(
                        ReadString(slot, "timeleft"));
                    DateTime? eta = timeLeft is { } remaining
                        && remaining > TimeSpan.Zero
                        ? nowUtc.Add(remaining)
                        : null;

                    jobs.Add(
                        new SabnzbdRemoteJob(
                            id,
                            ReadString(slot, "filename")
                                ?? ReadString(slot, "name")
                                ?? "SABnzbd download",
                            ReadString(slot, "status") ?? "Queued",
                            progress,
                            totalBytes,
                            completedBytes,
                            eta,
                            IsHistory: false));
                }
            }
        }

        if (TryGetContainer(historyRoot, "history", out var history)
            && history.TryGetProperty("slots", out var historySlots)
            && historySlots.ValueKind == JsonValueKind.Array)
        {
            foreach (var slot in historySlots.EnumerateArray())
            {
                var id = ReadString(slot, "nzo_id");
                if (string.IsNullOrWhiteSpace(id)
                    || jobs.Any(x => string.Equals(
                        x.Id,
                        id,
                        StringComparison.Ordinal)))
                {
                    continue;
                }

                jobs.Add(
                    new SabnzbdRemoteJob(
                        id,
                        ReadString(slot, "name")
                            ?? ReadString(slot, "nzb_name")
                            ?? "SABnzbd download",
                        ReadString(slot, "status") ?? "History",
                        ProgressPercent: null,
                        BytesTotal: ReadLong(slot, "bytes"),
                        BytesCompleted: ReadLong(slot, "bytes"),
                        EtaUtc: null,
                        IsHistory: true));
            }
        }

        return new SabnzbdRemoteSnapshot(
            jobs,
            queueSpeed);
    }

    private async Task<JsonDocument> RequestAsync(
        string mode,
        CancellationToken cancellationToken)
    {
        var settings = ResolveSettings();

        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["mode"] = mode,
                ["apikey"] = settings.ApiKey,
                ["output"] = "json",
                ["start"] = "0",
                ["limit"] = "200"
            });

        using var response = await httpClient.PostAsync(
            new Uri(settings.BaseUri, "api"),
            content,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream =
            await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
    }

    private SabnzbdResolvedSettings ResolveSettings()
    {
        var stored = BookIntegrationSettingsStore.Load();

        var merged = BookIntegrationSettingsStore.Normalize(
            new BookIntegrationSettings(
                FirstNonEmpty(
                    configuration["Books:SABnzbd:BaseUrl"],
                    stored.SabnzbdBaseUrl),
                FirstNonEmpty(
                    configuration["Books:SABnzbd:ApiKey"],
                    stored.SabnzbdApiKey),
                FirstNonEmpty(
                    configuration["Books:SABnzbd:Category"],
                    stored.SabnzbdCategory),
                stored.InboxPath));

        if (!Uri.TryCreate(
                merged.SabnzbdBaseUrl?.TrimEnd('/') + "/",
                UriKind.Absolute,
                out var baseUri)
            || string.IsNullOrWhiteSpace(merged.SabnzbdApiKey))
        {
            throw new InvalidOperationException(
                "SABnzbd is not configured for operation monitoring.");
        }

        return new SabnzbdResolvedSettings(
            baseUri,
            merged.SabnzbdApiKey);
    }

    private static bool TryGetContainer(
        JsonElement root,
        string property,
        out JsonElement container)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(property, out container)
            && container.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        container = default;
        return false;
    }

    private static bool NamesMatch(
        string candidate,
        string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        var left = candidate.Trim();
        var right = expected.Trim();

        return left.Equals(
                right,
                StringComparison.OrdinalIgnoreCase)
            || left.Contains(
                right,
                StringComparison.OrdinalIgnoreCase)
            || right.Contains(
                left,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstNonEmpty(
        string? primary,
        string? fallback) =>
        string.IsNullOrWhiteSpace(primary)
            ? fallback?.Trim()
            : primary.Trim();

    private static string? ReadString(
        JsonElement element,
        string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static double? ReadDouble(
        JsonElement element,
        string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && double.TryParse(
                value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number)
            ? number
            : null;
    }

    private static int? ReadInt(
        JsonElement element,
        string property)
    {
        var value = ReadDouble(element, property);
        return value is null
            ? null
            : (int)Math.Round(value.Value);
    }

    private static long? ReadLong(
        JsonElement element,
        string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && long.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out number)
            ? number
            : null;
    }

    private static long? MegabytesToBytes(double? megabytes)
    {
        if (megabytes is null
            || !double.IsFinite(megabytes.Value)
            || megabytes.Value < 0)
        {
            return null;
        }

        var bytes = megabytes.Value * 1024d * 1024d;
        return bytes >= long.MaxValue
            ? long.MaxValue
            : (long)Math.Round(bytes);
    }

    private static TimeSpan? ParseTimeLeft(string? value) =>
        TimeSpan.TryParse(
            value,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;

    private sealed record SabnzbdResolvedSettings(
        Uri BaseUri,
        string ApiKey);
}

public sealed class SabnzbdOperationMonitorService(
    IServiceScopeFactory scopeFactory,
    SabnzbdOperationsClient client,
    ILogger<SabnzbdOperationMonitorService> logger) : BackgroundService
{
    private static readonly TimeSpan ActivePollInterval =
        TimeSpan.FromSeconds(3);
    private static readonly TimeSpan IdlePollInterval =
        TimeSpan.FromSeconds(12);
    private static readonly TimeSpan MissingJobTimeout =
        TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            var hasActiveJobs = false;

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var store = new OperationStore(db);
                var operations = await store.ListActiveExternalAsync(
                    SabnzbdOperationsClient.ProviderId,
                    stoppingToken);

                hasActiveJobs = operations.Count > 0;
                if (hasActiveJobs)
                {
                    var snapshot = await client.GetSnapshotAsync(stoppingToken);
                    await ApplySnapshotAsync(
                        store,
                        operations,
                        snapshot,
                        stoppingToken);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Could not refresh SABnzbd operation status.");
            }

            try
            {
                await Task.Delay(
                    hasActiveJobs
                        ? ActivePollInterval
                        : IdlePollInterval,
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static async Task ApplySnapshotAsync(
        OperationStore store,
        IReadOnlyList<OperationSnapshot> operations,
        SabnzbdRemoteSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        foreach (var operation in operations)
        {
            if (string.IsNullOrWhiteSpace(operation.ExternalId))
            {
                continue;
            }

            var remote = snapshot.Jobs.FirstOrDefault(
                x => string.Equals(
                    x.Id,
                    operation.ExternalId,
                    StringComparison.Ordinal));

            if (remote is null)
            {
                if (DateTime.UtcNow - operation.UpdatedAtUtc
                    >= MissingJobTimeout)
                {
                    await store.MarkFailedAsync(
                        operation.Id,
                        "SABnzbd job no longer appears in queue or history.",
                        cancellationToken);
                }

                continue;
            }

            if (IsCompleted(remote.Status))
            {
                await store.MarkSucceededAsync(
                    operation.Id,
                    "SABnzbd download and post-processing completed.",
                    cancellationToken);
                continue;
            }

            if (IsFailed(remote.Status))
            {
                await store.MarkFailedAsync(
                    operation.Id,
                    $"SABnzbd reported status {remote.Status}.",
                    cancellationToken);
                continue;
            }

            if (operation.Status == OperationStatus.Queued)
            {
                await store.MarkRunningAsync(
                    operation.Id,
                    cancellationToken);
            }

            var progress = remote.ProgressPercent;
            if (remote.IsHistory && progress is null)
            {
                progress = Math.Max(
                    operation.ProgressPercent ?? 0,
                    99);
            }

            var speed = snapshot.Jobs.Count(x => !x.IsHistory) == 1
                ? snapshot.QueueBytesPerSecond
                : null;

            await store.ReportProgressAsync(
                operation.Id,
                progress,
                remote.IsHistory
                    ? $"SABnzbd post-processing: {remote.Status}."
                    : $"SABnzbd: {remote.Status}.",
                remote.BytesCompleted,
                remote.BytesTotal,
                speed,
                remote.EtaUtc,
                cancellationToken);
        }
    }

    private static bool IsCompleted(string status) =>
        status.Equals(
            "Completed",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsFailed(string status) =>
        status.Equals(
                "Failed",
                StringComparison.OrdinalIgnoreCase)
            || status.Contains(
                "Failure",
                StringComparison.OrdinalIgnoreCase);
}
