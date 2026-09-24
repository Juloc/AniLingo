namespace AniLingo.Tests;

[TestClass]
public sealed class ReaderImmersiveTests
{
    [TestMethod]
    public void ReaderExposesIndependentWakeLockAndImmersiveControls()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(
            root, "src", "AniLingo.Web", "Pages", "Novels", "Read.cshtml"));
        var script = File.ReadAllText(Path.Combine(
            root, "src", "AniLingo.Web", "wwwroot", "js", "reader-personalization.js"));
        var css = File.ReadAllText(Path.Combine(
            root, "src", "AniLingo.Web", "wwwroot", "css", "novels.css"));

        StringAssert.Contains(page, "data-reader-autoscroll-toggle");
        StringAssert.Contains(page, "data-reader-wake-lock-toggle");
        StringAssert.Contains(page, "data-reader-immersive-toggle");

        StringAssert.Contains(script, "navigator.wakeLock.request(\"screen\")");
        StringAssert.Contains(script, ".novel.keepAwake");
        StringAssert.Contains(script, "readKeepAwakePreference");
        StringAssert.Contains(script, "document.visibilityState === \"visible\"");
        StringAssert.Contains(script, "wakeLockButton?.addEventListener(\"click\"");
        StringAssert.Contains(script, "autoScrollButton?.addEventListener(\"click\", toggleAutoScroll)");
        StringAssert.Contains(script, "requestReaderFullscreen");
        StringAssert.Contains(script, "reader-immersive-fallback");
        StringAssert.Contains(script, "fullscreenchange");

        StringAssert.Contains(css, ".novel-reader-shell:fullscreen");
        StringAssert.Contains(css, "env(safe-area-inset-top)");
        StringAssert.Contains(css, ".novel-icon-button.is-active");
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
