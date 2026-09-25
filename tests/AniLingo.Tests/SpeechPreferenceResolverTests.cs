using AniLingo.Web.Features.Speech;

namespace AniLingo.Tests;

[TestClass]
public sealed class SpeechPreferenceResolverTests
{
    [TestMethod]
    public void ExplicitCompatibleVoiceWins()
    {
        var result = SpeechPreferenceResolver.Resolve(
            new SpeechPreferences(
                ProviderId: "device",
                VoiceId: "ja-selected",
                Language: "ja-JP",
                Rate: 1.2),
            Providers(),
            [
                new("device", "ja-default", "Japanese Default", "ja-JP", IsDefault: true),
                new("device", "ja-selected", "Japanese Selected", "ja-JP")
            ]);

        Assert.IsNotNull(result);
        Assert.AreEqual("device", result.ProviderId);
        Assert.AreEqual("ja-selected", result.VoiceId);
        Assert.AreEqual("selected-provider", result.Reason);
        Assert.AreEqual(1.2, result.Rate);
    }

    [TestMethod]
    public void ExactLanguageBeatsBaseLanguage()
    {
        var result = SpeechPreferenceResolver.Resolve(
            new SpeechPreferences(Language: "de-DE"),
            Providers(),
            [
                new("offline", "de-at", "Deutsch AT", "de-AT", IsDefault: true),
                new("offline", "de-de", "Deutsch DE", "de-DE")
            ]);

        Assert.IsNotNull(result);
        Assert.AreEqual("offline", result.ProviderId);
        Assert.AreEqual("de-de", result.VoiceId);
        Assert.AreEqual("offline-neural-fallback", result.Reason);
    }

    [TestMethod]
    public void ProviderFallbackOrderIsOfflineThenDeviceThenCloud()
    {
        var result = SpeechPreferenceResolver.Resolve(
            new SpeechPreferences(Language: "id-ID"),
            Providers(),
            [
                new("cloud", "id-cloud", "Cloud Indonesian", "id-ID"),
                new("device", "id-device", "Device Indonesian", "id-ID"),
                new("offline", "id-offline", "Offline Indonesian", "id-ID")
            ]);

        Assert.IsNotNull(result);
        Assert.AreEqual("offline", result.ProviderId);
        Assert.AreEqual("id-offline", result.VoiceId);
    }

    [TestMethod]
    public void UnavailableSelectedProviderFallsBackDeterministically()
    {
        var providers = Providers()
            .Select(x => x.Id == "offline" ? x with { IsAvailable = false } : x)
            .ToArray();

        var result = SpeechPreferenceResolver.Resolve(
            new SpeechPreferences(
                ProviderId: "offline",
                VoiceId: "missing",
                Language: "ja-JP"),
            providers,
            [
                new("device", "ja-device", "Device Japanese", "ja")
            ]);

        Assert.IsNotNull(result);
        Assert.AreEqual("device", result.ProviderId);
        Assert.AreEqual("ja-device", result.VoiceId);
        Assert.AreEqual("device-fallback", result.Reason);
    }

    [TestMethod]
    public void DeviceProviderUsesPlatformDefaultInsteadOfWrongLanguageVoice()
    {
        var result = SpeechPreferenceResolver.Resolve(
            new SpeechPreferences(Language: "ja-JP"),
            Providers(offlineAvailable: false),
            [
                new("device", "de-device", "German", "de-DE", IsDefault: true),
                new("cloud", "ja-cloud", "Japanese Cloud", "ja-JP")
            ]);

        Assert.IsNotNull(result);
        Assert.AreEqual("device", result.ProviderId);
        Assert.IsNull(result.VoiceId);
        Assert.IsTrue(result.UsesProviderDefaultVoice);
    }

    [TestMethod]
    public void LanguageAndProsodyAreNormalized()
    {
        Assert.AreEqual("ja-JP", SpeechPreferenceResolver.NormalizeLanguageTag("JA_jp"));
        Assert.AreEqual("zh-Hant-TW", SpeechPreferenceResolver.NormalizeLanguageTag("ZH_hant_tw"));
        Assert.AreEqual("und", SpeechPreferenceResolver.NormalizeLanguageTag("bad tag!"));

        var result = SpeechPreferenceResolver.Resolve(
            new SpeechPreferences(
                Language: "de_de",
                Rate: 100,
                Pitch: -2,
                Volume: double.NaN),
            Providers(offlineAvailable: false),
            []);

        Assert.IsNotNull(result);
        Assert.AreEqual("de-DE", result.Language);
        Assert.AreEqual(2.5, result.Rate);
        Assert.AreEqual(.5, result.Pitch);
        Assert.AreEqual(1d, result.Volume);
    }

    private static SpeechProviderDescriptor[] Providers(bool offlineAvailable = true) =>
    [
        new(
            "offline",
            "Offline neural",
            SpeechProviderKind.OfflineNeural,
            offlineAvailable,
            SpeechProviderCapabilities.PauseResume |
            SpeechProviderCapabilities.BoundaryEvents |
            SpeechProviderCapabilities.GuaranteedOffline),
        new(
            "device",
            "Device",
            SpeechProviderKind.Device,
            true,
            SpeechProviderCapabilities.PauseResume |
            SpeechProviderCapabilities.BoundaryEvents |
            SpeechProviderCapabilities.PlatformDefaultVoice),
        new(
            "cloud",
            "Cloud",
            SpeechProviderKind.Cloud,
            true,
            SpeechProviderCapabilities.PauseResume |
            SpeechProviderCapabilities.BoundaryEvents)
    ];
}
