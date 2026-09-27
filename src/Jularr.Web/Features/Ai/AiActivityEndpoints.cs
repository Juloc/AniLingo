using System.Text.Json;
using Jularr.Web.Features.Auth;
using Microsoft.AspNetCore.Antiforgery;

namespace Jularr.Web.Features.Ai;

public sealed record AiActivityUsageDto(
    long Input,
    long Cached,
    long Output,
    long Reasoning,
    bool Estimated);

/// <summary>Browser shape of one activity. Owner "all profiles" views also carry the profile id.</summary>
public sealed record AiActivityDto(
    Guid Id,
    string? ProfileId,
    string Operation,
    string State,
    bool Active,
    string ProviderId,
    string? Transport,
    string? Model,
    string? ReasoningEffort,
    DateTimeOffset StartedAt,
    long ElapsedMs,
    int? ProgressCurrent,
    int? ProgressTotal,
    AiActivityUsageDto? Usage,
    double? ContextWindowPercent,
    int SourceTokens,
    int ContextTokens,
    int Retries,
    string? Error)
{
    public static AiActivityDto From(AiActivitySnapshot snapshot, bool includeProfile, DateTimeOffset now) =>
        new(
            snapshot.Id,
            includeProfile ? snapshot.ProfileId : null,
            snapshot.Operation,
            AiActivityStates.Name(snapshot.State),
            snapshot.IsActive,
            snapshot.ProviderId,
            snapshot.Transport,
            snapshot.Model,
            snapshot.ReasoningEffort,
            snapshot.StartedAt,
            (long)snapshot.Elapsed(now).TotalMilliseconds,
            snapshot.ProgressCurrent,
            snapshot.ProgressTotal,
            snapshot.Usage is { } usage
                ? new AiActivityUsageDto(usage.InputTokens, usage.CachedInputTokens, usage.OutputTokens, usage.ReasoningOutputTokens, usage.Estimated)
                : null,
            snapshot.ContextWindowPercent is { } percent ? Math.Round(percent, 1) : null,
            snapshot.SourceTokens,
            snapshot.ContextTokens,
            snapshot.Retries,
            snapshot.Error);
}

public static class AiActivityStates
{
    public static string Name(AiActivityState state) => state switch
    {
        AiActivityState.Queued => "queued",
        AiActivityState.PreparingContext => "preparing",
        AiActivityState.Running => "running",
        AiActivityState.Retrying => "retrying",
        AiActivityState.Completed => "completed",
        AiActivityState.Failed => "failed",
        _ => "cancelled"
    };

    public static IReadOnlyList<AiActivityState> All { get; } = Enum.GetValues<AiActivityState>();
}

/// <summary>
/// Live AI activity over server-sent events plus cancellation. Profiles see their own requests; the
/// owner may request <c>scope=all</c>. Cancelling requires the page's antiforgery token.
/// </summary>
public static class AiActivityEndpoints
{
    public const string BasePath = "/api/ai/activity";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapAiActivity(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath).RequireAuthorization();

        group.MapGet("/", (string? scope, AiActivityTracker tracker, CurrentAccountContext account, TimeProvider time) =>
        {
            if (!TryResolveScope(scope, account, out var profileId))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            return Results.Json(List(tracker, profileId, time.GetUtcNow()), JsonOptions);
        });

        group.MapGet("/stream", async (
            string? scope,
            HttpContext context,
            AiActivityTracker tracker,
            CurrentAccountContext account,
            TimeProvider time) =>
        {
            if (!TryResolveScope(scope, account, out var profileId))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await StreamAsync(context, tracker, profileId, time, context.RequestAborted);
        });

        group.MapPost("/{id:guid}/cancel", async (
            Guid id,
            HttpContext context,
            IAntiforgery antiforgery,
            AiActivityTracker tracker,
            CurrentAccountContext account) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return Results.BadRequest();
            }

            return tracker.Cancel(id, account.ProfileId, account.IsOwner) switch
            {
                AiActivityCancelResult.Cancelled => Results.Accepted(),
                AiActivityCancelResult.NotActive => Results.Conflict(),
                AiActivityCancelResult.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
                _ => Results.NotFound()
            };
        });

        return endpoints;
    }

    public static IReadOnlyList<AiActivityDto> List(AiActivityTracker tracker, string? profileId, DateTimeOffset now) =>
        tracker.List(profileId)
            .Select(x => AiActivityDto.From(x, profileId is null, now))
            .ToArray();

    public static bool TryResolveScope(string? scope, CurrentAccountContext account, out string? profileId)
    {
        if (string.Equals(scope, "all", StringComparison.Ordinal))
        {
            profileId = null;
            return account.IsOwner;
        }

        profileId = account.ProfileId;
        return true;
    }

    private static async Task StreamAsync(
        HttpContext context,
        AiActivityTracker tracker,
        string? profileId,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache, no-store";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var version = tracker.Version;
                var payload = JsonSerializer.Serialize(List(tracker, profileId, time.GetUtcNow()), JsonOptions);
                await context.Response.WriteAsync($"event: activity\ndata: {payload}\n\n", cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);

                try
                {
                    // Push on change; resend at most every 20 s as a heartbeat through proxies.
                    await tracker.WaitForChangeAsync(version, cancellationToken)
                        .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
                }
                catch (TimeoutException)
                {
                }

                // Coalesce bursts of token updates into one message.
                await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
