using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using Jularr.Web.Features.Ai;

namespace Jularr.Web.Infrastructure.Ai;

public sealed record CodexTurnRequest(
    string Prompt,
    string OutputSchema,
    string WorkingDirectory,
    string? Model,
    string? ReasoningEffort,
    string? ServiceTier,
    TimeSpan Timeout);

public sealed record CodexTurnResult(
    string Text,
    string? Model,
    AiTokenUsage? Usage,
    long? ContextWindow);

/// <summary>
/// Typed operations on top of <see cref="CodexAppServerClient"/>: model catalog, account, rate limits
/// (including rolling updates) and single structured turns on ephemeral, read-only threads.
/// </summary>
public sealed class CodexAppServerGateway
{
    public const string ModelList = "model/list";
    public const string AccountRead = "account/read";
    public const string RateLimitsRead = "account/rateLimits/read";
    public const string RateLimitsUpdated = "account/rateLimits/updated";
    public const string ThreadStart = "thread/start";
    public const string TurnStart = "turn/start";
    public const string TurnInterrupt = "turn/interrupt";
    public const string TokenUsageUpdated = "thread/tokenUsage/updated";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Least-privilege overrides applied to every Jularr thread: no shell, web search, plugins or
    /// tool suggestions, even when the connected Codex supports them.
    /// </summary>
    private static readonly Dictionary<string, object> LeastPrivilegeConfig = new(StringComparer.Ordinal)
    {
        ["features.shell_tool"] = false,
        ["features.standalone_web_search"] = false,
        ["features.plugins"] = false,
        ["features.tool_suggest"] = false,
        ["model_verbosity"] = "low"
    };

    private readonly CodexAppServerClient client;
    private readonly TimeProvider time;
    private readonly ConcurrentDictionary<string, Channel<CodexAppServerNotification>> threads = new(StringComparer.Ordinal);
    private readonly object quotaGate = new();
    private AiQuotaSnapshot? quota;

    public CodexAppServerGateway(CodexAppServerClient client, TimeProvider time)
    {
        this.client = client;
        this.time = time;
        client.Notification += OnNotification;
        client.Disconnected += OnDisconnected;
    }

    private void OnDisconnected()
    {
        foreach (var channel in threads.Values)
        {
            channel.Writer.TryComplete(new InvalidOperationException("Codex app-server stopped during the request."));
        }
    }

    public CodexAppServerClient Client => client;

    public AiQuotaSnapshot? LatestQuota
    {
        get
        {
            lock (quotaGate)
            {
                return quota;
            }
        }
    }

    public AiAccountInfo? LatestAccount { get; private set; }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        client.TryConnectAsync(cancellationToken);

    /// <summary>True unless the server proved that thread/turn methods are missing.</summary>
    public bool SupportsTurns =>
        client.GetMethodState(ThreadStart) != AiCapabilityState.Unsupported
        && client.GetMethodState(TurnStart) != AiCapabilityState.Unsupported;

    public AiProviderCapabilities GetCapabilities(bool execAvailable)
    {
        var states = new Dictionary<AiCapability, AiCapabilityState>();
        var transport = AiTransports.CodexExec;

        if (client.IsAvailable == true)
        {
            transport = SupportsTurns ? AiTransports.CodexAppServer : AiTransports.CodexExec;
            states[AiCapability.ModelCatalog] = client.GetMethodState(ModelList);
            states[AiCapability.AccountStatus] = client.GetMethodState(AccountRead);
            states[AiCapability.RateLimits] = client.GetMethodState(RateLimitsRead);
            states[AiCapability.RateLimitUpdates] = client.HasSeenNotification(RateLimitsUpdated)
                ? AiCapabilityState.Supported
                : AiCapabilityState.Unknown;
            states[AiCapability.TurnLifecycle] = client.GetMethodState(TurnStart);
            states[AiCapability.TokenUsage] = client.HasSeenNotification(TokenUsageUpdated)
                ? AiCapabilityState.Supported
                : AiCapabilityState.Unknown;
            states[AiCapability.Interrupt] = client.GetMethodState(TurnInterrupt) == AiCapabilityState.Unsupported
                ? AiCapabilityState.Unsupported
                : SupportsTurns ? AiCapabilityState.Supported : AiCapabilityState.Unknown;
        }
        else if (client.IsAvailable == false)
        {
            foreach (var capability in new[]
                     {
                         AiCapability.ModelCatalog, AiCapability.AccountStatus, AiCapability.RateLimits,
                         AiCapability.RateLimitUpdates, AiCapability.TurnLifecycle
                     })
            {
                states[capability] = AiCapabilityState.Unsupported;
            }
        }

        if (execAvailable && transport == AiTransports.CodexExec)
        {
            // codex exec --json reports final token usage and stops when its process is killed.
            states[AiCapability.TokenUsage] = AiCapabilityState.Supported;
            states[AiCapability.Interrupt] = AiCapabilityState.Supported;
        }

        states[AiCapability.MaxOutputTokens] = AiCapabilityState.Unsupported;
        return new AiProviderCapabilities(transport, states, client.DetectedAt);
    }

