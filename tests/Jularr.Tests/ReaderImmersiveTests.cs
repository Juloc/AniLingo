namespace Jularr.Tests;

[TestClass]
public sealed class ReaderImmersiveTests
{
    [TestMethod]
    public void ReaderExposesIndependentWakeLockAndImmersiveControls()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(
            root, "src", "Jularr.Web", "Pages", "Novels", "Read.cshtml"));
        var script = File.ReadAllText(Path.Combine(
            root, "src", "Jularr.Web", "wwwroot", "js", "reader-personalization.js"));
        var shellCss = File.ReadAllText(Path.Combine(
            root, "src", "Jularr.Web", "wwwroot", "css", "reader-shell.css"));

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

        StringAssert.Contains(shellCss, ".reader-frame:fullscreen");
        StringAssert.Contains(shellCss, ".reader-frame-icon[aria-pressed=\"true\"]");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Jularr.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Jularr repository root.");
    }
}
