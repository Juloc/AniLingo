using AniLingo.Web.Features.Acquisition.DownloadClients;
using AniLingo.Web.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings.DownloadClients;

/// <summary>Add or edit one canonical SABnzbd download client entry.</summary>
[Authorize(Roles = AccountRoles.Owner)]
public sealed class EditModel(DownloadClientStore store) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid? Id { get; set; }

    [BindProperty]
    public string Name { get; set; } = "";

    [BindProperty]
    public string BaseUrl { get; set; } = "";

    [BindProperty]
    public string? Secret { get; set; }

    [BindProperty]
    public string? BooksCategory { get; set; }

    [BindProperty]
    public string? AnimeCategory { get; set; }

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
            Error = "Download client not found.";
            return;
        }

        Name = entry.Name;
        BaseUrl = entry.Settings.BaseUrl;
        BooksCategory = entry.Settings.BooksCategory;
        AnimeCategory = entry.Settings.AnimeCategory;
        Priority = entry.Priority;
        Enabled = entry.Enabled;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        try
        {
            var existing = Id is { } id ? await store.GetAsync(id, cancellationToken) : null;
            var secret = string.IsNullOrWhiteSpace(Secret) ? existing?.Secret : Secret.Trim();
            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new ArgumentException("Enter the SABnzbd API key.");
            }

            await store.SaveAsync(
                new DownloadClientEntry(
                    existing?.Id ?? Guid.NewGuid(),
                    Name,
                    DownloadClientType.Sabnzbd,
                    Enabled,
                    Priority,
                    new DownloadClientSettings(BaseUrl, BooksCategory, AnimeCategory),
                    secret),
                cancellationToken);

            TempData["DownloadClientNotice"] = "Download client saved.";
            return RedirectToPage("Index");
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Error = exception.Message;
            return Page();
        }
    }
}
