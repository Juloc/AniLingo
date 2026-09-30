using System.Globalization;

namespace Jularr.Web.Features.Operations;

/// <summary>The category pills of Admin → History.</summary>
public enum AdminHistoryCategory
{
    All,
    Acquisition,
    Imports,
    Remux,

    /// <summary>Swapping a media file for a rebuilt one, and finishing or undoing such a swap that was interrupted.</summary>
    Repack,
    Subtitle,
    Translation,
    Metadata,
    Ai,
    Maintenance
}

/// <summary>How a finished operation ended, as the history shows it.</summary>
public enum AdminHistoryResult
{
    Success,

    /// <summary>Stopped before it finished (for example because the server shut down); it can be run again.</summary>
    Warning,

    Failed,
    Cancelled
}

/// <summary>What Admin → History is narrowed to. <see cref="From"/> and <see cref="To"/> are whole UTC days, both included.</summary>
public sealed record AdminHistoryFilter(
    AdminHistoryCategory Category = AdminHistoryCategory.All,
    AdminHistoryResult? Result = null,
    DateOnly? From = null,
    DateOnly? To = null,
    string? Search = null,
    bool OldestFirst = false,
    int Page = 1)
{
    public bool HasNarrowing =>
        Result is not null || From is not null || To is not null || !string.IsNullOrWhiteSpace(Search);
}

/// <summary>Which operations to read for a page of the history, and the numbers the pills and pager need.</summary>
public sealed record AdminHistoryPlan(
    AdminHistoryFilter Filter,
    IReadOnlyDictionary<AdminHistoryCategory, int> CategoryCounts,
    IReadOnlyList<OperationKindKey> Kinds,
    int Total,
    int PageCount)
{
    public int Page => Filter.Page;

    public int Offset => (Page - 1) * AdminHistoryQuery.PageSize;

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < PageCount;
}

/// <summary>
/// Admin → History: the finished operations of the server, by category, result, day and text. The
/// database narrows and pages the rows; this class decides what the categories and results mean, reads
/// the address and turns the per-kind counts into the pills and the page to read.
/// </summary>
public static class AdminHistoryQuery
{
    public const int PageSize = 20;

    /// <summary>The category an operation belongs to, from what it was created as.</summary>
    public static AdminHistoryCategory CategoryOf(string kind, string category)
    {
        var k = kind.ToLowerInvariant();
        var c = category.ToLowerInvariant();

        if (c == "translation" || k.Contains("translation", StringComparison.Ordinal))
        {
            return AdminHistoryCategory.Translation;
        }

        if (c == "ai")
        {
            return AdminHistoryCategory.Ai;
        }

        if (c is "subtitles" or "learning"
            || k.Contains("subtitle", StringComparison.Ordinal)
            || k.StartsWith("learning-", StringComparison.Ordinal))
        {
            return AdminHistoryCategory.Subtitle;
        }

        // Media optimization (Features/Media/Optimization): the run that rewrites a file into a
        // direct-play container is a remux; the run that finishes or undoes an interrupted file swap
        // after a crash is the replace/repack side of it. Both keep the "media-optimization" prefix.
        if (k == "media-optimization-recovery")
        {
            return AdminHistoryCategory.Repack;
        }

        if (k == "media-optimization")
        {
            return AdminHistoryCategory.Remux;
        }

        if (c is "acquisition" or "external downloads"
            || k.Contains("-download", StringComparison.Ordinal)
            || k.StartsWith("anime-search", StringComparison.Ordinal)
            || k == "anime-grab")
        {
            return AdminHistoryCategory.Acquisition;
        }

        if (c is "anilist" or "artwork"
            || k.Contains("metadata", StringComparison.Ordinal)
            || k.Contains("-match", StringComparison.Ordinal)
            || k.Contains("refresh", StringComparison.Ordinal)
            || k.Contains("reanalyze", StringComparison.Ordinal))
        {
            return AdminHistoryCategory.Metadata;
        }

        if (k.Contains("import", StringComparison.Ordinal))
        {
            return AdminHistoryCategory.Imports;
        }

        return AdminHistoryCategory.Maintenance;
    }

    public static AdminHistoryResult ResultOf(OperationStatus status) => status switch
    {
        OperationStatus.Succeeded => AdminHistoryResult.Success,
        OperationStatus.Failed => AdminHistoryResult.Failed,
        OperationStatus.Cancelled => AdminHistoryResult.Cancelled,
        _ => AdminHistoryResult.Warning
    };

    /// <summary>The operation statuses that end in a result.</summary>
    public static IReadOnlyList<OperationStatus> StatusesOf(AdminHistoryResult result) => result switch
    {
        AdminHistoryResult.Success => [OperationStatus.Succeeded],
        AdminHistoryResult.Failed => [OperationStatus.Failed],
        AdminHistoryResult.Cancelled => [OperationStatus.Cancelled],
        _ => [OperationStatus.Interrupted]
    };

