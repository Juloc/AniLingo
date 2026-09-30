using System.Data.Common;
using System.Globalization;
using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Pages.Admin;

/// <summary>
/// Admin → History: the record of finished operations, by category, result, day and text, with who
/// started each one. Live work stays on Operations; this page only reads what has ended.
/// </summary>
[Authorize(Policy = JularrPolicies.AdminMedia)]
public sealed class HistoryModel(AppDbContext db, ILogger<HistoryModel> logger) : PageModel
{
    public const string PagePath = "/Admin/History";

    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public AdminHistoryPlan Plan { get; private set; } =
        AdminHistoryQuery.Plan([], new AdminHistoryFilter());

    public IReadOnlyList<OperationSnapshot> Items { get; private set; } = [];

    /// <summary>User names by account id, for the "performed by" column.</summary>
    public IReadOnlyDictionary<string, string> ActorNames { get; private set; } = new Dictionary<string, string>();

    /// <summary>Whether the history could not be read.</summary>
    public bool Failed { get; private set; }

    /// <summary>Who started an operation: the account's name, or null when the server did.</summary>
    public string? ActorOf(OperationSnapshot operation) =>
        string.IsNullOrEmpty(operation.ActorProfileId)
            ? null
            : ActorNames.GetValueOrDefault(operation.ActorProfileId) ?? Ui["admin.history.actor.unknown"];

    /// <summary>The address of the history with the given filter; default values stay out of it.</summary>
    public static string Href(AdminHistoryFilter filter)
    {
        var parts = new List<string>();
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add($"{name}={Uri.EscapeDataString(value)}");
            }
        }

        Add("cat", filter.Category == AdminHistoryCategory.All ? null : AdminHistoryQuery.CategoryName(filter.Category));
        Add("result", filter.Result is { } result ? AdminHistoryQuery.ResultName(result) : null);
        Add("from", filter.From is { } from ? AdminHistoryQuery.FormatDay(from) : null);
        Add("to", filter.To is { } to ? AdminHistoryQuery.FormatDay(to) : null);
        Add("q", filter.Search?.Trim());
        Add("sort", filter.OldestFirst ? "oldest" : null);
        Add("p", filter.Page > 1 ? filter.Page.ToString(CultureInfo.InvariantCulture) : null);
        return parts.Count == 0 ? PagePath : $"{PagePath}?{string.Join('&', parts)}";
    }

    public async Task OnGetAsync(
        string? cat,
        string? result,
        string? from,
        string? to,
        string? q,
        string? sort,
        int p,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var filter = AdminHistoryQuery.Normalize(new AdminHistoryFilter(
            AdminHistoryQuery.ParseCategory(cat),
            AdminHistoryQuery.TryParseResult(result),
            AdminHistoryQuery.TryParseDay(from),
            AdminHistoryQuery.TryParseDay(to),
            string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
            string.Equals(sort, "oldest", StringComparison.OrdinalIgnoreCase),
            Math.Max(p, 1)));
        Plan = AdminHistoryQuery.Plan([], filter);

        try
        {
            ActorNames = await db.OwnerAccounts
                .AsNoTracking()
                .ToDictionaryAsync(account => account.Id, account => account.UserName, cancellationToken);

            // The text search also matches the names of the people who started the work.
            var actorIds = string.IsNullOrWhiteSpace(filter.Search)
                ? []
                : ActorNames
                    .Where(actor => actor.Value.Contains(filter.Search, StringComparison.CurrentCultureIgnoreCase))
                    .Select(actor => actor.Key)
                    .ToArray();

            var store = new OperationStore(db);
            var query = AdminHistoryQuery.DatabaseFilter(filter, actorIds);
            Plan = AdminHistoryQuery.Plan(await store.CountHistoryByKindAsync(query, cancellationToken), filter);
            if (Plan.Total > 0)
            {
                var page = await store.QueryHistoryAsync(
                    query with
                    {
                        Kinds = Plan.Kinds.Count == 0 ? null : Plan.Kinds,
                        Offset = Plan.Offset,
                        Limit = AdminHistoryQuery.PageSize
                    },
                    cancellationToken);
                Items = page.Items;
            }
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or FormatException)
        {
            logger.LogError(exception, "The operation history could not be read.");
            Failed = true;
        }
    }
}
