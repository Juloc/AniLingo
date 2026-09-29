using System.Globalization;
using Jularr.Web.Data;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.MediaCore;
using Jularr.Web.Features.Search;
using Jularr.Web.Features.Shell;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Search;

/// <summary>
/// The local half of the unified search (#434): one query over everything on this server with the
/// Media Facts filters, ranked and paged by <see cref="MediaSearchService"/>. The profile's visible
/// media types come from the app shell, so a hidden type is neither searched nor offered as a filter.
/// Online providers are searched in Discover, which this page links to with the same query.
/// </summary>
public sealed class IndexModel(AppDbContext db, IAppShellService appShell, MediaSearchService search) : PageModel
{
    public const int PageSize = 20;

    // Filter values of the URL. They are the only state; nothing is stored per profile.
    public const string AvailableOption = "available";
    public const string LibraryOption = "library";
    public const string YesOption = "yes";
    public const string NoOption = "no";

    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public string Query { get; private set; } = "";

    /// <summary>The media types the profile may browse, in filter order.</summary>
    public IReadOnlyList<MediaSearchType> VisibleTypes { get; private set; } = [];

    /// <summary>The types the URL asked for; empty means every visible type.</summary>
    public IReadOnlySet<MediaSearchType> SelectedTypes { get; private set; } = new HashSet<MediaSearchType>();

    public string? Availability { get; private set; }
    public string? Monitoring { get; private set; }
    public string? Wanted { get; private set; }
    public string? Language { get; private set; }
    public string? Genre { get; private set; }
    public int? YearFrom { get; private set; }
    public int? YearTo { get; private set; }

    public int CurrentPage { get; private set; } = 1;

    public MediaSearchPage Results { get; private set; } = MediaSearchPage.Empty(PageSize);

    /// <summary>True when the URL carries a filter beyond the media types, so the filter group starts open.</summary>
    public bool HasFilters => Filters.HasFactFilters;

    private MediaSearchFilters Filters { get; set; } = MediaSearchFilters.None;

    public async Task OnGetAsync(
        string? q,
        string[]? type,
        string? availability,
        string? monitoring,
        string? wanted,
        string? language,
        string? genre,
        int? yearFrom,
        int? yearTo,
        int? p,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var access = await appShell.GetMediaAccessAsync(User, cancellationToken);
        VisibleTypes = [.. MediaSearchTypes.All.Where(candidate => access.IsVisible(MediaSearchTypes.ToWorkMediaType(candidate)))];

        Query = (q ?? string.Empty).Trim();
        SelectedTypes = (type ?? [])
            .Select(MediaSearchTypes.Parse)
            .OfType<MediaSearchType>()
            .Where(VisibleTypes.Contains)
            .ToHashSet();
        Availability = availability is AvailableOption or LibraryOption ? availability : null;
        Monitoring = monitoring is YesOption or NoOption ? monitoring : null;
        Wanted = wanted is YesOption or NoOption ? wanted : null;
        Language = string.IsNullOrWhiteSpace(language) ? null : language.Trim();
        Genre = string.IsNullOrWhiteSpace(genre) ? null : genre.Trim();
        YearFrom = yearFrom is > 0 and < 10_000 ? yearFrom : null;
        YearTo = yearTo is > 0 and < 10_000 ? yearTo : null;
        CurrentPage = Math.Max(1, p ?? 1);

        Filters = new MediaSearchFilters(
            SelectedTypes.Count == 0 ? null : SelectedTypes,
            Availability switch { AvailableOption => true, LibraryOption => false, _ => null },
            Monitoring switch { YesOption => true, NoOption => false, _ => null },
            Wanted switch { YesOption => true, NoOption => false, _ => null },
            Language,
            Genre,
            YearFrom,
            YearTo);

        if (Query.Length == 0)
        {
            return;
        }

        Results = await search.SearchAsync(
            new MediaSearchRequest(
                Query,
                Filters,
                access.VisibleMediaTypes,
                PageSize,
                (CurrentPage - 1) * PageSize),
            cancellationToken);
    }

    public string TypeLabel(MediaSearchType type) => Ui[$"search.type.{MediaSearchTypes.ToStorage(type)}"];

    /// <summary>The name of a language in that language ("Deutsch"), or its upper-case tag when the runtime does not know it.</summary>
    public static string LanguageLabel(string code)
    {
        try
        {
            var name = CultureInfo.GetCultureInfo(code).NativeName;
            return string.IsNullOrWhiteSpace(name) || string.Equals(name, code, StringComparison.OrdinalIgnoreCase)
                ? code.ToUpperInvariant()
                : char.ToUpper(name[0], CultureInfo.CurrentCulture) + name[1..];
        }
        catch (CultureNotFoundException)
        {
            return code.ToUpperInvariant();
        }
    }

    /// <summary>The query string of this search with the given page (and no page for the first), for the pager links.</summary>
    public string PageUrl(int page)
    {
        var parts = new List<string> { $"q={Uri.EscapeDataString(Query)}" };
        parts.AddRange(SelectedTypes.Select(t => $"type={MediaSearchTypes.ToStorage(t)}"));
        Add(parts, "availability", Availability);
        Add(parts, "monitoring", Monitoring);
        Add(parts, "wanted", Wanted);
        Add(parts, "language", Language);
        Add(parts, "genre", Genre);
        Add(parts, "yearFrom", YearFrom?.ToString(CultureInfo.InvariantCulture));
        Add(parts, "yearTo", YearTo?.ToString(CultureInfo.InvariantCulture));
        if (page > 1)
        {
            parts.Add($"p={page.ToString(CultureInfo.InvariantCulture)}");
        }

        return "/Search?" + string.Join('&', parts);
    }

    /// <summary>This search without its filters (the media type choice stays), for "Clear filters".</summary>
    public string ClearFiltersUrl =>
        "/Search?" + string.Join(
            '&',
            SelectedTypes
                .Select(t => $"type={MediaSearchTypes.ToStorage(t)}")
                .Prepend($"q={Uri.EscapeDataString(Query)}"));

    private static void Add(List<string> parts, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            parts.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }
}
