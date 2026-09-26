using AniLingo.Web.Data;
using AniLingo.Web.Features.Ai;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Localization;
using AniLingo.Web.Infrastructure.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.LocalizationAdmin;

[Authorize(Roles = AccountRoles.Owner)]
public sealed class IndexModel(
    AppDbContext db,
    CodexCliProvider translationGenerator) : PageModel
{
    private const int GenerationBatchSize = 100;

    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public IReadOnlyList<UiLocaleSummary> Locales { get; private set; } = [];
    public IReadOnlyList<UiTranslationEntry> Entries { get; private set; } = [];
    public string SelectedLocale { get; private set; } = UiTranslationCatalog.SourceLocale;
    public string? Search { get; private set; }
    public AiProviderStatus? AiStatus { get; private set; }

    public async Task OnGetAsync(
        string? locale,
        string? search,
        CancellationToken cancellationToken)
    {
        await LoadAsync(locale, search, cancellationToken);
    }

    public async Task<IActionResult> OnPostAddLanguageAsync(
        string locale,
        bool generateNow,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        var store = new UiTranslationCatalogStore(db);
        await store.SyncSourceMessagesAsync(cancellationToken);

        try
        {
            var added = await store.AddLocaleAsync(locale, cancellationToken);
            if (generateNow
                && !added.Locale.Equals(
                    UiTranslationCatalog.SourceLocale,
                    StringComparison.OrdinalIgnoreCase))
            {
                var count = await GenerateAsync(
                    store,
                    added.Locale,
                    includeMissing: true,
                    includeOutdated: false,
                    cancellationToken);
                TempData["Status"] = Ui.Format(
                    "localizationAdmin.status.addedAndGenerated",
                    ("name", added.NativeName),
                    ("locale", added.Locale),
                    ("count", count));
            }
            else
            {
                TempData["Status"] = Ui.Format(
                    "localizationAdmin.status.added",
                    ("name", added.NativeName),
                    ("locale", added.Locale));
            }

            return RedirectToPage(new { locale = added.Locale });
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadAsync(locale, null, cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostGenerateMissingAsync(
        string locale,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        var store = new UiTranslationCatalogStore(db);
        await store.SyncSourceMessagesAsync(cancellationToken);

        try
        {
            var count = await GenerateAsync(
                store,
                locale,
                includeMissing: true,
                includeOutdated: false,
                cancellationToken);
            TempData["Status"] = count == 0
                ? Ui["localizationAdmin.status.noMissing"]
                : Ui.Format("localizationAdmin.status.generatedMissing", ("count", count));
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage(new { locale });
    }

    public async Task<IActionResult> OnPostRegenerateOutdatedAsync(
        string locale,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        var store = new UiTranslationCatalogStore(db);
        await store.SyncSourceMessagesAsync(cancellationToken);

        try
        {
            var count = await GenerateAsync(
                store,
                locale,
                includeMissing: false,
                includeOutdated: true,
                cancellationToken);
            TempData["Status"] = count == 0
                ? Ui["localizationAdmin.status.noOutdated"]
                : Ui.Format("localizationAdmin.status.regeneratedOutdated", ("count", count));
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage(new { locale });
    }

    public async Task<IActionResult> OnPostSaveManualAsync(
        string locale,
        string key,
        string text,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        var store = new UiTranslationCatalogStore(db);
        await store.SyncSourceMessagesAsync(cancellationToken);

        try
        {
            await store.SaveManualAsync(locale, key, text, cancellationToken);
            TempData["Status"] = Ui.Format("localizationAdmin.status.savedManual", ("key", key));
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidOperationException
            or KeyNotFoundException)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage(new { locale });
    }

    public async Task<IActionResult> OnPostMarkReviewedAsync(
        string locale,
        string key,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        var store = new UiTranslationCatalogStore(db);
        await store.SyncSourceMessagesAsync(cancellationToken);

        try
        {
            await store.MarkReviewedAsync(locale, key, cancellationToken);
            TempData["Status"] = Ui.Format("localizationAdmin.status.markedReviewed", ("key", key));
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidOperationException
            or KeyNotFoundException)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage(new { locale });
    }

    private async Task<int> GenerateAsync(
        UiTranslationCatalogStore store,
        string locale,
        bool includeMissing,
        bool includeOutdated,
        CancellationToken cancellationToken)
    {
        var metadata = await store.AddLocaleAsync(locale, cancellationToken);
        if (metadata.Locale.Equals(
            UiTranslationCatalog.SourceLocale,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                Ui["localizationAdmin.error.englishIsSource"]);
        }

        var provider = await translationGenerator.GetStatusAsync(cancellationToken);
        if (!provider.IsAvailable || !provider.IsAuthenticated)
        {
            throw new InvalidOperationException(
                Ui["localizationAdmin.error.aiNotConnected"]);
        }

        var pending = await store.GetPendingDefinitionsAsync(
            metadata.Locale,
            includeMissing,
            includeOutdated,
            GenerationBatchSize,
            cancellationToken);
        if (pending.Count == 0)
        {
            return 0;
        }

        var result = await translationGenerator.GenerateUiTranslationsAsync(
            new UiTranslationGenerationRequest(
                metadata.Locale,
                metadata.EnglishName,
                pending),
            cancellationToken);

        await store.SaveGeneratedAsync(metadata.Locale, result, cancellationToken);
        return result.Translations.Count;
    }

    private async Task LoadAsync(
        string? locale,
        string? search,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        var store = new UiTranslationCatalogStore(db);
        await store.SyncSourceMessagesAsync(cancellationToken);

        Locales = await store.ListLocalesAsync(cancellationToken);
        Search = search;

        var requested = string.IsNullOrWhiteSpace(locale)
            ? UiTranslationCatalog.SourceLocale
            : locale;

        try
        {
            SelectedLocale = UiTranslationCatalog.ParseLocale(requested).Locale;
        }
        catch (ArgumentException)
        {
            SelectedLocale = UiTranslationCatalog.SourceLocale;
        }

        if (!Locales.Any(x =>
                x.Locale.Equals(SelectedLocale, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedLocale = UiTranslationCatalog.SourceLocale;
        }

        Entries = await store.GetEntriesAsync(
            SelectedLocale,
            Search,
            cancellationToken);
        AiStatus = await translationGenerator.GetStatusAsync(cancellationToken);
    }
}
