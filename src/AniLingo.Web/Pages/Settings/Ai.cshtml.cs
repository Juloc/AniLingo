using AniLingo.Web.Data;
using AniLingo.Web.Features.Ai;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Localization;
using AniLingo.Web.Infrastructure.Ai;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings;

public sealed class AiModel(
    AppDbContext db,
    CurrentAccountContext currentAccount,
    AiProfileSettingsStore settingsStore,
    ProfileAiProviderRouter providerRouter) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public bool HasStoredApiKey { get; private set; }
    public bool IsOwner => currentAccount.IsOwner;

    [BindProperty]
    public string ProviderId { get; set; } = AiProviderIds.Server;

    [BindProperty]
    public string? BaseUrl { get; set; }

    [BindProperty]
    public string? ModelName { get; set; }

    [BindProperty]
    public string? ApiKey { get; set; }

    [BindProperty]
    public AiTranslationMode TranslationMode { get; set; } =
        AiTranslationMode.Efficient;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveAsync(
        CancellationToken cancellationToken)
    {
        if (!await SaveAsync(cancellationToken))
        {
            await LoadUiAsync(cancellationToken);
            return Page();
        }

        TempData["Status"] = "AI settings updated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync(
        CancellationToken cancellationToken)
    {
        if (!await SaveAsync(cancellationToken))
        {
            await LoadUiAsync(cancellationToken);
            return Page();
        }

        var status = await providerRouter.TestCurrentAsync(cancellationToken);
        TempData["Status"] = status.IsAuthenticated
            ? $"{status.DisplayName} is reachable."
            : status.Error ?? "AI provider test failed.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetAsync(
        CancellationToken cancellationToken)
    {
        await settingsStore.ResetAsync(
            currentAccount.ProfileId,
            cancellationToken);
        TempData["Status"] = "AI settings reset to defaults.";
        return RedirectToPage();
    }

    private async Task<bool> SaveAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await settingsStore.LoadAsync(
                currentAccount.ProfileId,
                cancellationToken);

            var key = string.IsNullOrWhiteSpace(ApiKey)
                ? existing.ApiKey
                : ApiKey;

            await settingsStore.SaveAsync(
                currentAccount.ProfileId,
                new AiProfileSettings(
                    ProviderId,
                    BaseUrl,
                    ModelName,
                    key,
                    TranslationMode),
                cancellationToken);

            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return false;
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        await LoadUiAsync(cancellationToken);

        var settings = await settingsStore.LoadAsync(
            currentAccount.ProfileId,
            cancellationToken);

        ProviderId = settings.ProviderId;
        BaseUrl = settings.BaseUrl;
        ModelName = settings.Model;
        TranslationMode = settings.TranslationMode;
        HasStoredApiKey = !string.IsNullOrWhiteSpace(settings.ApiKey);
        ApiKey = null;
    }

    private async Task LoadUiAsync(CancellationToken cancellationToken)
    {
        var catalog = new UiTranslationCatalogStore(db);
        Ui = await catalog.LoadProfileBundleAsync(
            currentAccount.ProfileId,
            cancellationToken);
    }
}
