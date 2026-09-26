using System.Text.Json;

namespace AniLingo.Web.Features.Speech;

/// <summary>
/// One downloadable file that belongs to an offline neural TTS model pack. The client
/// downloads every file, verifies <see cref="Sha256"/> and only then activates the model.
/// </summary>
public sealed record SpeechModelFile(
    string Name,
    string Url,
    long SizeBytes,
    string Sha256);

/// <summary>
/// One explicit, versioned, downloadable offline-neural voice pack. See docs/TTS.md
/// (Phase 3) for the activation contract: download every file, verify every checksum,
/// then atomically activate. A manifest entry never implies support for a language it
/// does not list.
/// </summary>
public sealed record SpeechModelManifestEntry(
    string ProviderId,
    string ModelId,
    string Version,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Voices,
    IReadOnlyList<SpeechModelFile> Files,
    long TotalSizeBytes,
    string MinimumCompatibleVersion);

public sealed record SpeechModelManifest(IReadOnlyList<SpeechModelManifestEntry> Entries)
{
    public static readonly SpeechModelManifest Empty = new([]);
}

/// <summary>
/// Parses and validates the owner-configurable model manifest JSON. Invalid or
/// incomplete entries are dropped rather than served, so a client never sees a half
/// specified model it cannot safely verify.
/// </summary>
public static class SpeechModelManifestParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static SpeechModelManifest Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return SpeechModelManifest.Empty;
        }

        SpeechModelManifestDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<SpeechModelManifestDocument>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return SpeechModelManifest.Empty;
        }

        if (document?.Models is null)
        {
            return SpeechModelManifest.Empty;
        }

        var entries = document.Models
            .Select(TryBuildEntry)
            .Where(entry => entry is not null)
            .Select(entry => entry!)
            .ToArray();

        return new SpeechModelManifest(entries);
    }

    private static SpeechModelManifestEntry? TryBuildEntry(SpeechModelManifestModelDocument model)
    {
        if (string.IsNullOrWhiteSpace(model.ProviderId) ||
            string.IsNullOrWhiteSpace(model.ModelId) ||
            string.IsNullOrWhiteSpace(model.Version))
        {
            return null;
        }

        var languages = (model.Languages ?? [])
            .Select(x => SpeechPreferenceResolver.NormalizeLanguageTag(x))
            .Where(x => x != "und")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (languages.Length == 0)
        {
            return null;
        }

        var voices = (model.Voices ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var files = (model.Files ?? [])
            .Select(TryBuildFile)
            .Where(file => file is not null)
            .Select(file => file!)
            .ToArray();

        if (files.Length == 0 || files.Length != (model.Files?.Count ?? 0))
        {
            // Any unparsable file entry invalidates the whole model: a client must never
            // be offered a pack it cannot fully verify.
            return null;
        }

        var totalSize = files.Sum(x => x.SizeBytes);

        return new SpeechModelManifestEntry(
            ProviderId: model.ProviderId.Trim(),
            ModelId: model.ModelId.Trim(),
            Version: model.Version.Trim(),
            Languages: languages,
            Voices: voices,
            Files: files,
            TotalSizeBytes: totalSize,
            MinimumCompatibleVersion: string.IsNullOrWhiteSpace(model.MinimumCompatibleVersion)
                ? "0.0.0"
                : model.MinimumCompatibleVersion.Trim());
    }

    private static SpeechModelFile? TryBuildFile(SpeechModelManifestFileDocument file)
    {
        if (string.IsNullOrWhiteSpace(file.Name) ||
            string.IsNullOrWhiteSpace(file.Url) ||
            string.IsNullOrWhiteSpace(file.Sha256) ||
            file.SizeBytes <= 0)
        {
            return null;
        }

        var sha256 = file.Sha256.Trim();
        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
        {
            return null;
        }

        return new SpeechModelFile(
            Name: file.Name.Trim(),
            Url: file.Url.Trim(),
            SizeBytes: file.SizeBytes,
            Sha256: sha256.ToLowerInvariant());
    }

    private sealed class SpeechModelManifestDocument
    {
        public List<SpeechModelManifestModelDocument>? Models { get; set; }
    }

    private sealed class SpeechModelManifestModelDocument
    {
        public string? ProviderId { get; set; }
        public string? ModelId { get; set; }
        public string? Version { get; set; }
        public List<string>? Languages { get; set; }
        public List<string>? Voices { get; set; }
        public List<SpeechModelManifestFileDocument>? Files { get; set; }
        public string? MinimumCompatibleVersion { get; set; }
    }

    private sealed class SpeechModelManifestFileDocument
    {
        public string? Name { get; set; }
        public string? Url { get; set; }
        public long SizeBytes { get; set; }
        public string? Sha256 { get; set; }
    }
}
