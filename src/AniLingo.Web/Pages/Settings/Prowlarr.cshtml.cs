using System.Security.Cryptography;
using AniLingo.Web.Features.Acquisition.Prowlarr;
using AniLingo.Web.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings;

/// <summary>
/// The one place where the owner configures the Prowlarr connection used by anime acquisition.
/// </summary>
[Authorize(Roles = AccountRoles.Owner)]
public sealed class ProwlarrModel(
    ProwlarrSettingsStore store,
    IProwlarrClient client) : PageModel
{
    [BindProperty]
    public string? BaseUrl { get; set; }

    [BindProperty]
    public string? ApiKey { get; set; }

    [BindProperty]
    public string? Categories { get; set; }

    [BindProperty]
    public string? IndexerIds { get; set; }

    [BindProperty]
    public int SearchLimit { get; set; } = 100;

    public bool IsConfigured { get; private set; }
    public string? LoadError { get; private set; }
    public string? Notice => TempData["ProwlarrNotice"] as string;
    public string? Error => TempData["ProwlarrError"] as string;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var connection = await LoadAsync(cancellationToken);
        var settings = connection?.Settings ?? ProwlarrSettings.CreateDefault("");
        IsConfigured = connection is not null;
        BaseUrl = settings.BaseUrl;
        Categories = string.Join(", ", settings.Categories);
        IndexerIds = string.Join(", ", settings.IndexerIds);
        SearchLimit = settings.SearchLimit;
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!TryParseIds(Categories, out var categories) || !TryParseIds(IndexerIds, out var indexerIds))
            {
                throw new ArgumentException("Categories and indexer IDs must be positive numbers separated by commas.");
            }

            var apiKey = string.IsNullOrWhiteSpace(ApiKey)
                ? (await LoadAsync(cancellationToken))?.ApiKey
                : ApiKey.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("Enter the Prowlarr API key.");
            }

            await store.SaveAsync(
                new ProwlarrConnection(
                    new ProwlarrSettings(BaseUrl ?? "", categories, indexerIds, SearchLimit),
                    apiKey),
                cancellationToken);
            TempData["ProwlarrNotice"] = "Prowlarr settings saved.";
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            TempData["ProwlarrError"] = exception.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync(CancellationToken cancellationToken)
    {
        var connection = await LoadAsync(cancellationToken);
        if (connection is null)
        {
            TempData["ProwlarrError"] = LoadError ?? "Save a Prowlarr URL and API key first.";
            return RedirectToPage();
        }

        var result = await client.TestAsync(connection, cancellationToken);
        if (result.Success)
        {
            TempData["ProwlarrNotice"] = $"Connected to Prowlarr {result.Version}.";
        }
        else
        {
            TempData["ProwlarrError"] = result.Error ?? "Prowlarr connection failed.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(CancellationToken cancellationToken)
    {
        await store.DeleteAsync(cancellationToken);
        TempData["ProwlarrNotice"] = "Prowlarr connection removed; automatic anime searches stop.";
        return RedirectToPage();
    }

    private async Task<ProwlarrConnection?> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await store.LoadAsync(cancellationToken);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or ArgumentException or CryptographicException)
        {
            LoadError = $"The saved Prowlarr settings cannot be read ({exception.Message}). Save them again.";
            return null;
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
            if (!int.TryParse(part, out var id) || id <= 0)
            {
                return false;
            }

            parsed.Add(id);
        }

        ids = [.. parsed];
        return true;
    }
}
