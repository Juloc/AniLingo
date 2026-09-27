using Jularr.Web.Data;
using Jularr.Web.Features.Ai;
using Jularr.Web.Features.Auth;
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

    public AiModelCatalog SavedCatalog =>
        SavedProviderId == AiProviderIds.OpenAiCompatible ? PersonalCatalog : ServerCatalog;

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
        TempData["Status"] = catalog.LastError is null
            ? Ui.Format("settings.ai.modelsRefreshed", ("count", catalog.Models.Count))
            : catalog.Discovery == AiModelDiscovery.Unsupported
                ? Ui["ai.models.unsupported"]
                : Ui["ai.models.refreshFailed"];
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
            var overrides = AiOperations.ProfileConfigurable.Select(operation => KeyValuePair.Create(
                operation,
                new AiOperationOverride(
                    OverrideModels.GetValueOrDefault(operation),
                    isServer ? OverrideEfforts.GetValueOrDefault(operation) : null)));

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
                    Overrides = AiOperationOverrides.From(overrides)
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
        HasStoredApiKey = !string.IsNullOrWhiteSpace(settings.ApiKey);
        ApiKey = null;

        // Page GETs stay local-first: the catalog shown is the cached one; discovery is the
        // explicit "Refresh models" action.
        await LoadViewAsync(cancellationToken, settings);
    }

    private async Task LoadViewAsync(CancellationToken cancellationToken, AiProfileSettings? settings = null)
    {
        Ui = await LoadBundleAsync(cancellationToken);
        settings ??= await settingsStore.LoadAsync(currentAccount.ProfileId, cancellationToken);
        HasStoredApiKey = !string.IsNullOrWhiteSpace(settings.ApiKey);
        SavedProviderId = settings.ProviderId;
        Now = time.GetUtcNow();

        ServerCatalog = await providerRouter.GetCachedCatalogAsync(AiProfileSettings.Default, cancellationToken);
        PersonalCatalog = settings.ProviderId == AiProviderIds.OpenAiCompatible
            ? await providerRouter.GetCachedCatalogAsync(settings, cancellationToken)
            : AiModelCatalog.Empty("");

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
