using Jularr.Web.Data;
using Jularr.Web.Features.Ai;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.ChapterArtwork;
using Jularr.Web.Features.Localization;
using Jularr.Web.Infrastructure.Ai;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Settings;

public sealed class AiModel(
    AppDbContext db,
    CurrentAccountContext currentAccount,
    AiProfileSettingsStore settingsStore,
    ProfileAiProviderRouter providerRouter,
    AiUsageTracker usageTracker,
    ChapterArtworkStore artworkStore,
    ChapterArtworkGlobalSettingsStore artworkGlobal) : PageModel
{
    public ChapterArtworkPreferences Artwork { get; private set; } = ChapterArtworkPreferences.Default;
    public ChapterArtworkGlobalSettings ArtworkGlobal { get; private set; } = ChapterArtworkGlobalSettings.Default;

    [BindProperty]
    public string? ImageModel { get; set; }

    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public bool HasStoredApiKey { get; private set; }
    public bool IsOwner => currentAccount.IsOwner;
    public AiUsageSnapshot Usage { get; private set; } =
        new(0, 0, 0, 0, 0, []);

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
                    TranslationMode)
                {
                    ImageModel = ImageModel
                },
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
        ImageModel = settings.ImageModel;
        HasStoredApiKey = !string.IsNullOrWhiteSpace(settings.ApiKey);
        ApiKey = null;
    }

    private async Task LoadUiAsync(CancellationToken cancellationToken)
    {
        var catalog = new UiTranslationCatalogStore(db);
        Ui = await catalog.LoadProfileBundleAsync(
            currentAccount.ProfileId,
            cancellationToken);
        Usage = usageTracker.GetSnapshot(currentAccount.ProfileId);
        Artwork = await artworkStore.GetPreferencesAsync(currentAccount.ProfileId, cancellationToken);
        ArtworkGlobal = artworkGlobal.Load();
    }

    public async Task<IActionResult> OnPostArtworkAsync(
        bool enabled,
        bool autoGenerate,
        string? style,
        string? quality,
        int variations,
        bool globalEnabled,
        string? storageRoot,
        CancellationToken cancellationToken)
    {
        var current = await artworkStore.GetPreferencesAsync(currentAccount.ProfileId, cancellationToken);

        // Generation choices write to shared media storage and stay owner-only.
        var preferences = currentAccount.IsOwner
            ? new ChapterArtworkPreferences(
                enabled,
                autoGenerate,
                ChapterArtworkNames.ParseStyle(style),
                ChapterArtworkNames.ParseQuality(quality),
                Math.Clamp(variations, 1, ChapterArtworkPreferences.MaxVariations))
            : current with { Enabled = enabled };

        await artworkStore.SavePreferencesAsync(currentAccount.ProfileId, preferences, cancellationToken);

        if (currentAccount.IsOwner)
        {
            try
            {
                await artworkGlobal.SaveAsync(
                    new ChapterArtworkGlobalSettings(globalEnabled, storageRoot),
                    cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                TempData["Status"] = exception.Message;
                return RedirectToPage();
            }
        }

        TempData["Status"] = "AI settings updated.";
        return RedirectToPage();
    }
}