    public static string CategoryName(AdminHistoryCategory category) => category switch
    {
        AdminHistoryCategory.Acquisition => "acquisition",
        AdminHistoryCategory.Imports => "imports",
        AdminHistoryCategory.Remux => "remux",
        AdminHistoryCategory.Repack => "repack",
        AdminHistoryCategory.Subtitle => "subtitle",
        AdminHistoryCategory.Translation => "translation",
        AdminHistoryCategory.Metadata => "metadata",
        AdminHistoryCategory.Ai => "ai",
        AdminHistoryCategory.Maintenance => "maintenance",
        _ => "all"
    };

    public static AdminHistoryCategory ParseCategory(string? value)
    {
        foreach (var category in Enum.GetValues<AdminHistoryCategory>())
        {
            if (string.Equals(CategoryName(category), value?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return category;
            }
        }

        return AdminHistoryCategory.All;
    }

    public static string ResultName(AdminHistoryResult result) => result switch
    {
        AdminHistoryResult.Success => "success",
        AdminHistoryResult.Warning => "warning",
        AdminHistoryResult.Failed => "failed",
        _ => "cancelled"
    };

    /// <summary>Reads a result from the address; an unknown value means no filter.</summary>
    public static AdminHistoryResult? TryParseResult(string? value)
    {
        foreach (var result in Enum.GetValues<AdminHistoryResult>())
        {
            if (string.Equals(ResultName(result), value?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>Reads a day (<c>yyyy-MM-dd</c>) from the address; anything else means no limit.</summary>
    public static DateOnly? TryParseDay(string? value) =>
        DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;

    public static string FormatDay(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Puts a range the wrong way round the right way round.</summary>
    public static AdminHistoryFilter Normalize(AdminHistoryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return filter.From is { } from && filter.To is { } to && from > to
            ? filter with { From = to, To = from }
            : filter;
    }

    /// <summary>The first instant of the range (start of the first day, UTC).</summary>
    public static DateTime? FromUtc(DateOnly? from) =>
        from?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    /// <summary>The first instant after the range (start of the day after the last one, UTC).</summary>
    public static DateTime? ToUtcExclusive(DateOnly? to) =>
        to?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    /// <summary>The filter for the database, without the category (the plan adds its kinds).</summary>
    public static OperationHistoryFilter DatabaseFilter(
        AdminHistoryFilter filter,
        IReadOnlyCollection<string>? searchActorIds = null) =>
        new(
            FromUtc(filter.From),
            ToUtcExclusive(filter.To),
            Statuses: filter.Result is { } result ? StatusesOf(result).ToArray() : null,
            Search: string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim(),
            SearchActorIds: searchActorIds,
            NewestFirst: !filter.OldestFirst);

    /// <summary>
    /// Works out the pills and the page to read from how many finished operations each kind has under the
    /// filter (ignoring the category). A page past the end shows the last one.
    /// </summary>
    public static AdminHistoryPlan Plan(IReadOnlyList<OperationKindCount> counts, AdminHistoryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(counts);
        filter = Normalize(filter);

        var categoryCounts = Enum.GetValues<AdminHistoryCategory>().ToDictionary(category => category, _ => 0);
        foreach (var row in counts)
        {
            var category = CategoryOf(row.Key.Kind, row.Key.Category);
            categoryCounts[category] += row.Count;
            categoryCounts[AdminHistoryCategory.All] += row.Count;
        }

        var kinds = filter.Category == AdminHistoryCategory.All
            ? []
            : counts
                .Where(row => CategoryOf(row.Key.Kind, row.Key.Category) == filter.Category)
                .Select(row => row.Key)
                .ToArray();

        var total = categoryCounts[filter.Category];
        var pageCount = Math.Max(1, (total + PageSize - 1) / PageSize);
        var page = Math.Clamp(filter.Page, 1, pageCount);
        return new AdminHistoryPlan(filter with { Page = page }, categoryCounts, kinds, total, pageCount);
    }

    /// <summary>
    /// The page numbers to offer around the current one: the first, the last and two either side, with 0
    /// standing for a gap.
    /// </summary>
    public static IReadOnlyList<int> PageWindow(int page, int pageCount)
    {
        var pages = new List<int>();
        var last = 0;
        for (var number = 1; number <= pageCount; number++)
        {
            if (number != 1 && number != pageCount && Math.Abs(number - page) > 2)
            {
                continue;
            }

            if (number - last == 2)
            {
                pages.Add(last + 1);
            }
            else if (number - last > 2)
            {
                pages.Add(0);
            }

            pages.Add(number);
            last = number;
        }

        return pages;
    }
}
