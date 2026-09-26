using AniLingo.Web.Features.Acquisition.Indexers;
using AniLingo.Web.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings.Indexers;

/// <summary>Add or edit one canonical indexer entry.</summary>
[Authorize(Roles = AccountRoles.Owner)]
public sealed class EditModel(IndexerStore store) : PageModel
{
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
        if (Id is not { } id)
        {
            return;
        }

        var entry = await store.GetAsync(id, cancellationToken);
        if (entry is null)
        {
            Error = "Indexer not found.";
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
        try
        {
            if (!TryParseIds(Categories, out var categories) || !TryParseIds(IndexerIds, out var indexerIds))
            {
                throw new ArgumentException("Categories and indexer IDs must be positive numbers separated by commas.");
            }

            var existing = Id is { } id ? await store.GetAsync(id, cancellationToken) : null;
            var apiKey = string.IsNullOrWhiteSpace(ApiKey) ? existing?.ApiKey : ApiKey.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("Enter the indexer's API key.");
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

            TempData["IndexerNotice"] = "Indexer saved.";
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
