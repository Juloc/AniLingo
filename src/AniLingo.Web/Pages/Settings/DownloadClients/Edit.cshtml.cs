using AniLingo.Web.Data;
using AniLingo.Web.Features.Acquisition.DownloadClients;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings.DownloadClients;

/// <summary>Add or edit one canonical SABnzbd download client entry.</summary>
[Authorize(Roles = AccountRoles.Owner)]
public sealed class EditModel(AppDbContext db, DownloadClientStore store) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

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
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        if (Id is not { } id)
        {
            return;
        }

        var entry = await store.GetAsync(id, cancellationToken);
        if (entry is null)
        {
            Error = Ui["settings.downloadClients.notFound"];
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
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        try
        {
            var existing = Id is { } id ? await store.GetAsync(id, cancellationToken) : null;
            var secret = string.IsNullOrWhiteSpace(Secret) ? existing?.Secret : Secret.Trim();
            if (string.IsNullOrWhiteSpace(secret))
            {
                Error = Ui["settings.downloadClients.enterApiKey"];
                return Page();
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

            TempData["DownloadClientNotice"] = Ui["settings.downloadClients.saved"];
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
