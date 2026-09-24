using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Localization;

public enum UiTranslationStatus
{
    Missing = 0,
    Generated = 1,
    Reviewed = 2,
    Manual = 3,
    Outdated = 4
}

public sealed record UiMessageDefinition(
    string Key,
    string DefaultText,
    string Feature,
    string Surface,
    string Description,
    string Tone = "neutral",
    int? MaxLength = null,
    IReadOnlyDictionary<string, string>? Placeholders = null,
    IReadOnlyList<string>? DoNotTranslate = null)
{
    public string SourceHash => UiTranslationCatalog.ComputeSourceHash(this);
}

public sealed record UiLocaleMetadata(
    string Locale,
    string EnglishName,
    string NativeName,
    string Direction);

public sealed record UiLocaleSummary(
    string Locale,
    string EnglishName,
    string NativeName,
    string Direction,
    bool IsSource,
    int Total,
    int Ready,
    int Missing,
    int Generated,
    int Reviewed,
    int Manual,
    int Outdated)
{
    public int CoveragePercent => Total == 0
        ? 100
        : (int)Math.Floor((double)Ready / Total * 100);
}

public sealed record UiTranslationEntry(
    UiMessageDefinition Message,
    string? Text,
    UiTranslationStatus Status,
    string? Provider,
    string? Model,
    DateTime? UpdatedAt);

public sealed record UiGeneratedTranslation(string Key, string Text);

public sealed record UiTranslationGenerationRequest(
    string TargetLocale,
    string TargetLanguageName,
    IReadOnlyList<UiMessageDefinition> Messages);

public sealed record UiTranslationGenerationResult(
    string ProviderId,
    string? Model,
    string PromptVersion,
    IReadOnlyList<UiGeneratedTranslation> Translations);

public interface IUiTranslationGenerator
{
    Task<UiTranslationGenerationResult> GenerateUiTranslationsAsync(
        UiTranslationGenerationRequest request,
        CancellationToken cancellationToken);
}

public static partial class UiTranslationCatalog
{
    public const string SourceLocale = "en";
    public const string PromptVersion = "ui-translation-v1";

    public static UiLocaleMetadata ParseLocale(string locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            throw new ArgumentException("Enter a BCP-47 locale such as de, id, ro or ja.", nameof(locale));
        }

        var candidate = locale.Trim().Replace('_', '-');
        try
        {
            var culture = CultureInfo.GetCultureInfo(candidate);
            if (string.IsNullOrWhiteSpace(culture.Name))
            {
                throw new CultureNotFoundException();
            }

            return new UiLocaleMetadata(
                culture.Name,
                culture.EnglishName,
                culture.NativeName,
                culture.TextInfo.IsRightToLeft ? "rtl" : "ltr");
        }
        catch (CultureNotFoundException exception)
        {
            throw new ArgumentException(
                $"'{locale}' is not a supported BCP-47 locale on this server.",
                nameof(locale),
                exception);
        }
    }

    public static IReadOnlyList<string> BuildFallbackChain(string locale)
    {
        var metadata = ParseLocale(locale);
        var culture = CultureInfo.GetCultureInfo(metadata.Locale);
        var result = new List<string>();

        while (!string.IsNullOrWhiteSpace(culture.Name))
        {
            if (!result.Contains(culture.Name, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(culture.Name);
            }

            culture = culture.Parent;
        }

        if (!result.Contains(SourceLocale, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(SourceLocale);
        }

        return result;
    }

    public static string ComputeSourceHash(UiMessageDefinition message)
    {
        var placeholders = message.Placeholders is null
            ? []
            : message.Placeholders
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new KeyValuePair<string, string>(x.Key, x.Value))
                .ToArray();
        var doNotTranslate = message.DoNotTranslate?
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray() ?? [];

        var canonical = JsonSerializer.Serialize(new
        {
            message.Key,
            message.DefaultText,
            message.Feature,
            message.Surface,
            message.Description,
            message.Tone,
            message.MaxLength,
            Placeholders = placeholders,
            DoNotTranslate = doNotTranslate
        });

        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static bool IsGeneratedTranslationValid(
        UiMessageDefinition message,
        string? translatedText)
    {
        if (string.IsNullOrWhiteSpace(translatedText))
        {
            return false;
        }

        var requiredPlaceholders = PlaceholderRegex()
            .Matches(message.DefaultText)
            .Select(x => x.Value)
            .Concat((message.Placeholders ?? new Dictionary<string, string>())
                .Keys
                .Select(x => "{" + x + "}"))
            .Distinct(StringComparer.Ordinal);

        if (requiredPlaceholders.Any(
                placeholder => !translatedText.Contains(
                    placeholder,
                    StringComparison.Ordinal)))
        {
            return false;
        }

        if (message.DoNotTranslate is { Count: > 0 }
            && message.DoNotTranslate.Any(
                token => message.DefaultText.Contains(token, StringComparison.Ordinal)
                    && !translatedText.Contains(token, StringComparison.Ordinal)))
        {
            return false;
        }

        return true;
    }

    [GeneratedRegex(@"\{[A-Za-z0-9_.-]+\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderRegex();
}