    public async Task<IReadOnlyList<AiModelDescriptor>> ListModelsAsync(CancellationToken cancellationToken)
    {
        if (!await client.TryConnectAsync(cancellationToken))
        {
            throw new InvalidOperationException(client.LastError ?? "Codex app-server is not available.");
        }

        var models = new List<AiModelDescriptor>();
        string? cursor = null;
        for (var page = 0; page < 20; page++)
        {
            JsonElement result;
            try
            {
                result = await client.RequestAsync(
                    ModelList,
                    new { cursor, limit = 100, includeHidden = false },
                    RequestTimeout,
                    cancellationToken);
            }
            catch (CodexAppServerException exception) when (exception.IsMethodNotFound)
            {
                throw new AiModelDiscoveryUnsupportedException("This Codex version cannot list models.");
            }

            models.AddRange(ParseModels(result));
            cursor = String(result, "nextCursor");
            if (cursor is null)
            {
                break;
            }
        }

        return models;
    }

    public async Task<AiAccountInfo?> ReadAccountAsync(CancellationToken cancellationToken)
    {
        var result = await client.RequestAsync(AccountRead, new { refreshToken = false }, RequestTimeout, cancellationToken);
        var requiresAuth = result.TryGetProperty("requiresOpenaiAuth", out var requires) && requires.ValueKind == JsonValueKind.True;
        if (!result.TryGetProperty("account", out var account) || account.ValueKind != JsonValueKind.Object)
        {
            return new AiAccountInfo(null, null, requiresAuth);
        }

        // The account e-mail is intentionally not read: it is not needed to operate the provider.
        var info = new AiAccountInfo(String(account, "type"), String(account, "planType"), requiresAuth);
        LatestAccount = info;
        return info;
    }

    public async Task<AiQuotaSnapshot> ReadRateLimitsAsync(CancellationToken cancellationToken)
    {
        var result = await client.RequestAsync(RateLimitsRead, new { }, RequestTimeout, cancellationToken);
        var snapshot = ParseRateLimits(result, time.GetUtcNow());
        lock (quotaGate)
        {
            quota = snapshot;
        }

        return snapshot;
    }

    public async Task<CodexTurnResult> RunTurnAsync(
        CodexTurnRequest request,
        AiActivityHandle? activity,
        CancellationToken cancellationToken)
    {
        activity?.SetTransport(AiTransports.CodexAppServer);

        var thread = await client.RequestAsync(
            ThreadStart,
            new
            {
                model = request.Model,
                serviceTier = request.ServiceTier,
                cwd = request.WorkingDirectory,
                approvalPolicy = "never",
                sandbox = "read-only",
                config = LeastPrivilegeConfig,
                ephemeral = true
            },
            RequestTimeout,
            cancellationToken);

        var threadId = thread.TryGetProperty("thread", out var threadValue) ? String(threadValue, "id") : null;
        if (threadId is null)
        {
            throw new InvalidOperationException("Codex app-server returned no thread.");
        }

        var model = String(thread, "model") ?? request.Model;
        activity?.SetModel(model, String(thread, "reasoningEffort") ?? request.ReasoningEffort);

        var events = Channel.CreateUnbounded<CodexAppServerNotification>();
        threads[threadId] = events;

        try
        {
            using var schema = JsonDocument.Parse(request.OutputSchema);
            var turn = await client.RequestAsync(
                TurnStart,
                new
                {
                    threadId,
                    input = new object[] { new { type = "text", text = request.Prompt, text_elements = Array.Empty<object>() } },
                    effort = request.ReasoningEffort,
                    outputSchema = schema.RootElement
                },
                RequestTimeout,
                cancellationToken);

            var turnId = turn.TryGetProperty("turn", out var turnValue) ? String(turnValue, "id") : null;
            if (turnId is null)
            {
                throw new InvalidOperationException("Codex app-server returned no turn.");
            }

            activity?.SetState(AiActivityState.Running);
            return await AwaitTurnAsync(threadId, turnId, model, events.Reader, request.Timeout, activity, cancellationToken);
        }
        finally
        {
            threads.TryRemove(threadId, out _);
        }
    }

