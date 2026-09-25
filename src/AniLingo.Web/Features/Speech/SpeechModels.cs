namespace AniLingo.Web.Features.Speech;

public enum SpeechProviderKind
{
    OfflineNeural = 0,
    Device = 1,
    Cloud = 2
}

[Flags]
public enum SpeechProviderCapabilities
{
    None = 0,
    PauseResume = 1 << 0,
    BoundaryEvents = 1 << 1,
    PlatformDefaultVoice = 1 << 2,
    GuaranteedOffline = 1 << 3
}

public sealed record SpeechProviderDescriptor(
    string Id,
    string DisplayName,
    SpeechProviderKind Kind,
    bool IsAvailable,
    SpeechProviderCapabilities Capabilities);

public sealed record SpeechVoiceDescriptor(
    string ProviderId,
    string VoiceId,
    string DisplayName,
    string Language,
    bool IsDefault = false,
    bool IsLocal = false);

public sealed record SpeechPreferences(
    string? ProviderId = null,
    string? VoiceId = null,
    string? Language = null,
    double Rate = 1,
    double Pitch = 1,
    double Volume = 1);

public sealed record SpeechResolution(
    string ProviderId,
    string? VoiceId,
    string Language,
    double Rate,
    double Pitch,
    double Volume,
    bool UsesProviderDefaultVoice,
    string Reason);

public static class SpeechPreferenceResolver
{
    public static SpeechResolution? Resolve(
        SpeechPreferences preferences,
        IReadOnlyCollection<SpeechProviderDescriptor> providers,
        IReadOnlyCollection<SpeechVoiceDescriptor> voices)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(voices);

        var language = NormalizeLanguageTag(preferences.Language);
        var requestedProvider = NormalizeId(preferences.ProviderId);
        var requestedVoice = NormalizeId(preferences.VoiceId);
        var available = providers
            .Where(x => x.IsAvailable)
            .OrderBy(x => ProviderRank(x.Kind))
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();

        if (available.Length == 0)
        {
            return null;
        }

        if (requestedProvider is not null)
        {
            var explicitProvider = available.FirstOrDefault(
                x => string.Equals(x.Id, requestedProvider, StringComparison.OrdinalIgnoreCase));

            if (explicitProvider is not null)
            {
                var explicitResolution = ResolveWithinProvider(
                    explicitProvider,
                    voices,
                    requestedVoice,
                    language,
                    preferences);

                if (explicitResolution is not null)
                {
                    return explicitResolution with { Reason = "selected-provider" };
                }
            }
        }

        foreach (var provider in available)
        {
            var resolution = ResolveWithinProvider(
                provider,
                voices,
                requestedProvider is not null &&
                string.Equals(provider.Id, requestedProvider, StringComparison.OrdinalIgnoreCase)
                    ? requestedVoice
                    : null,
                language,
                preferences);

            if (resolution is not null)
            {
                return resolution with { Reason = ProviderFallbackReason(provider.Kind) };
            }
        }

