using Jularr.Web.Features.OfflineLibrary;

namespace Jularr.Web.Features.ClientApi;

/// <summary>
/// Additive v1 contract for smart offline prefetch (#415). The server owns the
/// policy and the selection; the client reports what is on the device and
/// executes the returned plan through the existing offline endpoints
/// (<see cref="ClientApiOfflineRoutes.Download"/> for episodes,
/// <see cref="ClientApiOfflineLibraryRoutes.Chapter"/> for chapters). Items
/// downloaded from a plan must be recorded with origin <c>prefetched</c>; an
/// item the user later saves explicitly (or keeps) is reported as
/// <c>explicit</c>, which permanently protects it from prefetch eviction.
/// </summary>
public static class ClientApiOfflinePrefetchRoutes
{
    public static string Policy =>
        $"{ClientApiContract.BasePath}/offline/prefetch/policy";

    public static string Plan =>
        $"{ClientApiContract.BasePath}/offline/prefetch/plan";
}

public static class ClientApiOfflinePrefetchContract
{
    /// <summary>Upper bound of inventory items accepted by one plan request.</summary>
    public const int MaxInventoryItems = 5000;

    public static string KindName(OfflinePrefetchKind kind) =>
        kind switch
        {
            OfflinePrefetchKind.Episode => "episode",
            OfflinePrefetchKind.Chapter => "chapter",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    public static bool TryParseKind(string? value, out OfflinePrefetchKind kind)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "episode":
                kind = OfflinePrefetchKind.Episode;
                return true;
            case "chapter":
                kind = OfflinePrefetchKind.Chapter;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    public static bool TryParseOrigin(string? value, out OfflinePrefetchOrigin origin)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "explicit":
                origin = OfflinePrefetchOrigin.Explicit;
                return true;
            case "prefetched":
                origin = OfflinePrefetchOrigin.Prefetched;
                return true;
            default:
                origin = default;
                return false;
        }
    }

    public static bool TryParseConnection(string? value, out OfflinePrefetchConnection connection)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "unmetered":
                connection = OfflinePrefetchConnection.Unmetered;
                return true;
            case "metered":
                connection = OfflinePrefetchConnection.Metered;
                return true;
            default:
                connection = default;
                return false;
        }
    }

    public static string ReasonName(OfflinePrefetchReason reason) =>
        reason switch
        {
            OfflinePrefetchReason.Planned => "planned",
            OfflinePrefetchReason.Disabled => "disabled",
            OfflinePrefetchReason.MeteredConnection => "metered_connection",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };

    public static string DownloadUrl(OfflinePrefetchCandidate candidate) =>
        candidate.Kind == OfflinePrefetchKind.Episode
            ? ClientApiOfflineRoutes.Download(candidate.ItemId)
            : ClientApiOfflineLibraryRoutes.Chapter(candidate.ItemId);
}

public sealed record ClientOfflinePrefetchPolicy(
    bool Enabled,
    long CapBytes,
    bool IncludeEpisodes,
    bool IncludeChapters,
    int EpisodesAhead,
    int ChaptersAhead,
    bool AllowMetered);

/// <summary>
/// The device as the client sees it. <c>connection</c> is <c>unmetered</c> or
/// <c>metered</c> (required: an unknown connection must be reported as metered).
/// <c>deviceLimitBytes</c> is the user's own offline limit on the device, if any.
/// </summary>
public sealed record ClientOfflinePrefetchPlanRequest(
    string? Connection,
    long? DeviceLimitBytes,
    IReadOnlyList<ClientOfflinePrefetchInventoryItem>? Inventory);

/// <summary>
/// One item stored offline. <c>kind</c>: <c>episode</c> (episode id) or
/// <c>chapter</c> (chapter id); <c>origin</c>: <c>explicit</c> or
/// <c>prefetched</c>; <c>active</c> marks a download that is still running.
/// </summary>
public sealed record ClientOfflinePrefetchInventoryItem(
    string? Kind,
    Guid ItemId,
    long SizeBytes,
    string? Origin,
    DateTime LastUsedUtc,
    bool Active);

public sealed record ClientOfflinePrefetchPlan(
    int ApiVersion,
    string Reason,
    ClientOfflinePrefetchPolicy Policy,
    long BudgetBytes,
    long PrefetchedBytesAfter,
    IReadOnlyList<ClientOfflinePrefetchDownload> Downloads,
    IReadOnlyList<ClientOfflinePrefetchEviction> Evictions);

