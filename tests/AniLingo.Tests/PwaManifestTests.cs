using System.Text.Json;
using System.Text.RegularExpressions;
using AniLingo.Web.Features.Localization;

namespace AniLingo.Tests;

[TestClass]
public sealed class PwaManifestTests
{
    [TestMethod]
    public void ManifestAndRuntimeCoverInstallOfflineAndPlatformCapabilities()
    {
        var repoRoot = FindRepositoryRoot();
        var webRoot = Path.Combine(repoRoot, "src", "AniLingo.Web", "wwwroot");

        var manifestPath = Path.Combine(webRoot, "manifest.webmanifest");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;

        Assert.AreEqual("AniLingo", root.GetProperty("name").GetString());
        Assert.AreEqual("standalone", root.GetProperty("display").GetString());
        Assert.AreEqual("/", root.GetProperty("start_url").GetString());
        Assert.IsFalse(root.GetProperty("prefer_related_applications").GetBoolean());

        var icons = root.GetProperty("icons").EnumerateArray().ToArray();
        Assert.IsTrue(icons.Any(x => x.GetProperty("sizes").GetString() == "192x192"));
        Assert.IsTrue(icons.Any(x => x.GetProperty("sizes").GetString() == "512x512"));
        Assert.IsTrue(icons.Any(x =>
            x.TryGetProperty("purpose", out var purpose)
            && purpose.GetString() == "maskable"));

        foreach (var icon in icons)
        {
            var source = icon.GetProperty("src").GetString();
            Assert.IsFalse(string.IsNullOrWhiteSpace(source));

            var relative = source!.TrimStart('/')
                .Replace('/', Path.DirectorySeparatorChar);
            Assert.IsTrue(
                File.Exists(Path.Combine(webRoot, relative)),
                $"Manifest icon '{source}' does not exist.");
        }

        Assert.IsTrue(File.Exists(
            Path.Combine(webRoot, "icons", "apple-touch-icon.png")));

        var layout = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "AniLingo.Web",
            "Pages",
            "Shared",
            "_Layout.cshtml"));

        StringAssert.Contains(layout, "viewport-fit=cover");
        StringAssert.Contains(layout, "apple-mobile-web-app-capable");
        StringAssert.Contains(layout, "apple-mobile-web-app-status-bar-style");
        StringAssert.Contains(layout, "apple-touch-icon");
        StringAssert.Contains(layout, "href=\"~/css/site.css\" asp-append-version=\"true\"");
        StringAssert.Contains(layout, "src=\"~/js/pwa.js\" asp-append-version=\"true\"");

        var serviceWorker = File.ReadAllText(
            Path.Combine(webRoot, "service-worker.js"));

        StringAssert.Contains(serviceWorker, "\"/offline.html\"");
        Assert.IsFalse(serviceWorker.Contains("\"/css/site.css\"", StringComparison.Ordinal));
        Assert.IsFalse(serviceWorker.Contains("\"/js/pwa.js\"", StringComparison.Ordinal));
        StringAssert.Contains(serviceWorker, "SKIP_WAITING");
        StringAssert.Contains(serviceWorker, "CACHE_CURRENT_ASSETS");
        StringAssert.Contains(serviceWorker, "cache: \"reload\"");
        StringAssert.Contains(serviceWorker, "putLatestAsset");
        StringAssert.Contains(serviceWorker, "cached.pathname === current.pathname");
        StringAssert.Contains(serviceWorker, "request.mode === \"navigate\"");
        Assert.IsFalse(serviceWorker.Contains("\"/Learn", StringComparison.Ordinal));
        Assert.IsFalse(serviceWorker.Contains("\"/Library", StringComparison.Ordinal));
        Assert.IsFalse(serviceWorker.Contains("handler=Media", StringComparison.Ordinal));

        var pwaRuntime = File.ReadAllText(
            Path.Combine(webRoot, "js", "pwa.js"));

        StringAssert.Contains(pwaRuntime, "beforeinstallprompt");
        StringAssert.Contains(pwaRuntime, "shellLabel(\"pwa.install.appleMobile\")");
        StringAssert.Contains(pwaRuntime, "shellLabel(\"pwa.install.appleDesktop\")");
        StringAssert.Contains(
            UiTranslationResources.Get("pwa.install.appleMobile").DefaultText,
            "Add to Home Screen");
        StringAssert.Contains(
            UiTranslationResources.Get("pwa.install.appleDesktop").DefaultText,
            "Add to Dock");
        StringAssert.Contains(pwaRuntime, "apple-mobile-web-app-capable");
        StringAssert.Contains(pwaRuntime, "viewport-fit=cover");
        StringAssert.Contains(pwaRuntime, "mediaSession");
        StringAssert.Contains(pwaRuntime, "wakeLock");
        StringAssert.Contains(pwaRuntime, "requestPictureInPicture");
        StringAssert.Contains(pwaRuntime, "navigator.share");
        StringAssert.Contains(pwaRuntime, "currentFingerprintedAssets");
        StringAssert.Contains(pwaRuntime, "url.searchParams.has(\"v\")");
        StringAssert.Contains(pwaRuntime, "CACHE_CURRENT_ASSETS");
        StringAssert.Contains(pwaRuntime, "updateViaCache: \"none\"");
    }

    [TestMethod]
    public void PwaRuntimeLabelsComeFromTheUiCatalog()
    {
        var repoRoot = FindRepositoryRoot();
        var webRoot = Path.Combine(repoRoot, "src", "AniLingo.Web", "wwwroot");
        var pwaRuntime = File.ReadAllText(Path.Combine(webRoot, "js", "pwa.js"));
        var layout = File.ReadAllText(Path.Combine(
            repoRoot, "src", "AniLingo.Web", "Pages", "Shared", "_Layout.cshtml"));

        // The layout serializes exactly the pwa.* catalog subset for the runtime.
        StringAssert.Contains(layout, "id=\"app-shell-text\"");
        StringAssert.Contains(layout, "ui.WithPrefix(\"pwa.\")");

        var keys = Regex.Matches(pwaRuntime, @"shellLabel\(""(?<key>[^""]+)""\)")
            .Select(x => x.Groups["key"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.IsTrue(keys.Length >= 10);
        foreach (var key in keys)
        {
            Assert.IsTrue(key.StartsWith("pwa.", StringComparison.Ordinal), key);
            Assert.IsTrue(UiTranslationResources.TryGet(key, out _), $"Missing catalog key {key}.");
        }

        foreach (var literal in new[]
                 {
                     "\"Install\"",
                     "\"Not now\"",
                     "\"A new AniLingo version is ready.\"",
                     "\"Back online.\"",
                     "\"Link copied.\""
                 })
        {
            Assert.IsFalse(
                pwaRuntime.Contains(literal, StringComparison.Ordinal),
                $"pwa.js still hard-codes {literal}.");
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AniLingo.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AniLingo repository root.");
    }
}
