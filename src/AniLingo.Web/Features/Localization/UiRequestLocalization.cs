using System.Globalization;
using System.Security.Claims;
using AniLingo.Web.Data;
using Microsoft.Net.Http.Headers;

namespace AniLingo.Web.Features.Localization;

/// <summary>
/// Resolves the UI text bundle for the current request exactly once.
/// Signed-in profiles use their persisted profile locale; anonymous pages
/// (setup, sign-in, registration) negotiate the browser Accept-Language header
/// against the Owner-enabled locales and otherwise fall back to English.
/// </summary>
public static class UiRequestLocalization
{
    private const int MaxNegotiatedLanguages = 16;
    private static readonly object BundleItemKey = new();

    public static async Task<UiTextBundle> GetBundleAsync(
        HttpContext httpContext,
        AppDbContext db)
    {
        if (httpContext.Items.TryGetValue(BundleItemKey, out var cached)
            && cached is UiTextBundle cachedBundle)
        {
            return cachedBundle;
        }

        var cancellationToken = httpContext.RequestAborted;
        var store = new UiTranslationCatalogStore(db);
        var profileId = httpContext.User.Identity?.IsAuthenticated == true
            ? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;

        UiTextBundle bundle;
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            bundle = await store.LoadProfileBundleAsync(profileId, cancellationToken);
        }
        else
        {
            var enabled = await store.ListEnabledLocalesAsync(cancellationToken);
            var locale = SelectBrowserLocale(
                httpContext.Request.GetTypedHeaders().AcceptLanguage,
                enabled);
            bundle = await store.LoadLocaleBundleAsync(locale, cancellationToken);

            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.Headers.Append(
                    HeaderNames.Vary,
                    HeaderNames.AcceptLanguage);
            }
        }

        httpContext.Items[BundleItemKey] = bundle;
        return bundle;
    }

    public static UiLocaleMetadata SelectBrowserLocale(
        IEnumerable<StringWithQualityHeaderValue>? acceptLanguage,
        IReadOnlyList<UiLocaleMetadata> enabledLocales)
    {
        var english = UiTranslationCatalog.ParseLocale(UiTranslationCatalog.SourceLocale);
        if (acceptLanguage is null || enabledLocales.Count == 0)
        {
            return english;
        }

        var enabled = enabledLocales
            .GroupBy(x => x.Locale, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        // OrderByDescending is stable, so equal q-values keep header order.
        var requested = acceptLanguage
            .Take(MaxNegotiatedLanguages)
            .Where(x => x.Quality is null or > 0)
            .OrderByDescending(x => x.Quality ?? 1);

        foreach (var language in requested)
        {
            var tag = language.Value.Value;
            if (string.IsNullOrWhiteSpace(tag) || tag == "*")
            {
                continue;
            }

            UiLocaleMetadata metadata;
            try
            {
                metadata = UiTranslationCatalog.ParseLocale(tag);
            }
            catch (ArgumentException)
            {
                continue;
            }

            // Exact culture first, then its parents (de-CH -> de). English is
            // only matched when the browser actually asked for it.
            for (var culture = CultureInfo.GetCultureInfo(metadata.Locale);
                 !string.IsNullOrWhiteSpace(culture.Name);
                 culture = culture.Parent)
            {
                if (enabled.TryGetValue(culture.Name, out var match))
                {
                    return match;
                }
            }
        }

        return english;
    }
}
