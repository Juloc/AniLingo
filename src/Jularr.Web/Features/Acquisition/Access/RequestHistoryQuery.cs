namespace Jularr.Web.Features.Acquisition.Access;

/// <summary>Which of a profile's requests the history shows.</summary>
public enum RequestHistoryFilter
{
    All,

    /// <summary>Waiting for a decision, or approved and not yet in the library.</summary>
    Open,

    /// <summary>Completed, rejected, withdrawn or failed.</summary>
    Finished
}

/// <summary>One page of a profile's request history with the counts the filter tabs show.</summary>
public sealed record RequestHistoryPage(
    IReadOnlyList<AcquisitionRequest> Items,
    RequestHistoryFilter Filter,
    int Page,
    int OpenCount,
    int FinishedCount)
{
    public int Total => Filter switch
    {
        RequestHistoryFilter.Open => OpenCount,
        RequestHistoryFilter.Finished => FinishedCount,
        _ => OpenCount + FinishedCount
    };

    public int PageCount => Math.Max(1, (Total + RequestHistoryQuery.PageSize - 1) / RequestHistoryQuery.PageSize);

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < PageCount;
}

/// <summary>
/// The per-user request history (#597): the requests one profile submitted, most recently changed
/// first, with their state. Every query is scoped to the profile it is asked for; the page passes
/// the signed-in profile, including for the owner.
/// </summary>
public sealed class RequestHistoryQuery(AcquisitionAccessStore store)
{
    public const int PageSize = 25;

    public async Task<RequestHistoryPage> GetAsync(
        string profileId,
        RequestHistoryFilter filter,
        int page,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        var (open, finished) = await store.CountForProfileAsync(profileId, cancellationToken);
        var total = filter switch
        {
            RequestHistoryFilter.Open => open,
            RequestHistoryFilter.Finished => finished,
            _ => open + finished
        };

        // A page past the end (a filter with fewer requests, or requests that were withdrawn) shows the last one.
        var lastPage = Math.Max(1, (total + PageSize - 1) / PageSize);
        var current = Math.Clamp(page, 1, lastPage);
        var items = await store.ListForProfileAsync(profileId, filter, (current - 1) * PageSize, PageSize, cancellationToken);
        return new RequestHistoryPage(items, filter, current, open, finished);
    }

    public static RequestHistoryFilter ParseFilter(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "open" => RequestHistoryFilter.Open,
        "finished" => RequestHistoryFilter.Finished,
        _ => RequestHistoryFilter.All
    };

    public static string FilterName(RequestHistoryFilter filter) => filter switch
    {
        RequestHistoryFilter.Open => "open",
        RequestHistoryFilter.Finished => "finished",
        _ => "all"
    };
}
