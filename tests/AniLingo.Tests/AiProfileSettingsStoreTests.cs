using AniLingo.Web.Features.Ai;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniLingo.Tests;

[TestClass]
public sealed class AiProfileSettingsStoreTests
{
    [TestMethod]
    public async Task PersonalApiKeyIsProtectedAtRestAndRoundTrips()
    {
        var root = TempDirectory();
        var keys = Path.Combine(root, "keys");
        var integrations = Path.Combine(root, "integrations");
        Directory.CreateDirectory(keys);

        try
        {
            var dataProtection = DataProtectionProvider.Create(
                new DirectoryInfo(keys));
            var store = new AiProfileSettingsStore(
                dataProtection,
                NullLogger<AiProfileSettingsStore>.Instance,
                new DirectoryInfo(integrations));

            const string secret = "test-secret-api-key";
            var expected = new AiProfileSettings(
                AiProviderIds.OpenAiCompatible,
                "https://example.invalid/v1",
                "test-model",
                secret,
                AiTranslationMode.Quality);

            await store.SaveAsync(
                "profile-test",
                expected,
                CancellationToken.None);

            var path = Path.Combine(
                integrations,
                "ai",
                "accounts",
                "profile-test.json");
            var persisted = await File.ReadAllTextAsync(path);

            Assert.IsFalse(
                persisted.Contains(secret, StringComparison.Ordinal),
                "The raw API key must never be written to disk.");

            var reloaded = await store.LoadAsync(
                "profile-test",
                CancellationToken.None);

            Assert.AreEqual(expected, reloaded);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task ResetReturnsToEfficientServerDefaults()
    {
        var root = TempDirectory();
        var keys = Path.Combine(root, "keys");
        var integrations = Path.Combine(root, "integrations");
        Directory.CreateDirectory(keys);

        try
        {
            var store = new AiProfileSettingsStore(
                DataProtectionProvider.Create(new DirectoryInfo(keys)),
                NullLogger<AiProfileSettingsStore>.Instance,
                new DirectoryInfo(integrations));

            await store.SaveAsync(
                "profile-test",
                new AiProfileSettings(
                    AiProviderIds.OpenAiCompatible,
                    "https://example.invalid/v1",
                    "test-model",
                    "secret",
                    AiTranslationMode.Maximum),
                CancellationToken.None);

            await store.ResetAsync(
                "profile-test",
                CancellationToken.None);

            var reloaded = await store.LoadAsync(
                "profile-test",
                CancellationToken.None);

            Assert.AreEqual(AiProfileSettings.Default, reloaded);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "anilingo-ai-profile-settings",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
