using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.LocalizationPreferences;

public sealed class IndexModel(
    AppDbContext db,
    CurrentAccountContext currentAccount) : PageModel
{
    public IReadOnlyList<UiLocaleSummary> Locales { get; private set; } = [];
    public string CurrentLocale { get; private set; } = UiTranslationCatalog.SourceLocale;
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(
        string locale,
        CancellationToken cancellationToken)
    {
        var store = new UiTranslationCatalogStore(db);
        await store.SyncSourceMessagesAsync(cancellationToken);

        try
        {
            await store.SetProfileLocaleAsync(
                currentAccount.ProfileId,
                locale,
                cancellationToken);
            Ui = await store.LoadProfileBundleAsync(
                currentAccount.ProfileId,
                cancellationToken);
            TempData["Status"] = Ui["settings.language.updated"];
            return RedirectToPage();
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var store = new UiTranslationCatalogStore(db);
        await store.SyncSourceMessagesAsync(cancellationToken);

        Locales = await store.ListLocalesAsync(cancellationToken);
        var current = await store.GetProfileLocaleAsync(
            currentAccount.ProfileId,
            cancellationToken);
        CurrentLocale = current.Locale;
        Ui = await store.LoadProfileBundleAsync(
            currentAccount.ProfileId,
            cancellationToken);
    }
}
