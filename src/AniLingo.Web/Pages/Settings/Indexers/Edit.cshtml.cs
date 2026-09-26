using AniLingo.Web.Data;
using AniLingo.Web.Features.Acquisition.Indexers;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings.Indexers;

/// <summary>Add or edit one canonical indexer entry.</summary>
[Authorize(Roles = AccountRoles.Owner)]
public sealed class EditModel(AppDbContext db, IndexerStore store) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    [BindProperty(SupportsGet = true)]
    public Guid? Id { get; set; }

    [BindProperty]
    public string Name { get; set; } = "";

    [BindProperty]
    public IndexerType Type { get; set; } = IndexerType.Newznab;

    [BindProperty]
    public string BaseUrl { get; set; } = "";

    [BindProperty]
    public string? ApiKey { get; set; }

    [BindProperty]
    public string? Categories { get; set; }

    [BindProperty]
    public string? IndexerIds { get; set; }

    [BindProperty]
    public int SearchLimit { get; set; } = 100;

    [BindProperty]
    public int Priority { get; set; } = 1;

    [BindProperty]
    public bool Enabled { get; set; } = true;

    public bool IsNew => Id is null;
    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        if (Id is not { } id)
        {
            return;
        }

        var entry = await store.GetAsync(id, cancellationToken);
        if (entry is null)
        {
            Error = Ui["settings.indexers.notFound"];
            return;
        }

        Name = entry.Name;
        Type = entry.Type;
        BaseUrl = entry.Settings.BaseUrl;
        Categories = string.Join(", ", entry.Settings.Categories);
        IndexerIds = string.Join(", ", entry.Settings.IndexerIds);
        SearchLimit = entry.Settings.SearchLimit;
        Priority = entry.Priority;
        Enabled = entry.Enabled;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        try
        {
            if (!TryParseIds(Categories, out var categories) || !TryParseIds(IndexerIds, out var indexerIds))
            {
                Error = Ui["settings.indexers.invalidIds"];
                return Page();
            }

            var existing = Id is { } id ? await store.GetAsync(id, cancellationToken) : null;
            var apiKey = string.IsNullOrWhiteSpace(ApiKey) ? existing?.ApiKey : ApiKey.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Error = Ui["settings.indexers.enterApiKey"];
                return Page();
            }

            await store.SaveAsync(
                new IndexerEntry(
                    existing?.Id ?? Guid.NewGuid(),
                    Name,
                    Type,
                    Enabled,
                    Priority,
                    new IndexerSettings(BaseUrl, categories, indexerIds, SearchLimit),
                    apiKey),
                cancellationToken);

            TempData["IndexerNotice"] = Ui["settings.indexers.saved"];
            return RedirectToPage("Index");
        }
        catch (Exception exception) when (
            exception is ArgumentException or ArgumentOutOfRangeException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Error = exception.Message;
            return Page();
        }
    }

    private static bool TryParseIds(string? value, out int[] ids)
    {
        ids = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var parsed = new List<int>();
        foreach (var part in value.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, out var parsedId) || parsedId <= 0)
            {
                return false;
            }

            parsed.Add(parsedId);
        }

        ids = [.. parsed];
        return true;
    }
}
