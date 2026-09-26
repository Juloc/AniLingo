using AniLingo.Web.Features.Acquisition.Sabnzbd;
using AniLingo.Web.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings;

/// <summary>
/// The one place where the owner configures SABnzbd for Books and Anime.
/// </summary>
[Authorize(Roles = AccountRoles.Owner)]
public sealed class SabnzbdModel(
    SabnzbdSettingsStore store,
    SabnzbdConnectionResolver resolver,
    ISabnzbdClient client) : PageModel
{
    [BindProperty]
    public string? BaseUrl { get; set; }

    [BindProperty]
    public string? ApiKey { get; set; }

    [BindProperty]
    public bool ClearApiKey { get; set; }

    [BindProperty]
    public string? BooksCategory { get; set; }

    [BindProperty]
    public string? AnimeCategory { get; set; }

    public SabnzbdResolvedSettings? Resolved { get; private set; }
    public bool HasSavedApiKey => Resolved?.Stored.ApiKey is not null;
    public string? Notice => TempData["SabnzbdNotice"] as string;
    public string? Error => TempData["SabnzbdError"] as string;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            var existing = await store.LoadAsync(cancellationToken);
            await store.SaveAsync(
                new SabnzbdStoredSettings(
                    BaseUrl,
                    ClearApiKey
                        ? null
                        : string.IsNullOrWhiteSpace(ApiKey)
                            ? existing.ApiKey
                            : ApiKey,
                    BooksCategory,
                    AnimeCategory),
                cancellationToken);
            TempData["SabnzbdNotice"] = "SABnzbd settings saved.";
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidDataException
                or IOException
                or UnauthorizedAccessException)
        {
            TempData["SabnzbdError"] = exception.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync(CancellationToken cancellationToken)
    {
        var connection = await resolver.GetConnectionAsync(cancellationToken);
        if (connection is null)
        {
            TempData["SabnzbdError"] = "Save a SABnzbd URL and API key first.";
            return RedirectToPage();
        }

        var result = await client.TestAsync(connection, cancellationToken);
        if (result.Success)
        {
            TempData["SabnzbdNotice"] =
                $"Connected to SABnzbd {result.Version}. Queue access works, so AniLingo can track progress.";
        }
        else
        {
            TempData["SabnzbdError"] = result.Error ?? "SABnzbd connection failed.";
        }

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Resolved = await resolver.ResolveAsync(cancellationToken);
        BaseUrl = Resolved.Stored.BaseUrl;
        BooksCategory = Resolved.Stored.BooksCategory;
        AnimeCategory = Resolved.Stored.AnimeCategory;
    }
}