        return null;
    }

    public static string NormalizeLanguageTag(string? value)
    {
        var raw = value?.Trim().Replace('_', '-');
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "und";
        }

        var parts = raw.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 ||
            parts.Any(part => part.Length is < 1 or > 8 || part.Any(ch => !char.IsLetterOrDigit(ch))))
        {
            return "und";
        }

        parts[0] = parts[0].ToLowerInvariant();

        for (var i = 1; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 4 && part.All(char.IsLetter))
            {
                parts[i] = char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant();
            }
            else if ((part.Length == 2 && part.All(char.IsLetter)) ||
                     (part.Length == 3 && part.All(char.IsDigit)))
            {
                parts[i] = part.ToUpperInvariant();
            }
            else
            {
                parts[i] = part.ToLowerInvariant();
            }
        }

        var normalized = string.Join('-', parts);
        return normalized.Length <= 64 ? normalized : "und";
    }

    public static string BaseLanguage(string? value)
    {
        var normalized = NormalizeLanguageTag(value);
        var separator = normalized.IndexOf('-');
        return separator < 0 ? normalized : normalized[..separator];
    }

    public static double NormalizeRate(double value) =>
        Math.Round(Math.Clamp(double.IsFinite(value) ? value : 1, .5, 2.5), 2);

    public static double NormalizePitch(double value) =>
        Math.Round(Math.Clamp(double.IsFinite(value) ? value : 1, .5, 2), 2);

    public static double NormalizeVolume(double value) =>
        Math.Round(Math.Clamp(double.IsFinite(value) ? value : 1, 0, 1), 2);

    private static SpeechResolution? ResolveWithinProvider(
        SpeechProviderDescriptor provider,
        IReadOnlyCollection<SpeechVoiceDescriptor> allVoices,
        string? requestedVoice,
        string language,
        SpeechPreferences preferences)
    {
        var providerVoices = allVoices
            .Where(x => string.Equals(
                x.ProviderId,
                provider.Id,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (requestedVoice is not null)
        {
            var selected = providerVoices.FirstOrDefault(
                x => string.Equals(x.VoiceId, requestedVoice, StringComparison.OrdinalIgnoreCase));

            if (selected is not null && IsLanguageCompatible(selected.Language, language))
            {
                return Build(provider, selected, language, preferences, false, "selected-voice");
            }
        }

        var exact = providerVoices
            .Where(x => LanguageRank(x.Language, language) == 0)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.VoiceId, StringComparer.Ordinal)
            .FirstOrDefault();

        if (exact is not null)
        {
            return Build(provider, exact, language, preferences, false, "exact-language");
        }

        var baseMatch = providerVoices
            .Where(x => LanguageRank(x.Language, language) == 1)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.VoiceId, StringComparer.Ordinal)
            .FirstOrDefault();

        if (baseMatch is not null)
        {
            return Build(provider, baseMatch, language, preferences, false, "base-language");
        }

        if (provider.Capabilities.HasFlag(SpeechProviderCapabilities.PlatformDefaultVoice))
        {
            return Build(provider, null, language, preferences, true, "platform-default");
        }

        return null;
    }

    private static SpeechResolution Build(
        SpeechProviderDescriptor provider,
        SpeechVoiceDescriptor? voice,
        string language,
        SpeechPreferences preferences,
        bool usesProviderDefaultVoice,
        string reason) =>
        new(
            ProviderId: provider.Id,
            VoiceId: voice?.VoiceId,
            Language: language,
            Rate: NormalizeRate(preferences.Rate),
            Pitch: NormalizePitch(preferences.Pitch),
            Volume: NormalizeVolume(preferences.Volume),
            UsesProviderDefaultVoice: usesProviderDefaultVoice,
            Reason: reason);

    private static bool IsLanguageCompatible(string voiceLanguage, string requestedLanguage) =>
        LanguageRank(voiceLanguage, requestedLanguage) <= 1 ||
        string.Equals(requestedLanguage, "und", StringComparison.OrdinalIgnoreCase);

    private static int LanguageRank(string voiceLanguage, string requestedLanguage)
    {
        var voice = NormalizeLanguageTag(voiceLanguage);
        var requested = NormalizeLanguageTag(requestedLanguage);

        if (string.Equals(requested, "und", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(voice, requested, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return string.Equals(
            BaseLanguage(voice),
            BaseLanguage(requested),
            StringComparison.OrdinalIgnoreCase)
            ? 1
            : 2;
    }

    private static int ProviderRank(SpeechProviderKind kind) => kind switch
    {
        SpeechProviderKind.OfflineNeural => 0,
        SpeechProviderKind.Device => 1,
        SpeechProviderKind.Cloud => 2,
        _ => int.MaxValue
    };

    private static string ProviderFallbackReason(SpeechProviderKind kind) => kind switch
    {
        SpeechProviderKind.OfflineNeural => "offline-neural-fallback",
        SpeechProviderKind.Device => "device-fallback",
        SpeechProviderKind.Cloud => "cloud-fallback",
        _ => "provider-fallback"
    };

    private static string? NormalizeId(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