    private async Task<CodexTurnResult> AwaitTurnAsync(
        string threadId,
        string turnId,
        string? model,
        ChannelReader<CodexAppServerNotification> events,
        TimeSpan timeout,
        AiActivityHandle? activity,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        string? text = null;
        AiTokenUsage? usage = null;
        long? contextWindow = null;

        try
        {
            while (true)
            {
                var notification = await events.ReadAsync(deadline.Token);
                var parameters = notification.Params;
                switch (notification.Method)
                {
                    case "item/completed":
                        if (parameters.TryGetProperty("item", out var item)
                            && String(item, "type") == "agentMessage"
                            && String(item, "text") is { } message)
                        {
                            text = message;
                        }

                        break;

                    case TokenUsageUpdated:
                        if (parameters.TryGetProperty("tokenUsage", out var tokenUsage))
                        {
                            usage = ParseUsage(tokenUsage.TryGetProperty("total", out var total) ? total : default) ?? usage;
                            contextWindow = Long(tokenUsage, "modelContextWindow") ?? contextWindow;
                            if (usage is not null)
                            {
                                activity?.ReportUsage(usage, contextWindow);
                            }
                        }

                        break;

                    case "error":
                        if (parameters.TryGetProperty("willRetry", out var retry) && retry.ValueKind == JsonValueKind.True)
                        {
                            activity?.ReportRetry();
                        }

                        break;

                    case "turn/completed":
                        if (!parameters.TryGetProperty("turn", out var completed) || String(completed, "id") != turnId)
                        {
                            break;
                        }

                        switch (String(completed, "status"))
                        {
                            case "completed":
                                text ??= LastAgentMessage(completed);
                                if (string.IsNullOrWhiteSpace(text))
                                {
                                    throw new InvalidOperationException("Codex returned no result.");
                                }

                                return new CodexTurnResult(text, model, usage, contextWindow);

                            case "interrupted":
                                throw new OperationCanceledException("Codex turn was interrupted.");

                            default:
                                var error = completed.TryGetProperty("error", out var errorValue) && errorValue.ValueKind == JsonValueKind.Object
                                    ? String(errorValue, "message")
                                    : null;
                                throw new InvalidOperationException(AiErrorSanitizer.Sanitize(error) ?? "Codex could not complete the request.");
                        }
                }
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            await InterruptAsync(threadId, turnId);
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Codex request was cancelled.", null, cancellationToken);
            }

            throw new InvalidOperationException("Codex request timed out.");
        }
        catch (ChannelClosedException)
        {
            throw new InvalidOperationException("Codex app-server stopped during the request.");
        }
    }

    private async Task InterruptAsync(string threadId, string turnId)
    {
        try
        {
            await client.RequestAsync(TurnInterrupt, new { threadId, turnId }, TimeSpan.FromSeconds(5), CancellationToken.None);
        }
        catch (Exception exception) when (exception is CodexAppServerException or InvalidOperationException or OperationCanceledException)
        {
            // Best effort: the turn may already have finished or the server may be gone.
        }
    }

    private void OnNotification(CodexAppServerNotification notification)
    {
        if (notification.Method == RateLimitsUpdated)
        {
            if (notification.Params.ValueKind == JsonValueKind.Object
                && notification.Params.TryGetProperty("rateLimits", out var limits)
                && ParseBucket(limits, null) is { } bucket)
            {
                lock (quotaGate)
                {
                    var now = time.GetUtcNow();
                    quota = quota is null
                        ? new AiQuotaSnapshot(null, [bucket], now, now)
                        : quota.Merge(bucket, now);
                }
            }

            return;
        }

        if (notification.Params.ValueKind == JsonValueKind.Object
            && String(notification.Params, "threadId") is { } threadId
            && threads.TryGetValue(threadId, out var channel))
        {
            channel.Writer.TryWrite(notification);
        }
    }