/// <summary>
/// An item to download, in priority order. <c>containerId</c> is the anime id
/// (episodes) or the work id (chapters); <c>url</c> is the existing offline
/// endpoint that returns the download descriptor / chapter payload.
/// </summary>
public sealed record ClientOfflinePrefetchDownload(
    string Kind,
    Guid ItemId,
    Guid ContainerId,
    string Title,
    long SizeBytes,
    string Url);

/// <summary>A prefetched item to delete from the device, least recently used first.</summary>
public sealed record ClientOfflinePrefetchEviction(
    string Kind,
    Guid ItemId,
    long SizeBytes);

public static class ClientApiOfflinePrefetchEndpoints
{
    public static IEndpointRouteBuilder MapClientApiOfflinePrefetchV1(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup(ClientApiContract.BasePath)
            .RequireAuthorization();

        group.MapGet("/offline/prefetch/policy", async (
            OfflinePrefetchService service,
            CancellationToken cancellationToken) =>
            Results.Ok(ToClient(await service.GetPolicyAsync(cancellationToken))));

        group.MapPost("/offline/prefetch/plan", async (
            ClientOfflinePrefetchPlanRequest request,
            OfflinePrefetchService service,
            CancellationToken cancellationToken) =>
        {
            if (!ClientApiOfflinePrefetchContract.TryParseConnection(request.Connection, out var connection))
            {
                return BadRequest(
                    "invalid_connection",
                    "The connection must be \"unmetered\" or \"metered\".");
            }

            var items = request.Inventory ?? [];
            if (items.Count > ClientApiOfflinePrefetchContract.MaxInventoryItems)
            {
                return BadRequest(
                    "too_many_items",
                    $"At most {ClientApiOfflinePrefetchContract.MaxInventoryItems} inventory items can be reported per request.");
            }

            if (request.DeviceLimitBytes is < 0)
            {
                return BadRequest("invalid_limit", "The device limit must be zero or greater.");
            }

            var inventory = new List<OfflinePrefetchInventoryItem>(items.Count);
            foreach (var item in items)
            {
                if (!ClientApiOfflinePrefetchContract.TryParseKind(item.Kind, out var kind) ||
                    !ClientApiOfflinePrefetchContract.TryParseOrigin(item.Origin, out var origin) ||
                    item.SizeBytes < 0)
                {
                    return BadRequest(
                        "invalid_inventory",
                        "Every inventory item needs a kind (episode or chapter), an origin (explicit or prefetched) and a size of zero or greater.");
                }

                inventory.Add(new OfflinePrefetchInventoryItem(
                    kind,
                    item.ItemId,
                    item.SizeBytes,
                    origin,
                    DateTime.SpecifyKind(item.LastUsedUtc, DateTimeKind.Utc),
                    item.Active));
            }

            var (policy, plan) = await service.PlanAsync(
                new OfflinePrefetchDeviceState(connection, request.DeviceLimitBytes, inventory),
                cancellationToken);

            return Results.Ok(new ClientOfflinePrefetchPlan(
                ClientApiContract.ApiVersion,
                ClientApiOfflinePrefetchContract.ReasonName(plan.Reason),
                ToClient(policy),
                plan.BudgetBytes,
                plan.PrefetchedBytesAfter,
                [
                    .. plan.Downloads.Select(x => new ClientOfflinePrefetchDownload(
                        ClientApiOfflinePrefetchContract.KindName(x.Kind),
                        x.ItemId,
                        x.ContainerId,
                        x.Title,
                        x.SizeBytes,
                        ClientApiOfflinePrefetchContract.DownloadUrl(x)))
                ],
                [
                    .. plan.Evictions.Select(x => new ClientOfflinePrefetchEviction(
                        ClientApiOfflinePrefetchContract.KindName(x.Kind),
                        x.ItemId,
                        x.SizeBytes))
                ]));
        });

        return endpoints;
    }

    private static ClientOfflinePrefetchPolicy ToClient(OfflinePrefetchPolicy policy) =>
        new(
            policy.Enabled,
            policy.CapBytes,
            policy.IncludeEpisodes,
            policy.IncludeChapters,
            policy.EpisodesAhead,
            policy.ChaptersAhead,
            policy.AllowMetered);

    private static IResult BadRequest(string code, string message) =>
        Results.BadRequest(new ClientErrorResponse(code, message));
}
