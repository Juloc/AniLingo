using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.DownloadClients;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Settings.DownloadClients;

/// <summary>Add or edit one canonical SABnzbd download client entry.</summary>
[Authorize(Policy = JularrPolicies.AcquisitionSettings)]
public sealed class EditModel(AppDbContext db, DownloadClientStore store, ILogger<EditModel> logger) : PageModel
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
    public string? MangaCategory { get; set; }

    [BindProperty]
    public string? LightNovelCategory { get; set; }

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
        BooksCategory = entry.Settings.CategoryFor(MediaAcquisitionKind.Book);
        AnimeCategory = entry.Settings.CategoryFor(MediaAcquisitionKind.Anime);
        MangaCategory = entry.Settings.CategoryFor(MediaAcquisitionKind.Manga);
        LightNovelCategory = entry.Settings.CategoryFor(MediaAcquisitionKind.LightNovel);
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
                    new DownloadClientSettings(
                        BaseUrl,
                        new Dictionary<MediaAcquisitionKind, string?>
                        {
                            [MediaAcquisitionKind.Anime] = AnimeCategory,
                            [MediaAcquisitionKind.Manga] = MangaCategory,
                            [MediaAcquisitionKind.LightNovel] = LightNovelCategory,
                            [MediaAcquisitionKind.Book] = BooksCategory
                        }),
                    secret),
                cancellationToken);

            TempData["DownloadClientNotice"] = Ui["settings.downloadClients.saved"];
            return RedirectToPage("/Admin/Usenet");
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Saving download client {Name} failed", Name);
            Error = Ui["settings.downloadClients.saveFailed"];
            return Page();
        }
    }
}
