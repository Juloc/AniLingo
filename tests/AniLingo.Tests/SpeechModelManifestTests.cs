using AniLingo.Web.Features.Speech;

namespace AniLingo.Tests;

[TestClass]
public sealed class SpeechModelManifestTests
{
    private static readonly string ValidSha256 = new('a', 64);

    [TestMethod]
    public void BlankOrInvalidJsonYieldsAnEmptyManifest()
    {
        Assert.AreEqual(0, SpeechModelManifestParser.Parse(null).Entries.Count);
        Assert.AreEqual(0, SpeechModelManifestParser.Parse("").Entries.Count);
        Assert.AreEqual(0, SpeechModelManifestParser.Parse("not json").Entries.Count);
        Assert.AreEqual(0, SpeechModelManifestParser.Parse("""{"models":[]}""").Entries.Count);
    }

    [TestMethod]
    public void ValidEntryParsesLanguagesVoicesAndTotalSize()
    {
        var json = $$"""
        {
          "models": [
            {
              "providerId": "sherpa-onnx",
              "modelId": "piper-de-thorsten",
              "version": "1.0.0",
              "languages": ["de_DE"],
              "voices": ["thorsten"],
              "files": [
                { "name": "model.onnx", "url": "https://example.invalid/model.onnx", "sizeBytes": 1000, "sha256": "{{ValidSha256}}" },
                { "name": "tokens.txt", "url": "https://example.invalid/tokens.txt", "sizeBytes": 200, "sha256": "{{ValidSha256}}" }
              ],
              "minimumCompatibleVersion": "0.5.0"
            }
          ]
        }
        """;

        var manifest = SpeechModelManifestParser.Parse(json);

        Assert.AreEqual(1, manifest.Entries.Count);
        var entry = manifest.Entries[0];
        Assert.AreEqual("sherpa-onnx", entry.ProviderId);
        Assert.AreEqual("piper-de-thorsten", entry.ModelId);
        Assert.AreEqual("1.0.0", entry.Version);
        CollectionAssert.AreEqual(new[] { "de-DE" }, entry.Languages.ToArray());
        CollectionAssert.AreEqual(new[] { "thorsten" }, entry.Voices.ToArray());
        Assert.AreEqual(2, entry.Files.Count);
        Assert.AreEqual(1200, entry.TotalSizeBytes);
        Assert.AreEqual("0.5.0", entry.MinimumCompatibleVersion);
    }

    [TestMethod]
    public void EntryWithoutAnyRecognizedLanguageIsDropped()
    {
        var json = $$"""
        {
          "models": [
            {
              "providerId": "sherpa-onnx",
              "modelId": "unknown-language",
              "version": "1.0.0",
              "languages": ["!!!"],
              "files": [
                { "name": "model.onnx", "url": "https://example.invalid/model.onnx", "sizeBytes": 1000, "sha256": "{{ValidSha256}}" }
              ]
            }
          ]
        }
        """;

        Assert.AreEqual(0, SpeechModelManifestParser.Parse(json).Entries.Count);
    }

    [TestMethod]
    public void EntryWithAnUnverifiableFileIsDroppedEntirely()
    {
        var json = """
        {
          "models": [
            {
              "providerId": "sherpa-onnx",
              "modelId": "bad-checksum",
              "version": "1.0.0",
              "languages": ["ja-JP"],
              "files": [
                { "name": "model.onnx", "url": "https://example.invalid/model.onnx", "sizeBytes": 1000, "sha256": "not-hex" }
              ]
            }
          ]
        }
        """;

        Assert.AreEqual(
            0,
            SpeechModelManifestParser.Parse(json).Entries.Count,
            "A model must never be offered when even one of its files cannot be verified.");
    }

    [TestMethod]
    public void MissingRequiredIdentifiersDropTheEntry()
    {
        var json = $$"""
        {
          "models": [
            {
              "modelId": "no-provider",
              "version": "1.0.0",
              "languages": ["ja-JP"],
              "files": [
                { "name": "model.onnx", "url": "https://example.invalid/model.onnx", "sizeBytes": 1000, "sha256": "{{ValidSha256}}" }
              ]
            }
          ]
        }
        """;

        Assert.AreEqual(0, SpeechModelManifestParser.Parse(json).Entries.Count);
    }

    [TestMethod]
    public async Task StoreServesTheBundledDefaultManifestWhenNoOverrideExists()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), $"anilingo-tts-manifest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataRoot);
        try
        {
            var store = new SpeechModelManifestStore(dataRoot);
            var manifest = await store.LoadAsync();

            Assert.AreEqual(
                0,
                manifest.Entries.Count,
                "The bundled default manifest ships with zero models until an owner pins verified downloads.");
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task StorePrefersAnOwnerOverrideFileOverTheBundledDefault()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), $"anilingo-tts-manifest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dataRoot, "speech"));
        try
        {
            var json = $$"""
            {
              "models": [
                {
                  "providerId": "sherpa-onnx",
                  "modelId": "owner-pinned",
                  "version": "1.0.0",
                  "languages": ["ja-JP"],
                  "files": [
                    { "name": "model.onnx", "url": "https://example.invalid/model.onnx", "sizeBytes": 1, "sha256": "{{ValidSha256}}" }
                  ]
                }
              ]
            }
            """;
            await File.WriteAllTextAsync(
                Path.Combine(dataRoot, "speech", "tts-model-manifest.json"), json);

            var store = new SpeechModelManifestStore(dataRoot);
            var manifest = await store.LoadAsync();

            Assert.AreEqual(1, manifest.Entries.Count);
            Assert.AreEqual("owner-pinned", manifest.Entries[0].ModelId);
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }
}
