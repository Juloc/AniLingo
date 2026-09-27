namespace AniLingo.Web.Features.Speech;

/// <summary>
/// Serves the offline-neural model manifest (see docs/TTS.md, Phase 3). The manifest is a
/// small JSON document, never model bytes: an owner can drop an override file under
/// <c>{dataRoot}/speech/tts-model-manifest.json</c> without rebuilding the image or APK.
/// With no override present the bundled default manifest (embedded in the assembly) is
/// served, which lists zero models until an owner pins verified downloads (provider/model
/// id, version, languages, voices, file URLs, byte sizes and SHA-256) so Jularr never
/// claims a language or voice it cannot verify.
/// </summary>
public sealed class SpeechModelManifestStore
{
    private const string BundledResource = "AniLingo.Speech.tts-model-manifest.json";

    private readonly string overridePath;

    public SpeechModelManifestStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        overridePath = Path.Combine(dataRoot, "speech", "tts-model-manifest.json");
    }

    public async Task<SpeechModelManifest> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(overridePath))
        {
            var overrideJson = await File.ReadAllTextAsync(overridePath, cancellationToken);
            return SpeechModelManifestParser.Parse(overrideJson);
        }

        using var stream = typeof(SpeechModelManifestStore).Assembly
            .GetManifestResourceStream(BundledResource);
        if (stream is null)
        {
            return SpeechModelManifest.Empty;
        }

        using var reader = new StreamReader(stream);
        var bundledJson = await reader.ReadToEndAsync(cancellationToken);
        return SpeechModelManifestParser.Parse(bundledJson);
    }
}
