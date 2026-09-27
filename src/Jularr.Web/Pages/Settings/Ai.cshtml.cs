using Jularr.Web.Data;
using Jularr.Web.Features.Ai;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Jularr.Web.Infrastructure.Ai;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Settings;

/// <summary>
/// Settings → AI (#422). The GET renders from local state only; when the selected provider's model
/// list was never loaded, the page asks for it once right after it rendered (DiscoverModels) and then
/// shows the real model picker. AI work never runs on an implicit provider default model.
/// </summary>
public sealed class AiModel(
    AppDbContext db,
    CurrentAccountContext currentAccount,
    AiProfileSettingsStore settingsStore,
    ProfileAiProviderRouter providerRouter,
    AiUsageStore usageStore,
    AiActivityTracker activityTracker,
    CodexCliProvider codex,
    TimeProvider time) : PageModel
{
    [BindProperty]
    public string? ImageModel { get; set; }

    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public bool HasStoredApiKey { get; private set; }
    public bool IsOwner => currentAccount.IsOwner;
    public string SavedProviderId { get; private set; } = AiProviderIds.Server;
    public AiModelCatalog ServerCatalog { get; private set; } = AiModelCatalog.Empty(AiModelCatalogKeys.CodexServer);
    public AiModelCatalog PersonalCatalog { get; private set; } = AiModelCatalog.Empty("");
    public AiUsageReport Usage { get; private set; } = AiUsageReport.Empty(AiUsagePeriod.Today);
    public AiActivityPanel Activity { get; private set; } = null!;
    public AiQuotaSnapshot? Quota { get; private set; }
    public DateTimeOffset Now { get; private set; }

    /// <summary>Saved server model the catalog no longer lists; the catalog default runs instead.</summary>
    public string? UnavailableServerModel { get; private set; }

    /// <summary>True when the page should load the model list once after it rendered.</summary>
    public bool DiscoverOnLoad { get; private set; }

    /// <summary>"Recommended for books" for the saved settings; null while no model is known.</summary>
    public AiBookPresetPlan? BookPreset { get; private set; }

    public bool BookPresetActive { get; private set; }

    /// <summary>Last known connection state; null when it was not checked since the server started.</summary>
    public AiProviderStatus? Connection { get; private set; }

    public AiModelCatalog SavedCatalog =>
        SavedProviderId == AiProviderIds.OpenAiCompatible ? PersonalCatalog : ServerCatalog;

    /// <summary>Server provider without catalog and without a manual model: AI tasks cannot start.</summary>
    public bool ServerModelMissing =>
        !ServerCatalog.HasModels && string.IsNullOrWhiteSpace(ServerModel);

    [BindProperty]
    public string ProviderId { get; set; } = AiProviderIds.Server;

    [BindProperty]
    public string? BaseUrl { get; set; }

    [BindProperty]
    public string? ModelName { get; set; }

    [BindProperty]
    public string? ServerModel { get; set; }

    [BindProperty]
    public string? ApiKey { get; set; }

    [BindProperty]
    public AiTranslationMode TranslationMode { get; set; } =
        AiTranslationMode.Efficient;

    [BindProperty]
    public string? ReasoningEffort { get; set; }

    [BindProperty]
    public string? ServiceTier { get; set; }

    [BindProperty]
    public int? MaxOutputTokens { get; set; }

    [BindProperty]
    public Dictionary<string, string?> OverrideModels { get; set; } = new(StringComparer.Ordinal);

    [BindProperty]
    public Dictionary<string, string?> OverrideEfforts { get; set; } = new(StringComparer.Ordinal);

    [BindProperty(SupportsGet = true)]
    public string? Period { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveAsync(
        CancellationToken cancellationToken)
    {
        if (!await SaveAsync(cancellationToken))
        {
            await LoadViewAsync(cancellationToken);
            return Page();
        }

        TempData["Status"] = Ui["settings.ai.saved"];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync(
        CancellationToken cancellationToken)
    {
        if (!await SaveAsync(cancellationToken))
        {
            await LoadViewAsync(cancellationToken);
            return Page();
        }

        var status = await providerRouter.TestCurrentAsync(cancellationToken);
        TempData["Status"] = status.IsAuthenticated
            ? Ui.Format("settings.ai.testSucceeded", ("provider", status.DisplayName))
            : AiErrorSanitizer.Sanitize(status.Error) ?? Ui["settings.ai.testFailed"];

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRefreshModelsAsync(
        CancellationToken cancellationToken)
    {
        if (!await SaveAsync(cancellationToken))
        {
            await LoadViewAsync(cancellationToken);
            return Page();
        }

        var settings = await settingsStore.LoadAsync(currentAccount.ProfileId, cancellationToken);
        var catalog = await providerRouter.RefreshCatalogAsync(settings, cancellationToken);
        TempData["Status"] = CatalogResult(catalog);
        return RedirectToPage();
    }

    /// <summary>
    /// The one automatic model discovery the page requests after it rendered. Only asks the provider
    /// while the saved provider's catalog needs it; otherwise it answers from the cache.
    /// </summary>
    public async Task<IActionResult> OnPostDiscoverModelsAsync(
        CancellationToken cancellationToken)
    {
        Ui = await LoadBundleAsync(cancellationToken);
        var settings = await settingsStore.LoadAsync(currentAccount.ProfileId, cancellationToken);
        var catalog = await providerRouter.GetOrDiscoverCatalogAsync(settings, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return new JsonResult(new
        {
            models = catalog.Models.Count,
            status = catalog.HasModels
                ? AiViewFormat.CatalogStatus(Ui, catalog, time.GetUtcNow())
                : CatalogResult(catalog)
        });
    }

    public async Task<IActionResult> OnPostApplyBookPresetAsync(
        CancellationToken cancellationToken)
    {
        if (!await SaveAsync(cancellationToken))
        {
            await LoadViewAsync(cancellationToken);
            return Page();
        }

        var settings = await settingsStore.LoadAsync(currentAccount.ProfileId, cancellationToken);
        var catalog = await providerRouter.GetCachedCatalogAsync(AiProfileSettings.Default, cancellationToken);
        var plan = AiBookPreset.Build(settings, catalog);
        if (plan is null)
        {
            TempData["Status"] = Ui["settings.ai.bookPreset.unavailable"];
            return RedirectToPage();
        }

        await settingsStore.SaveAsync(currentAccount.ProfileId, plan.Settings, cancellationToken);
        TempData["Status"] = Ui["settings.ai.bookPreset.applied"];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetAsync(
        CancellationToken cancellationToken)
    {
        await settingsStore.ResetAsync(
            currentAccount.ProfileId,
            cancellationToken);
        Ui = await LoadBundleAsync(cancellationToken);
        TempData["Status"] = Ui["settings.ai.resetDone"];
        return RedirectToPage();
    }

    public IReadOnlyList<AiReasoningOption> ReasoningOptionsFor(string? model) =>
        AiOptionResolver.ReasoningOptions(ServerCatalog, string.IsNullOrWhiteSpace(model) ? null : model);

    public IReadOnlyList<AiServiceTierOption> ServiceTierOptionsFor(string? model) =>
        AiOptionResolver.ServiceTierOptions(ServerCatalog, string.IsNullOrWhiteSpace(model) ? null : model);

    private string CatalogResult(AiModelCatalog catalog) =>
        catalog.LastError is null
            ? Ui.Format("settings.ai.modelsRefreshed", ("count", catalog.Models.Count))
            : catalog.Discovery == AiModelDiscovery.Unsupported
                ? Ui["ai.models.unsupported"]
                : Ui["ai.models.refreshFailed"];

    private async Task<bool> SaveAsync(
        CancellationToken cancellationToken)
    {
        Ui = await LoadBundleAsync(cancellationToken);

        try
        {
            var existing = await settingsStore.LoadAsync(
                currentAccount.ProfileId,
                cancellationToken);

            var key = string.IsNullOrWhiteSpace(ApiKey)
                ? existing.ApiKey
                : ApiKey;
            var isServer = ProviderId == AiProviderIds.Server;

            // Override model ids belong to one provider's catalog; switching provider starts clean.
            var overrides = ProviderId == existing.ProviderId
                ? AiOperationOverrides.From(AiOperations.ProfileConfigurable.Select(operation => KeyValuePair.Create(
                    operation,
                    new AiOperationOverride(
                        OverrideModels.GetValueOrDefault(operation),
                        isServer ? OverrideEfforts.GetValueOrDefault(operation) : null))))
                : AiOperationOverrides.Empty;

            await settingsStore.SaveAsync(
                currentAccount.ProfileId,
                new AiProfileSettings(
                    ProviderId,
                    BaseUrl,
                    isServer ? ServerModel : ModelName,
                    key,
                    TranslationMode)
                {
                    ImageModel = ImageModel,
                    ReasoningEffort = isServer ? ReasoningEffort : null,
                    ServiceTier = isServer ? ServiceTier : null,
                    MaxOutputTokens = isServer ? null : MaxOutputTokens,
                    Overrides = overrides
                },
                cancellationToken);

            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            ApiKey = null;
            return false;
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var settings = await settingsStore.LoadAsync(
            currentAccount.ProfileId,
            cancellationToken);

        ProviderId = settings.ProviderId;
        BaseUrl = settings.BaseUrl;
        ModelName = settings.ProviderId == AiProviderIds.OpenAiCompatible ? settings.Model : null;
        ServerModel = settings.ProviderId == AiProviderIds.Server ? settings.Model : null;
        TranslationMode = settings.TranslationMode;
        ImageModel = settings.ImageModel;
        ReasoningEffort = settings.ReasoningEffort;
        ServiceTier = settings.ServiceTier;
        MaxOutputTokens = settings.MaxOutputTokens;
        OverrideModels = settings.Overrides.Items
            .Where(x => x.Value.Model is not null)
            .ToDictionary(x => x.Key, x => x.Value.Model, StringComparer.Ordinal);
        OverrideEfforts = settings.Overrides.Items
            .Where(x => x.Value.ReasoningEffort is not null)
            .ToDictionary(x => x.Key, x => x.Value.ReasoningEffort, StringComparer.Ordinal);
        ApiKey = null;

        await LoadViewAsync(cancellationToken, settings);

        if (settings.ProviderId == AiProviderIds.Server && ServerCatalog.HasModels)
        {
            // The picker always shows the concrete model that runs: the saved one when the catalog
            // lists it, otherwise the catalog default.
            if (ServerModel is not null && ServerCatalog.Find(ServerModel) is null)
            {
                UnavailableServerModel = ServerModel;
            }

            ServerModel = AiOptionResolver.EffectiveServerModel(ServerCatalog, ServerModel);
        }
    }

    private async Task LoadViewAsync(CancellationToken cancellationToken, AiProfileSettings? settings = null)
    {
        Ui = await LoadBundleAsync(cancellationToken);
        settings ??= await settingsStore.LoadAsync(currentAccount.ProfileId, cancellationToken);
        HasStoredApiKey = !string.IsNullOrWhiteSpace(settings.ApiKey);
        SavedProviderId = settings.ProviderId;
        Now = time.GetUtcNow();

        // Local state only: the catalogs shown are the cached ones. DiscoverOnLoad lets the page
        // request the first discovery after it rendered.
        ServerCatalog = await providerRouter.GetCachedCatalogAsync(AiProfileSettings.Default, cancellationToken);
        PersonalCatalog = settings.ProviderId == AiProviderIds.OpenAiCompatible
            ? await providerRouter.GetCachedCatalogAsync(settings, cancellationToken)
            : AiModelCatalog.Empty("");
        DiscoverOnLoad = ProfileAiProviderRouter.CanDiscover(settings)
            && AiModelCatalogService.NeedsDiscovery(SavedCatalog, Now);

        BookPreset = AiBookPreset.Build(settings, ServerCatalog);
        BookPresetActive = BookPreset?.IsActiveFor(settings) == true;

        Connection = settings.ProviderId == AiProviderIds.Server
            ? codex.LastStatus
            : await providerRouter.GetStatusAsync(cancellationToken);

        Usage = await usageStore.GetReportAsync(
            currentAccount.ProfileId,
            DateOnly.FromDateTime(Now.UtcDateTime),
            ParsePeriod(Period),
            cancellationToken);

        Activity = new AiActivityPanel(
            Ui,
            AiActivityEndpoints.List(activityTracker, currentAccount.ProfileId, Now).Take(10).ToArray(),
            AllProfiles: false);

        Quota = settings.ProviderId == AiProviderIds.Server ? codex.LatestQuota : null;
    }

    internal static AiUsagePeriod ParsePeriod(string? value) =>
        value switch
        {
            "7d" => AiUsagePeriod.Last7Days,
            "30d" => AiUsagePeriod.Last30Days,
            _ => AiUsagePeriod.Today
        };

    private Task<UiTextBundle> LoadBundleAsync(CancellationToken cancellationToken) =>
        new UiTranslationCatalogStore(db).LoadProfileBundleAsync(
            currentAccount.ProfileId,
            cancellationToken);
}
