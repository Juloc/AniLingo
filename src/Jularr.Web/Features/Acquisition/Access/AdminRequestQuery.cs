namespace Jularr.Web.Features.Acquisition.Access;

/// <summary>The status tabs of Admin → Requests.</summary>
public enum AdminRequestTab
{
    All,

    /// <summary>Waiting for the owner's decision, or failed and waiting for a retry.</summary>
    Open,

    /// <summary>Approved; acquisition has not started yet.</summary>
    Approved,

    /// <summary>Searching, downloading or importing.</summary>
    InProgress,

    Done,
    Rejected
}

/// <summary>What the Admin → Requests list is narrowed to. Every member but <see cref="Tab"/> also narrows the tab counts.</summary>
public sealed record AdminRequestFilter(
    AdminRequestTab Tab = AdminRequestTab.All,
    MediaAcquisitionKind? Kind = null,
    AcquisitionRequestStatus? Status = null,
    string? Language = null,
    string? RequesterProfileId = null,
    string? Search = null,
    int Page = 1)
{
    public bool HasNarrowing =>
        Kind is not null || Status is not null || Language is not null || RequesterProfileId is not null
        || !string.IsNullOrWhiteSpace(Search);
}

/// <summary>One page of the owner's request queue plus what the filter bar and the tabs need.</summary>
public sealed record AdminRequestPage(
    IReadOnlyList<AcquisitionRequest> Items,
    AdminRequestFilter Filter,
    IReadOnlyDictionary<AdminRequestTab, int> TabCounts,
    IReadOnlyList<string> Languages,
    int Total,
    int PageCount)
{
    public int Page => Filter.Page;

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < PageCount;
}

/// <summary>
/// The owner's request queue (Admin → Requests): tabs by lifecycle, filters for media type, language, status
/// and requester, a search over title and requester, and paging. The queue of one server is small, so the
/// rows are read once and narrowed here; that keeps tab counts and filters consistent with each other.
/// </summary>
public static class AdminRequestQuery
{
    public const int PageSize = 20;

    public static AdminRequestTab TabOf(AcquisitionRequestStatus status) => status switch
    {
        AcquisitionRequestStatus.Pending or AcquisitionRequestStatus.Failed => AdminRequestTab.Open,
        AcquisitionRequestStatus.Approved => AdminRequestTab.Approved,
        AcquisitionRequestStatus.Searching or AcquisitionRequestStatus.Downloading or AcquisitionRequestStatus.Importing
            => AdminRequestTab.InProgress,
        AcquisitionRequestStatus.Completed => AdminRequestTab.Done,
        AcquisitionRequestStatus.Rejected => AdminRequestTab.Rejected,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    /// <summary>The audio language a request asks for; only anime requests carry one.</summary>
    public static string? LanguageOf(AcquisitionRequest request) => request.Options.AudioLanguage;

    public static string TabName(AdminRequestTab tab) => tab switch
    {
        AdminRequestTab.Open => "open",
        AdminRequestTab.Approved => "approved",
        AdminRequestTab.InProgress => "progress",
        AdminRequestTab.Done => "done",
        AdminRequestTab.Rejected => "rejected",
        _ => "all"
    };

    public static AdminRequestTab ParseTab(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "open" => AdminRequestTab.Open,
        "approved" => AdminRequestTab.Approved,
        "progress" => AdminRequestTab.InProgress,
        "done" => AdminRequestTab.Done,
        "rejected" => AdminRequestTab.Rejected,
        _ => AdminRequestTab.All
    };

    /// <summary>Reads a media type from the address; an unknown value means no filter.</summary>
    public static MediaAcquisitionKind? TryParseKind(string? value)
    {
        foreach (var kind in Enum.GetValues<MediaAcquisitionKind>())
        {
            if (string.Equals(AcquisitionAccessNames.Kind(kind), value, StringComparison.Ordinal))
            {
                return kind;
            }
        }

        return null;
    }

    /// <summary>Reads a request status from the address; an unknown value means no filter.</summary>
    public static AcquisitionRequestStatus? TryParseStatus(string? value)
    {
        foreach (var status in Enum.GetValues<AcquisitionRequestStatus>())
        {
            if (string.Equals(AcquisitionAccessNames.Status(status), value, StringComparison.Ordinal))
            {
                return status;
            }
        }

        return null;
    }

    /// <param name="rows">Every request, in the order the list shows them.</param>
    /// <param name="requesterNames">User names by profile id; the search also matches the requester's name.</param>
    public static AdminRequestPage Build(
        IReadOnlyList<AcquisitionRequest> rows,
        AdminRequestFilter filter,
        IReadOnlyDictionary<string, string> requesterNames)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(requesterNames);

        var search = filter.Search?.Trim();
        var language = string.IsNullOrWhiteSpace(filter.Language) ? null : filter.Language;
        var requester = string.IsNullOrWhiteSpace(filter.RequesterProfileId) ? null : filter.RequesterProfileId;

        // Everything but the tab and the status narrows the counts too, so a tab's number is what it would list.
        var narrowed = rows
            .Where(row => filter.Kind is not { } kind || row.Kind == kind)
            .Where(row => language is null || string.Equals(LanguageOf(row), language, StringComparison.Ordinal))
            .Where(row => requester is null || row.RequestedByProfileId == requester)
            .Where(row => string.IsNullOrEmpty(search) || Matches(row, search, requesterNames))
            .ToArray();

        var counts = new Dictionary<AdminRequestTab, int>
        {
            [AdminRequestTab.All] = narrowed.Length
        };
        foreach (var tab in Enum.GetValues<AdminRequestTab>().Where(tab => tab != AdminRequestTab.All))
        {
            counts[tab] = narrowed.Count(row => TabOf(row.Status) == tab);
        }

        var matching = narrowed
            .Where(row => filter.Tab == AdminRequestTab.All || TabOf(row.Status) == filter.Tab)
            .Where(row => filter.Status is not { } status || row.Status == status)
            .ToArray();

        var languages = rows
            .Select(LanguageOf)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(tag => LanguageRank(tag))
            .ThenBy(tag => tag, StringComparer.Ordinal)
            .ToArray();

        // A page past the end (a filter with fewer rows, requests that changed state) shows the last one.
        var pageCount = Math.Max(1, (matching.Length + PageSize - 1) / PageSize);
        var page = Math.Clamp(filter.Page, 1, pageCount);
        var items = matching.Skip((page - 1) * PageSize).Take(PageSize).ToArray();
        return new AdminRequestPage(items, filter with { Page = page }, counts, languages, matching.Length, pageCount);
    }

    private static bool Matches(AcquisitionRequest row, string search, IReadOnlyDictionary<string, string> requesterNames) =>
        row.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || (row.Subtitle?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false)
        || (requesterNames.TryGetValue(row.RequestedByProfileId, out var name)
            && name.Contains(search, StringComparison.CurrentCultureIgnoreCase));

    private static int LanguageRank(string tag)
    {
        for (var index = 0; index < RequestLanguages.Choices.Count; index++)
        {
            if (RequestLanguages.Choices[index].Tag == tag)
            {
                return index;
            }
        }

        return RequestLanguages.Choices.Count;
    }
}
