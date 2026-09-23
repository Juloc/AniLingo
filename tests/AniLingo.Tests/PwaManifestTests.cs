using System.Text.Json;

namespace AniLingo.Tests;

[TestClass]
public sealed class PwaManifestTests
{
    [TestMethod]
    public void ManifestIsValidAndServiceWorkerKeepsPrivateRoutesOutOfPrecache()
    {
        var repoRoot = FindRepositoryRoot();
        var webRoot = Path.Combine(repoRoot, "src", "AniLingo.Web", "wwwroot");

        var manifestPath = Path.Combine(webRoot, "manifest.webmanifest");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;

        Assert.AreEqual("AniLingo", root.GetProperty("name").GetString());
        Assert.AreEqual("standalone", root.GetProperty("display").GetString());
        Assert.AreEqual("/", root.GetProperty("start_url").GetString());

        var icons = root.GetProperty("icons");
        Assert.IsTrue(icons.GetArrayLength() > 0);

        var serviceWorker = File.ReadAllText(
            Path.Combine(webRoot, "service-worker.js"));

        StringAssert.Contains(serviceWorker, "\"/offline.html\"");
        StringAssert.Contains(serviceWorker, "\"/css/site.css\"");
        Assert.IsFalse(serviceWorker.Contains("\"/Learn", StringComparison.Ordinal));
        Assert.IsFalse(serviceWorker.Contains("\"/Library", StringComparison.Ordinal));
        Assert.IsFalse(serviceWorker.Contains("handler=Media", StringComparison.Ordinal));
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
