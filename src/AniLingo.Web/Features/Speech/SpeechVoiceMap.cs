using System.Text.Json;

namespace AniLingo.Web.Features.Speech;

/// <summary>
/// One per-language voice selection map (normalized BCP-47 tag -> provider voice id).
/// The map is stored as one JSON column on a reader preference scope; every language
/// entry inherits independently through the reader preference cascade.
/// </summary>
public static class SpeechVoiceMap
{
    public const int MaxEntries = 32;
    public const int MaxVoiceIdLength = 200;

    public static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        Dictionary<string, string?>? raw;
        try
        {
            raw = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
        }
        catch (JsonException)
        {
            return Empty;
        }

        if (raw is null || raw.Count == 0)
        {
            return Empty;
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in raw.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var language = SpeechPreferenceResolver.NormalizeLanguageTag(key);
            var voiceId = NormalizeVoiceId(value);
            if (language == "und" || voiceId is null || map.ContainsKey(language))
            {
                continue;
            }

            map[language] = voiceId;
            if (map.Count >= MaxEntries)
            {
                break;
            }
        }

        return map;
    }

    public static string? Serialize(IReadOnlyDictionary<string, string> map)
    {
        if (map.Count == 0)
        {
            return null;
        }

        var ordered = map
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        return JsonSerializer.Serialize(ordered);
    }

    /// <summary>
    /// Exact normalized tag first, then the base language ("de-AT" falls back to "de").
    /// </summary>
    public static string? VoiceIdFor(
        IReadOnlyDictionary<string, string> map,
        string? language)
    {
        if (map.Count == 0)
        {
            return null;
        }

        var normalized = SpeechPreferenceResolver.NormalizeLanguageTag(language);
        if (normalized == "und")
        {
            return null;
        }

        if (map.TryGetValue(normalized, out var exact))
        {
            return exact;
        }

        var baseLanguage = SpeechPreferenceResolver.BaseLanguage(normalized);
        return map.TryGetValue(baseLanguage, out var fallback) ? fallback : null;
    }

    public static string? NormalizeVoiceId(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > MaxVoiceIdLength ||
            string.Equals(normalized, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return normalized;
    }
}

public sealed record SpeechAvailability(
    SpeechResolution? Resolution,
    string? UnavailableReason)
{
    public bool IsAvailable => Resolution is not null;

    public const string NoProvider = "no-provider";
    public const string ProviderUnavailable = "provider-unavailable";
    public const string NoVoiceForLanguage = "no-voice-for-language";
}

public static class SpeechAvailabilityResolver
{
    /// <summary>
    /// Same deterministic order as <see cref="SpeechPreferenceResolver.Resolve"/> but always
    /// explains why nothing could be resolved so the Reader can show a useful state.
    /// </summary>
    public static SpeechAvailability Explain(
        SpeechPreferences preferences,
        IReadOnlyCollection<SpeechProviderDescriptor> providers,
        IReadOnlyCollection<SpeechVoiceDescriptor> voices)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var resolution = SpeechPreferenceResolver.Resolve(preferences, providers, voices);
        if (resolution is not null)
        {
            return new SpeechAvailability(resolution, null);
        }

        if (providers.Count == 0)
        {
            return new SpeechAvailability(null, SpeechAvailability.NoProvider);
        }

        return providers.Any(x => x.IsAvailable)
            ? new SpeechAvailability(null, SpeechAvailability.NoVoiceForLanguage)
            : new SpeechAvailability(null, SpeechAvailability.ProviderUnavailable);
    }
}
