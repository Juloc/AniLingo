using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.ReaderPreferences;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Speech;

/// <summary>
/// Profile-level TTS preferences (provider, per-language voice, rate/pitch/volume) for
/// native clients. This intentionally reuses the canonical <see cref="ReaderPreference"/>
/// "default" (profile, no book) scope row and its existing Tts* columns/rules instead of
/// introducing a second store: the Reader already owns these columns and this service only
/// adds a native-client-friendly read/write surface over the same global layer. Book-,
/// genre- and type-scoped overrides and Reader-only fields (auto-continue chapters) stay
/// Reader concerns and are not exposed here.
/// </summary>
public sealed record TtsPreferencesSnapshot(
    string ProviderId,
    IReadOnlyDictionary<string, string> VoiceIds,
    double Rate,
    double Pitch,
    double Volume);

public sealed record TtsPreferencesUpdate(
    string? ProviderId = null,
    double? Rate = null,
    double? Pitch = null,
    double? Volume = null,
    string? VoiceLanguage = null,
    string? VoiceId = null);

public sealed class TtsPreferencesService(AppDbContext db, CurrentAccountContext currentAccount)
{
    public async Task<TtsPreferencesSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        var preference = await FindAsync(tracking: false, cancellationToken);
        return ToSnapshot(preference);
    }

    public async Task<TtsPreferencesSnapshot> UpdateAsync(
        TtsPreferencesUpdate update,
        CancellationToken cancellationToken = default)
    {
        var preference = await FindAsync(tracking: true, cancellationToken);
        if (preference is null)
        {
            preference = new ReaderPreference
            {
                ProfileId = currentAccount.ProfileId,
                ScopeKey = ReaderPreferenceRules.UserDefaultScope
            };
            db.ReaderPreferences.Add(preference);
        }

        if (update.ProviderId is not null)
        {
            preference.TtsProviderId = ReaderPreferenceRules.NormalizeTtsProviderId(update.ProviderId);
        }

        if (update.Rate is { } rate)
        {
            preference.TtsRate = ReaderPreferenceRules.NormalizeTtsRate(rate);
        }

        if (update.Pitch is { } pitch)
        {
            preference.TtsPitch = ReaderPreferenceRules.NormalizeTtsPitch(pitch);
        }

        if (update.Volume is { } volume)
        {
            preference.TtsVolume = ReaderPreferenceRules.NormalizeTtsVolume(volume);
        }

        if (update.VoiceLanguage is not null)
        {
            ApplyVoice(preference, update.VoiceLanguage, update.VoiceId);
        }

        preference.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToSnapshot(preference);
    }

    private Task<ReaderPreference?> FindAsync(bool tracking, CancellationToken cancellationToken)
    {
        var query = tracking ? db.ReaderPreferences : db.ReaderPreferences.AsNoTracking();
        return query.SingleOrDefaultAsync(
            x => x.ProfileId == currentAccount.ProfileId &&
                 x.ScopeKey == ReaderPreferenceRules.UserDefaultScope,
            cancellationToken);
    }

    private static void ApplyVoice(ReaderPreference preference, string language, string? voiceId)
    {
        var normalizedLanguage = SpeechPreferenceResolver.NormalizeLanguageTag(language);
        var map = new Dictionary<string, string>(
            SpeechVoiceMap.Parse(preference.TtsVoiceIds),
            StringComparer.OrdinalIgnoreCase);
        var normalizedVoice = SpeechVoiceMap.NormalizeVoiceId(voiceId);

        if (normalizedVoice is null)
        {
            map.Remove(normalizedLanguage);
        }
        else if (map.Count < SpeechVoiceMap.MaxEntries || map.ContainsKey(normalizedLanguage))
        {
            map[normalizedLanguage] = normalizedVoice;
        }

        preference.TtsVoiceIds = SpeechVoiceMap.Serialize(map);
    }

    private static TtsPreferencesSnapshot ToSnapshot(ReaderPreference? preference) =>
        new(
            ProviderId: ReaderPreferenceRules.NormalizeTtsProviderId(preference?.TtsProviderId),
            VoiceIds: SpeechVoiceMap.Parse(preference?.TtsVoiceIds),
            Rate: ReaderPreferenceRules.NormalizeTtsRate(preference?.TtsRate ?? 1),
            Pitch: ReaderPreferenceRules.NormalizeTtsPitch(preference?.TtsPitch ?? 1),
            Volume: ReaderPreferenceRules.NormalizeTtsVolume(preference?.TtsVolume ?? 1));
}
