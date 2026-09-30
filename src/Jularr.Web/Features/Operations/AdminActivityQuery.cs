using System.Globalization;

namespace Jularr.Web.Features.Operations;

/// <summary>The state tabs of Admin → Activity; all of them are views of the same operations.</summary>
public enum AdminActivityTab
{
    /// <summary>Everything not finished well: running, queued, failed and interrupted work.</summary>
    Todo,

    Running,

    /// <summary>Failed and interrupted work, which can be run again.</summary>
    Failed,

    All
}

/// <summary>What Admin → Activity is narrowed to.</summary>
public sealed record AdminActivityFilter(
    AdminActivityTab Tab = AdminActivityTab.Todo,
    AdminHistoryCategory Category = AdminHistoryCategory.All,
    OperationStatus? Status = null,
    string? Search = null,
    int Page = 1)
{
    public bool HasNarrowing =>
        Category != AdminHistoryCategory.All || Status is not null || !string.IsNullOrWhiteSpace(Search);
}

/// <summary>Which operations to read for a page of the activity, and the numbers the tabs and pager need.</summary>
public sealed record AdminActivityPlan(
    AdminActivityFilter Filter,
    IReadOnlyDictionary<AdminActivityTab, int> TabCounts,
    IReadOnlyList<OperationStatus> Statuses,
    IReadOnlyList<OperationKindKey> Kinds,
    int Total,
    int PageCount)
{
    public int Page => Filter.Page;

    public int Offset => (Page - 1) * AdminActivityQuery.PageSize;

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < PageCount;
}

/// <summary>
/// Admin → Activity: the work queue of the server. The database narrows and pages the rows; this class
/// decides what each tab means, reads the address and turns the per-kind, per-status counts into the tab
/// numbers and the page to read.
/// </summary>
public static class AdminActivityQuery
{
    public const int PageSize = 20;

    /// <summary>The statuses an operation can be in to show under a tab.</summary>
    public static IReadOnlyList<OperationStatus> StatusesOf(AdminActivityTab tab) => tab switch
    {
        AdminActivityTab.Todo =>
            [OperationStatus.Running, OperationStatus.Queued, OperationStatus.Failed, OperationStatus.Interrupted],
        AdminActivityTab.Running => [OperationStatus.Running],
        AdminActivityTab.Failed => [OperationStatus.Failed, OperationStatus.Interrupted],
        _ =>
        [
            OperationStatus.Running,
            OperationStatus.Queued,
            OperationStatus.Failed,
            OperationStatus.Interrupted,
            OperationStatus.Succeeded,
            OperationStatus.Cancelled
        ]
    };

    public static string TabName(AdminActivityTab tab) => tab switch
    {
        AdminActivityTab.Running => "running",
        AdminActivityTab.Failed => "failed",
        AdminActivityTab.All => "all",
        _ => "todo"
    };

    /// <summary>Reads a tab from the address; anything unknown is the to-do tab.</summary>
    public static AdminActivityTab ParseTab(string? value)
    {
        foreach (var tab in Enum.GetValues<AdminActivityTab>())
        {
            if (string.Equals(TabName(tab), value?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return tab;
            }
        }

        return AdminActivityTab.Todo;
    }

    public static string StatusName(OperationStatus status) => status.ToString().ToLowerInvariant();

    /// <summary>Reads a status from the address; an unknown value means no filter.</summary>
    public static OperationStatus? TryParseStatus(string? value)
    {
        foreach (var status in Enum.GetValues<OperationStatus>())
        {
            if (string.Equals(StatusName(status), value?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return status;
            }
        }

        return null;
    }

    /// <summary>Puts the filter in a state that can be read: a status the tab does not show is dropped.</summary>
    public static AdminActivityFilter Normalize(AdminActivityFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return filter.Status is { } status && !StatusesOf(filter.Tab).Contains(status)
            ? filter with { Status = null }
            : filter;
    }

    /// <summary>
    /// Works out the tab numbers and the page to read from how many operations each kind has in each
    /// status (under the search text only). A page past the end shows the last one.
    /// </summary>
    public static AdminActivityPlan Plan(IReadOnlyList<OperationActivityCount> counts, AdminActivityFilter filter)
    {
        ArgumentNullException.ThrowIfNull(counts);
        filter = Normalize(filter);

        var inCategory = counts
            .Where(row => filter.Category == AdminHistoryCategory.All
                || AdminHistoryQuery.CategoryOf(row.Key.Kind, row.Key.Category) == filter.Category)
            .ToArray();

        var tabCounts = Enum.GetValues<AdminActivityTab>().ToDictionary(
            tab => tab,
            tab => inCategory.Where(row => StatusesOf(tab).Contains(row.Status)).Sum(row => row.Count));

        IReadOnlyList<OperationStatus> statuses = filter.Status is { } only ? [only] : StatusesOf(filter.Tab);
        var total = inCategory.Where(row => statuses.Contains(row.Status)).Sum(row => row.Count);
        var kinds = filter.Category == AdminHistoryCategory.All
            ? []
            : inCategory.Select(row => row.Key).Distinct().ToArray();

        var pageCount = Math.Max(1, (total + PageSize - 1) / PageSize);
        var page = Math.Clamp(filter.Page, 1, pageCount);
        return new AdminActivityPlan(filter with { Page = page }, tabCounts, statuses, kinds, total, pageCount);
    }

    /// <summary>The database filter for the page the plan chose.</summary>
    public static OperationActivityFilter DatabaseFilter(AdminActivityPlan plan) =>
        new(
            plan.Statuses,
            plan.Filter.Category == AdminHistoryCategory.All ? null : plan.Kinds,
            string.IsNullOrWhiteSpace(plan.Filter.Search) ? null : plan.Filter.Search.Trim(),
            plan.Offset,
            PageSize);

    /// <summary>What to say about where an operation is: the error of a failed one, otherwise its latest message.</summary>
    public static string? NoteOf(OperationSnapshot operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var note = operation.Status is OperationStatus.Failed or OperationStatus.Interrupted
            ? operation.Error ?? operation.Message
            : operation.Message;
        return string.IsNullOrWhiteSpace(note) ? null : note;
    }

    /// <summary>The address of the activity page with the given filter; default values stay out of it.</summary>
    public static string Href(string path, AdminActivityFilter filter)
    {
        var parts = new List<string>();
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add($"{name}={Uri.EscapeDataString(value)}");
            }
        }

        Add("tab", filter.Tab == AdminActivityTab.Todo ? null : TabName(filter.Tab));
        Add("type", filter.Category == AdminHistoryCategory.All ? null : AdminHistoryQuery.CategoryName(filter.Category));
        Add("status", filter.Status is { } status ? StatusName(status) : null);
        Add("q", filter.Search?.Trim());
        Add("p", filter.Page > 1 ? filter.Page.ToString(CultureInfo.InvariantCulture) : null);
        return parts.Count == 0 ? path : $"{path}?{string.Join('&', parts)}";
    }
}