    public static IReadOnlyList<AiModelDescriptor> ParseModels(JsonElement result)
    {
        if (!result.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var models = new List<AiModelDescriptor>();
        foreach (var entry in data.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || (entry.TryGetProperty("hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True))
            {
                continue;
            }

            var slug = String(entry, "model") ?? String(entry, "id");
            if (slug is null)
            {
                continue;
            }

            var efforts = entry.TryGetProperty("supportedReasoningEfforts", out var effortArray) && effortArray.ValueKind == JsonValueKind.Array
                ? effortArray.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.Object)
                    .Select(x => (Effort: String(x, "reasoningEffort"), Description: String(x, "description")))
                    .Where(x => x.Effort is not null && AiProfileSettings.IsValidOptionId(x.Effort))
                    .Select(x => new AiReasoningOption(x.Effort!, x.Description))
                    .ToArray()
                : [];

            var tiers = entry.TryGetProperty("serviceTiers", out var tierArray) && tierArray.ValueKind == JsonValueKind.Array
                ? tierArray.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.Object)
                    .Select(x => (Id: String(x, "id"), Name: String(x, "name"), Description: String(x, "description")))
                    .Where(x => x.Id is not null && AiProfileSettings.IsValidOptionId(x.Id))
                    .Select(x => new AiServiceTierOption(x.Id!, x.Name ?? x.Id!, x.Description))
                    .ToArray()
                : [];

            models.Add(new AiModelDescriptor(
                slug,
                String(entry, "displayName") ?? slug,
                String(entry, "description"),
                efforts,
                String(entry, "defaultReasoningEffort"),
                tiers,
                String(entry, "defaultServiceTier"),
                entry.TryGetProperty("isDefault", out var isDefault) && isDefault.ValueKind == JsonValueKind.True,
                null));
        }

        return models;
    }

    public static AiQuotaSnapshot ParseRateLimits(JsonElement result, DateTimeOffset now)
    {
        bool? ordinaryAllowed = result.TryGetProperty("ordinaryUsageAllowed", out var allowed)
            ? allowed.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            }
            : null;

        var buckets = new List<AiQuotaBucket>();
        if (result.TryGetProperty("rateLimitsByLimitId", out var byId) && byId.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in byId.EnumerateObject())
            {
                if (ParseBucket(property.Value, property.Name) is { } bucket)
                {
                    buckets.Add(bucket);
                }
            }
        }

        if (buckets.Count == 0
            && result.TryGetProperty("rateLimits", out var single)
            && ParseBucket(single, null) is { } only)
        {
            buckets.Add(only);
        }

        return new AiQuotaSnapshot(ordinaryAllowed, buckets, now, null);
    }

    public static AiQuotaBucket? ParseBucket(JsonElement value, string? key)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        AiQuotaCredits? credits = null;
        if (value.TryGetProperty("credits", out var creditValue) && creditValue.ValueKind == JsonValueKind.Object)
        {
            credits = new AiQuotaCredits(
                creditValue.TryGetProperty("hasCredits", out var has) && has.ValueKind == JsonValueKind.True,
                creditValue.TryGetProperty("unlimited", out var unlimited) && unlimited.ValueKind == JsonValueKind.True,
                String(creditValue, "balance"));
        }

        AiQuotaSpendLimit? spend = null;
        if (value.TryGetProperty("individualLimit", out var spendValue) && spendValue.ValueKind == JsonValueKind.Object)
        {
            spend = new AiQuotaSpendLimit(
                String(spendValue, "limit") ?? "",
                String(spendValue, "used") ?? "",
                Double(spendValue, "remainingPercent") ?? 0,
                UnixSeconds(Long(spendValue, "resetsAt")));
        }

        return new AiQuotaBucket(
            String(value, "limitId") ?? key,
            String(value, "limitName"),
            String(value, "normalModelSlug"),
            ParseWindow(value, "primary"),
            ParseWindow(value, "secondary"),
            credits,
            spend,
            value.TryGetProperty("spendControlReached", out var reached)
                ? reached.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null }
                : null,
            String(value, "planType"),
            String(value, "rateLimitReachedType"));
    }

    private static AiQuotaWindow? ParseWindow(JsonElement bucket, string name)
    {
        if (!bucket.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var used = Double(window, "usedPercent");
        if (used is null)
        {
            return null;
        }

        var minutes = Long(window, "windowDurationMins");
        return new AiQuotaWindow(
            used.Value,
            minutes is null ? null : (int)Math.Clamp(minutes.Value, 0, int.MaxValue),
            UnixSeconds(Long(window, "resetsAt")));
    }

    public static AiTokenUsage? ParseUsage(JsonElement breakdown)
    {
        if (breakdown.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new AiTokenUsage(
            Long(breakdown, "inputTokens") ?? 0,
            Long(breakdown, "cachedInputTokens") ?? 0,
            Long(breakdown, "outputTokens") ?? 0,
            Long(breakdown, "reasoningOutputTokens") ?? 0,
            Estimated: false);
    }

    private static string? LastAgentMessage(JsonElement turn) =>
        turn.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Object && String(x, "type") == "agentMessage")
                .Select(x => String(x, "text"))
                .LastOrDefault(x => !string.IsNullOrWhiteSpace(x))
            : null;

    private static DateTimeOffset? UnixSeconds(long? value) =>
        value is > 0 ? DateTimeOffset.FromUnixTimeSeconds(value.Value) : null;

    internal static string? String(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    internal static long? Long(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.Number
            ? property.TryGetInt64(out var integer)
                ? integer
                : (long)Math.Round(property.GetDouble(), MidpointRounding.AwayFromZero)
            : null;

    private static double? Double(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.GetDouble(),
            JsonValueKind.String when double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }
}
